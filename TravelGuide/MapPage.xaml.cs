using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Devices.Sensors;
using System.Globalization;
#if ANDROID
using Android.OS;
using Android.Webkit;
#endif
using System.Text;
using System.Text.Json;
using TravelGuide.Models;

namespace TravelGuide;

/// <summary>
/// Trang bản đồ WebView: hiển thị POI, vị trí user, highlight POI đang trong geofence,
/// và bridge HTTPS <see cref="MapJsBridgeRoot"/> (Android không tin cậy <c>app://</c> cho Navigating). Bản đồ: Leaflet + OSM.
/// </summary>
public partial class MapPage : ContentPage
{
    private readonly DatabaseService _dbService;
    private readonly NarrationEngine _narrationEngine;
    private readonly GpsBackgroundService _gpsService;
    private readonly GeofenceEngine _geofenceEngine;

    private Location? _lastKnownLocation;
    private TouristPlace? _nearestPlace;

    /// <summary>HTTPS + host .invalid: Android WebView thường không gọi <see cref="MapView_Navigating"/> với <c>app://</c> → overlay "Đang tải bản đồ" kẹt vĩnh viễn.</summary>
    private const string MapJsBridgeRoot = "https://tg.invalid";

    private CancellationTokenSource? _mapLoadingFallbackCts;

    /// <summary>Chọn chuỗi theo ngôn ngữ UI hiện tại (<see cref="AppLanguage"/>).</summary>
    private static string T(string vi, string en, string ja, string ko, string zh) =>
        AppLanguage.Current switch
        {
            "en" => en,
            "ja" => ja,
            "ko" => ko,
            "zh" => zh,
            _ => vi
        };

    private static string F(double d) => d.ToString(CultureInfo.InvariantCulture);

    public MapPage(DatabaseService dbService, NarrationEngine narrationEngine, GpsBackgroundService gpsService, GeofenceEngine geofenceEngine)
    {
        InitializeComponent();
#if ANDROID
        mapView.HandlerChanged += (_, _) =>
        {
            if (mapView.Handler?.PlatformView is not Android.Webkit.WebView awv)
                return;
            var s = awv.Settings;
            s.JavaScriptEnabled = true;
            s.DomStorageEnabled = true;
            s.DatabaseEnabled = true;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
                s.MixedContentMode = MixedContentHandling.AlwaysAllow;
        };
#endif
        _dbService = dbService;
        _narrationEngine = narrationEngine;
        _gpsService = gpsService;
        _geofenceEngine = geofenceEngine;

        WeakReferenceMessenger.Default.Register<LocationMessage>(this, (r, m) =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                _lastKnownLocation = m.Value;
                if (mapView != null)
                {
                    await mapView.EvaluateJavaScriptAsync($"updateLocation({F(m.Value.Longitude)}, {F(m.Value.Latitude)});");
                    await UpdateNearbyBanner(m.Value);
                }
            });
        });

        _geofenceEngine.OnPoiEntered += OnPoiEntered;
        _geofenceEngine.OnPoiTriggered += OnPoiTriggered;
        _geofenceEngine.OnPoiExited += OnPoiExited;
    }

    /// <summary>Khi trang hiện: gắn mini player và bật vòng lặp GPS.</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        MiniPlayer.Attach(_narrationEngine);
        _dbService.ClearCache();
        LoadMap();
        await _gpsService.StartAsync();
    }

    /// <summary>Khi rời trang: tắt GPS và gỡ đăng ký sự kiện geofence / messenger.</summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _gpsService.Stop();
        WeakReferenceMessenger.Default.Unregister<LocationMessage>(this);
        _geofenceEngine.OnPoiEntered -= OnPoiEntered;
        _geofenceEngine.OnPoiTriggered -= OnPoiTriggered;
        _geofenceEngine.OnPoiExited -= OnPoiExited;
        CancelMapLoadingFallback();
    }

    /// <summary>Callback <see cref="GeofenceEngine.OnPoiEntered"/>: banner + highlight marker.</summary>
    private void OnPoiEntered(TouristPlace poi)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _nearestPlace = poi;
            LblNearbyTitle.Text = poi.Name;
            LblNearbyDist.Text = T("Bạn đang trong vùng này", "You are in this area", "このエリアにいます", "이 구역에 있습니다", "您正在此区域内");
            NearbyBanner.IsVisible = true;
            _ = mapView.EvaluateJavaScriptAsync($"highlightMarker({F(poi.Longitude)}, {F(poi.Latitude)});");
            _ = MarkPoiVisitedAsync(poi);
        });
    }

    private async Task MarkPoiVisitedAsync(TouristPlace poi)
    {
        var changed = await _dbService.MarkPlaceVisitedAsync(poi.Id);
        if (!changed) return;

        poi.IsVisited = true;
        await mapView.EvaluateJavaScriptAsync($"markVisitedMarker({poi.Id});");
    }

    /// <summary>Callback khi geofence quyết định phát thuyết minh: cập nhật text banner.</summary>
    private void OnPoiTriggered(TouristPlace poi)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            NearbyBanner.BackgroundColor = Color.FromArgb("#2E7D32");
            LblNearbyDist.Text = T("Đang phát thuyết minh...", "Playing commentary...", "解説を再生中...", "해설 재생 중...", "正在播放讲解...");
        });
    }

    /// <summary>Rời khỏi mọi POI: ẩn banner và bỏ highlight.</summary>
    private void OnPoiExited()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            NearbyBanner.IsVisible = false;
            NearbyBanner.BackgroundColor = Color.FromArgb("#2E86DE");
            _ = mapView.EvaluateJavaScriptAsync("clearHighlight();");
        });
    }

    /// <summary>Nút tải lại: rebuild HTML bản đồ (POI trong 1km).</summary>
    private void OnReloadClicked(object sender, EventArgs e)
    {
        LoadingOverlay.IsVisible = true;
        _dbService.ClearCache();
        LoadMap();
    }

    /// <summary>Phát thủ công POI đang được coi là “gần nhất” trên banner.</summary>
    private async void OnSpeakNearbyClicked(object sender, EventArgs e)
    {
        if (_nearestPlace != null) await _narrationEngine.SpeakExclusiveAsync(_nearestPlace);
    }

    /// <summary>Xử lý URL từ JS (<see cref="MapJsBridgeRoot"/>). Hỗ trợ cũ <c>app://</c> nếu còn sót.</summary>
    private async void MapView_Navigating(object sender, WebNavigatingEventArgs e)
    {
        if (!TryMapBridgeUri(e.Url, out var u))
            return;
        e.Cancel = true;

        var path = u.AbsolutePath.Trim('/').ToLowerInvariant();
        switch (path)
        {
            case "error":
            {
                var errorMsg = Uri.UnescapeDataString(QueryFirst(u, "msg") ?? "");
                CancelMapLoadingFallback();
                LoadingOverlay.IsVisible = false;
                LblGpsStatus.Text = $"JS ERR: {errorMsg[..Math.Min(errorMsg.Length, 60)]}";
                break;
            }
            case "speak":
            {
                var parsed = ParseSpeakUrl(u);
                var places = await _dbService.GetPlacesAsync();
                TouristPlace? matched = null;
                if (parsed.PoiId > 0)
                    matched = places.FirstOrDefault(p => p.Id == parsed.PoiId);

                if (matched is null && !string.IsNullOrWhiteSpace(parsed.RawText))
                {
                    var raw = parsed.RawText;
                    matched = places.FirstOrDefault(p =>
                        raw.StartsWith(p.Name, StringComparison.OrdinalIgnoreCase) ||
                        raw.Contains(p.Name, StringComparison.OrdinalIgnoreCase));
                }

                if (matched != null)
                    await _narrationEngine.SpeakExclusiveAsync(matched);
                break;
            }
            case "locate":
                await GetUserLocationAsync();
                break;
            case "loaded":
                CancelMapLoadingFallback();
                LoadingOverlay.IsVisible = false;
                UpdateGpsStatus(T("Đã tải bản đồ", "Map ready", "地図を読み込みました", "지도 로드 완료", "地图已加载"), "#4CAF50");
                break;
            case "scan":
            {
                var poiId = ParseScanPoiId(u);
                if (poiId <= 0) return;
                await Shell.Current.GoToAsync($"{nameof(QrScannerPage)}?payload={Uri.EscapeDataString(poiId.ToString())}");
                break;
            }
            case "open":
            {
                var poiId = ParseOpenPoiId(u);
                if (poiId <= 0) return;
                await OpenPlaceDetailFromMapAsync(poiId);
                break;
            }
        }
    }

    private static bool TryMapBridgeUri(string rawUrl, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(rawUrl))
            return false;

        if (rawUrl.StartsWith($"{MapJsBridgeRoot}/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rawUrl, MapJsBridgeRoot, StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var u))
                return false;
            if (!string.Equals(u.Host, "tg.invalid", StringComparison.OrdinalIgnoreCase))
                return false;
            uri = u;
            return true;
        }

        if (!rawUrl.StartsWith("app://", StringComparison.OrdinalIgnoreCase))
            return false;

        var tail = rawUrl.AsSpan("app://".Length).TrimStart('/');
        var rebuilt = $"{MapJsBridgeRoot}/{tail}";
        if (!Uri.TryCreate(rebuilt, UriKind.Absolute, out var u2))
            return false;
        if (!string.Equals(u2.Host, "tg.invalid", StringComparison.OrdinalIgnoreCase))
            return false;
        uri = u2;
        return true;
    }

    private static string? QueryFirst(Uri uri, string name)
    {
        var q = uri.Query;
        if (string.IsNullOrEmpty(q) || q[0] != '?')
            return null;
        foreach (var part in q.AsSpan(1).ToString().Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            if (!part[..eq].Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            return Uri.UnescapeDataString(part[(eq + 1)..]);
        }

        return null;
    }

    private void CancelMapLoadingFallback()
    {
        try
        {
            _mapLoadingFallbackCts?.Cancel();
        }
        catch
        {
            // ignored
        }

        _mapLoadingFallbackCts?.Dispose();
        _mapLoadingFallbackCts = null;
    }

    /// <summary>Nếu bridge không về (mạng/WebView), ẩn overlay để user không kẹt vĩnh viễn.</summary>
    private void ArmMapLoadingFallback()
    {
        CancelMapLoadingFallback();
        _mapLoadingFallbackCts = new CancellationTokenSource();
        var token = _mapLoadingFallbackCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(22000, token).ConfigureAwait(false);
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!LoadingOverlay.IsVisible)
                    return;
                LoadingOverlay.IsVisible = false;
                UpdateGpsStatus(
                    T("Bản đồ tải lâu — thử mạng hoặc bấm 🔄", "Map slow — check network or tap 🔄", "地図が遅い — 通信か🔄", "지도 지연 — 네트워크 또는 🔄", "地图较慢 — 检查网络或点🔄"),
                    "#FF9800");
            });
        }, token);
    }

    /// <summary>Lấy GPS một lần (nút trên map), cập nhật marker và banner gần POI.</summary>
    private async Task GetUserLocationAsync()
    {
        try
        {
            UpdateGpsStatus(T("Đang định vị...", "Locating...", "位置取得中...", "위치 찾는 중...", "定位中..."), "#FFA726");
            var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)));
            if (location == null)
            {
                UpdateGpsStatus(T("Không có GPS", "No GPS", "GPSなし", "GPS 없음", "无GPS"), "#EF5350");
                return;
            }

            _lastKnownLocation = location;
            UpdateGpsStatus(T($"Độ chính xác: {location.Accuracy:F0}m", $"Accuracy: {location.Accuracy:F0}m", $"精度: {location.Accuracy:F0}m", $"정확도: {location.Accuracy:F0}m", $"精度: {location.Accuracy:F0}m"), "#4CAF50");
            await mapView.EvaluateJavaScriptAsync($"updateLocation({F(location.Longitude)}, {F(location.Latitude)});");
            await UpdateNearbyBanner(location);
        }
        catch
        {
            UpdateGpsStatus(T("Lỗi GPS", "GPS Error", "GPSエラー", "GPS 오류", "GPS错误"), "#EF5350");
        }
    }

    /// <summary>So POI gần nhất với user; hiện banner nếu trong ~2× bán kính POI.</summary>
    private async Task UpdateNearbyBanner(Location userLocation)
    {
        var places = await _dbService.GetPlacesAsync();
        _nearestPlace = places
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => Location.CalculateDistance(userLocation.Latitude, userLocation.Longitude, p.Latitude, p.Longitude, DistanceUnits.Kilometers))
            .FirstOrDefault();
        if (_nearestPlace == null) return;

        double dist = Location.CalculateDistance(userLocation.Latitude, userLocation.Longitude, _nearestPlace.Latitude, _nearestPlace.Longitude, DistanceUnits.Kilometers) * 1000;
        if (dist <= _nearestPlace.Radius * 2)
        {
            LblNearbyTitle.Text = _nearestPlace.Name;
            LblNearbyDist.Text = dist < 1000 ? T($"Cách bạn {dist:F0}m", $"{dist:F0}m away", $"{dist:F0}m先", $"{dist:F0}m 거리", $"距您{dist:F0}米")
                                             : T($"Cách bạn {dist / 1000:F1}km", $"{dist / 1000:F1}km away", $"{dist / 1000:F1}km先", $"{dist / 1000:F1}km 거리", $"距您{dist / 1000:F1}公里");
            NearbyBanner.IsVisible = true;
        }
        else
        {
            NearbyBanner.IsVisible = false;
        }
    }

    /// <summary>Cập nhật nhãn trạng thái GPS + màu chấm chỉ báo.</summary>
    private void UpdateGpsStatus(string message, string colorHex)
    {
        LblGpsStatus.Text = message;
        GpsIndicator.Fill = new SolidColorBrush(Color.FromArgb(colorHex));
    }

    /// <summary>Tải HTML map (Leaflet + OSM): hiển thị tất cả POI đã đồng bộ.</summary>
    async void LoadMap()
    {
        Location userLocation;
        try
        {
            userLocation = await Geolocation.Default.GetLastKnownLocationAsync()
                ?? await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Low, TimeSpan.FromSeconds(5)))
                ?? new Location(10.7595, 106.7012);
        }
        catch
        {
            userLocation = new Location(10.7595, 106.7012);
        }

        _lastKnownLocation = userLocation;
        var allPlaces = await _dbService.GetPlacesAsync();
        var sb = new StringBuilder();
        foreach (var p in allPlaces)
        {
            double dist = Location.CalculateDistance(userLocation.Latitude, userLocation.Longitude, p.Latitude, p.Longitude, DistanceUnits.Kilometers) * 1000;
            var color = p.IsVisited
                ? "#4CAF50"
                : dist <= 1000
                ? (p.Radius <= 100 ? "#F44336" : "#2196F3")
                : "#9E9E9E";
            AppendAddPlaceJavaScript(sb, p, color);
        }

        if (mapView != null)
        {
            mapView.Source = new HtmlWebViewSource
            {
                Html = BuildLeafletHtml(userLocation, sb.ToString(), JsonSerializer.Serialize(GetAdminWebBaseUrlForQr()))
            };
            ArmMapLoadingFallback();
        }
    }

    /// <summary>Sinh lệnh JS <c>addPlace(...)</c> an toàn (JSON encode tên/mô tả).</summary>
    private static void AppendAddPlaceJavaScript(StringBuilder sb, TouristPlace p, string color)
    {
        string jId = JsonSerializer.Serialize(p.Id);
        string jLng = JsonSerializer.Serialize(p.Longitude);
        string jLat = JsonSerializer.Serialize(p.Latitude);
        string jName = JsonSerializer.Serialize(p.Name ?? "");
        string jDesc = JsonSerializer.Serialize(p.Description ?? "");
        string jColor = JsonSerializer.Serialize(color);
        string jQr = JsonSerializer.Serialize((p.QrImagePath ?? "").Trim());
        string jTag = JsonSerializer.Serialize((p.Tag ?? "").Trim());
        sb.AppendLine($"addPlace({jId},{jLng},{jLat},{jName},{jDesc},{jColor},{jQr},{jTag});");
    }

    private static (int PoiId, string RawText) ParseSpeakUrl(Uri u)
    {
        var idStr = QueryFirst(u, "id");
        if (!string.IsNullOrEmpty(idStr) && int.TryParse(idStr, out var id))
            return (id, "");

        return (0, QueryFirst(u, "text") ?? "");
    }

    private static int ParseScanPoiId(Uri u)
    {
        var raw = QueryFirst(u, "id");
        return int.TryParse(raw, out var id) ? id : 0;
    }

    private static int ParseOpenPoiId(Uri u)
    {
        var raw = QueryFirst(u, "id");
        return int.TryParse(raw, out var id) ? id : 0;
    }

    private async Task OpenPlaceDetailFromMapAsync(int poiId)
    {
        var places = await _dbService.GetPlacesAsync();
        var place = places.FirstOrDefault(p => p.Id == poiId);
        if (place is null) return;

        var services =
            Handler?.MauiContext?.Services
            ?? Shell.Current?.CurrentPage?.Handler?.MauiContext?.Services
            ?? Application.Current?.Windows.FirstOrDefault()?.Page?.Handler?.MauiContext?.Services;
        if (services is null) return;

        var detailPage = services.GetRequiredService<PlaceDetailPage>();
        detailPage.LoadPlace(place);
        var navigation = Shell.Current?.Navigation ?? Navigation;
        await navigation.PushAsync(detailPage);
    }

    private static string GetAdminWebBaseUrlForQr() =>
        EndpointResolver.ResolveAdminWebBaseUrls().Primary;

    /// <summary>HTML Leaflet + OpenStreetMap; giữ API JS <c>addPlace</c>/<c>updateLocation</c>/<c>highlightMarker</c>/<c>clearHighlight</c>/<c>markVisitedMarker</c>.</summary>
    private string BuildLeafletHtml(Location center, string markersJs, string adminWebBaseJson)
    {
        // Leaflet qua MauiAsset (appassets) trên Android WebView thường không chạy được (MIME/loader) → luôn dùng HTTPS CDN.
        const string leafletCss = "https://cdn.jsdelivr.net/npm/leaflet@1.9.4/dist/leaflet.css";
        const string leafletJs = "https://cdn.jsdelivr.net/npm/leaflet@1.9.4/dist/leaflet.js";
        return $@"
<!DOCTYPE html><html><head>
  <meta charset='utf-8'><meta name='viewport' content='initial-scale=1,maximum-scale=1,user-scalable=no'>
  <link rel='stylesheet' href='{leafletCss}' />
  <style>
    /* WebView Android: không set height 100% cho html/body thì #map absolute thường cao 0 → bản đồ trắng */
    html, body {{ margin:0; padding:0; width:100%; height:100%; overflow:hidden; }}
    #map {{ position:absolute; left:0; top:0; right:0; bottom:0; width:100%; height:100%; min-height:100%; }}
    #locateBtn{{position:absolute;top:16px;right:16px;z-index:1000;background:#1E88E5;color:white;border:none;padding:10px 14px;border-radius:10px;font-weight:bold;font-size:14px;}}
    .leaflet-div-icon.poi-pin{{background:transparent!important;border:none!important;}}
  </style>
</head><body>
  <div id='map'></div>
  <button id='locateBtn' type='button'>{T("📍 Vị trí của tôi", "📍 My location", "📍 現在地", "📍 내 위치", "📍 我的位置")}</button>
  <script>const MAP_BRIDGE='{MapJsBridgeRoot}';</script>
  <script src='{leafletJs}' onerror=""window.location.href=MAP_BRIDGE+'/error?msg='+encodeURIComponent('Leaflet script load failed');""></script>
  <script>
    window.onerror=function(msg,src,line,col,err){{window.location.href=MAP_BRIDGE+'/error?msg='+encodeURIComponent(msg+' | '+src+':'+line);return true;}};
    if (typeof L==='undefined') {{ window.location.href=MAP_BRIDGE+'/error?msg='+encodeURIComponent('Leaflet L undefined'); }}
    const adminWebBase = {adminWebBaseJson};
    let mapLoaded = false;
    function notifyLoadedOnce(){{
      if (mapLoaded) return;
      mapLoaded = true;
      clearTimeout(mapLoadTimeout);
      setTimeout(() => window.location.href=MAP_BRIDGE+'/loaded', 50);
    }}
    const mapLoadTimeout = setTimeout(() => {{
      if (!mapLoaded) window.location.href=MAP_BRIDGE+'/error?msg='+encodeURIComponent('Map load timeout');
    }}, 60000);
    const map = L.map('map', {{ zoomControl: true }}).setView([{F(center.Latitude)},{F(center.Longitude)}], 15);
    L.tileLayer('https://tile.openstreetmap.org/{{z}}/{{x}}/{{y}}.png', {{
      maxZoom: 19,
      attribution: '&copy; <a href=""https://www.openstreetmap.org/copyright"">OpenStreetMap</a>'
    }}).addTo(map);
    map.whenReady(() => {{
      notifyLoadedOnce();
      setTimeout(function() {{ try {{ map.invalidateSize(true); }} catch(e) {{}} }}, 200);
      setTimeout(function() {{ try {{ map.invalidateSize(true); }} catch(e) {{}} }}, 900);
    }});
    window.addEventListener('resize', function() {{ try {{ map.invalidateSize(false); }} catch(e) {{}} }});
    var userMarker = null, markerMap = {{}}, markerByCoord = {{}};
    function speakById(id){{ window.location.href=MAP_BRIDGE+'/speak?id='+encodeURIComponent(String(id)); }}
    function scanById(id){{ window.location.href=MAP_BRIDGE+'/scan?id='+encodeURIComponent(String(id)); }}
    function openById(id){{ window.location.href=MAP_BRIDGE+'/open?id='+encodeURIComponent(String(id)); }}
    function fallbackQrById(id){{
      return 'https://api.qrserver.com/v1/create-qr-code/?size=220x220&data=' + encodeURIComponent(String(id));
    }}
    function resolveQrSource(id, rawPath){{
      const p = String(rawPath || '').trim();
      if (!p) return fallbackQrById(id);
      if (/^https?:\/\//i.test(p)) return p;
      const normalized = p
        .replace(/^\.?\//, '')
        .replace(/^WEB\//i, '');
      if (!normalized) return fallbackQrById(id);
      return String(adminWebBase || '').replace(/\/$/, '') + '/' + normalized.replace(/^\//, '');
    }}
    function noteByTag(tag){{
      const t = String(tag || '').trim().toLowerCase();
      if (t === 'quán ăn' || t === 'quan an') return '{T("Ẩm thực địa phương", "Local food spot", "ローカルグルメ", "로컬 맛집", "本地美食")}';
      if (t === 'quán nước' || t === 'quan nuoc') return '{T("Quán nước nổi bật", "Popular drinks spot", "人気のドリンク店", "인기 음료 매장", "热门饮品店")}';
      if (t === 'di tích lịch sử' || t === 'di tich lich su') return '{T("Điểm di tích", "Historical landmark", "史跡スポット", "역사 유적지", "历史遗迹")}';
      return '{T("Điểm tham quan", "Point of interest", "観光スポット", "관광 포인트", "景点")}';
    }}
    function iconByTag(tag){{
      const t = String(tag || '').trim().toLowerCase();
      if (t === 'quán ăn' || t === 'quan an') return '🍜';
      if (t === 'quán nước' || t === 'quan nuoc') return '🥤';
      if (t === 'di tích lịch sử' || t === 'di tich lich su') return '🏛️';
      return '📍';
    }}
    function addPlace(id,lng,lat,name,desc,color,qrImagePath,tag){{
      const markerId = String(id);
      const coordKey = lng+'_'+lat;
      const wrap = document.createElement('div');
      wrap.style.cssText = 'display:flex;align-items:center;gap:6px;cursor:pointer;transform:translateY(-6px);';
      const note = document.createElement('div');
      note.style.cssText = 'max-width:170px;padding:3px 8px;border-radius:999px;background:#fff7ed;border:1px solid #fdba74;color:#c2410c;font-size:12px;font-weight:600;line-height:1.2;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;box-shadow:0 1px 3px rgba(0,0,0,.12);';
      note.textContent = iconByTag(tag) + ' ' + String(name || '');
      const el = document.createElement('div');
      el.style.cssText = 'width:16px;height:16px;flex:0 0 16px;border-radius:50%;border:2px solid #fff;box-shadow:0 1px 4px rgba(0,0,0,.35);background:'+color+';';
      wrap.appendChild(note);
      wrap.appendChild(el);
      const icon = L.divIcon({{
        html: wrap,
        className: 'poi-pin',
        iconSize: [190, 36],
        iconAnchor: [95, 36]
      }});
      const m = L.marker([lat, lng], {{ icon: icon }}).addTo(map);
      wrap.addEventListener('click', () => openById(id));
      markerMap[markerId] = {{ marker: m, el: el, defaultColor: color, isVisited: color === '#4CAF50' }};
      markerByCoord[coordKey] = markerId;
    }}
    function updateLocation(lng,lat){{
      if (userMarker) {{ map.removeLayer(userMarker); userMarker = null; }}
      const uel = document.createElement('div');
      uel.style.cssText = 'width:14px;height:14px;border-radius:50%;background:#1E88E5;border:3px solid #fff;box-shadow:0 0 0 2px rgba(30,136,229,.35);';
      const uicon = L.divIcon({{ html: uel, className: '', iconSize: [20, 20], iconAnchor: [10, 10] }});
      userMarker = L.marker([lat, lng], {{ icon: uicon, zIndexOffset: 1000 }}).addTo(map);
      map.flyTo([lat, lng], 16);
    }}
    function highlightMarker(lng,lat){{
      Object.keys(markerMap).forEach(id => {{
        const o = markerMap[id];
        o.el.style.background = o.isVisited ? '#4CAF50' : o.defaultColor;
        o.el.style.width = '16px'; o.el.style.height = '16px';
      }});
      const markerId = markerByCoord[lng+'_'+lat];
      if (markerId && markerMap[markerId]) {{
        markerMap[markerId].el.style.background = '#FFD700';
        markerMap[markerId].el.style.width = '22px';
        markerMap[markerId].el.style.height = '22px';
      }}
    }}
    function clearHighlight(){{
      Object.keys(markerMap).forEach(id => {{
        const o = markerMap[id];
        o.el.style.background = o.isVisited ? '#4CAF50' : o.defaultColor;
        o.el.style.width = '16px'; o.el.style.height = '16px';
      }});
    }}
    function markVisitedMarker(id){{
      const markerId = String(id);
      if (!markerMap[markerId]) return;
      markerMap[markerId].isVisited = true;
      markerMap[markerId].defaultColor = '#4CAF50';
      markerMap[markerId].el.style.background = '#4CAF50';
      markerMap[markerId].el.style.width = '16px';
      markerMap[markerId].el.style.height = '16px';
    }}
    document.getElementById('locateBtn').addEventListener('click', () => window.location.href=MAP_BRIDGE+'/locate');
    {markersJs}
    setTimeout(() => window.location.href=MAP_BRIDGE+'/locate', 1500);
  </script>
</body></html>";
    }
}
