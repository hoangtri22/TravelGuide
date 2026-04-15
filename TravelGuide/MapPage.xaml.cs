using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Devices.Sensors;
using System.Globalization;
using System.Text;
using System.Text.Json;
using TravelGuide.Models;

namespace TravelGuide;

/// <summary>
/// Trang bản đồ WebView: hiển thị POI, vị trí user, highlight POI đang trong geofence,
/// và bridge <c>app://</c> để TTS / định vị. Luôn dùng Mapbox GL (token: Preferences, env, hoặc file Raw).
/// </summary>
public partial class MapPage : ContentPage
{
    private readonly DatabaseService _dbService;
    private readonly NarrationEngine _narrationEngine;
    private readonly GpsBackgroundService _gpsService;
    private readonly GeofenceEngine _geofenceEngine;

    private Location? _lastKnownLocation;
    private TouristPlace? _nearestPlace;

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
        if (_nearestPlace != null) await _narrationEngine.SpeakAsync(_nearestPlace);
    }

    /// <summary>Xử lý custom URL từ JavaScript: lỗi, TTS từ popup, nút định vị, map đã load.</summary>
    private async void MapView_Navigating(object sender, WebNavigatingEventArgs e)
    {
        if (!e.Url.StartsWith("app://")) return;
        e.Cancel = true;

        if (e.Url.Contains("error"))
        {
            string errorMsg = Uri.UnescapeDataString(e.Url.Replace("app://error?msg=", ""));
            LoadingOverlay.IsVisible = false;
            LblGpsStatus.Text = $"JS ERR: {errorMsg[..Math.Min(errorMsg.Length, 60)]}";
        }
        else if (e.Url.Contains("speak"))
        {
            var parsed = ParseSpeakUrl(e.Url);
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
                await _narrationEngine.SpeakAsync(matched);
        }
        else if (e.Url.Contains("locate"))
        {
            await GetUserLocationAsync();
        }
        else if (e.Url.Contains("loaded"))
        {
            LoadingOverlay.IsVisible = false;
        }
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

    /// <summary>Tải HTML map: hiển thị tất cả POI đã đồng bộ (trước đây chỉ trong 1km nên dễ không thấy điểm).</summary>
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
            mapView.Source = new HtmlWebViewSource { Html = BuildMapHtml(userLocation, sb.ToString()) };
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
        sb.AppendLine($"addPlace({jId},{jLng},{jLat},{jName},{jDesc},{jColor});");
    }

    private static (int PoiId, string RawText) ParseSpeakUrl(string url)
    {
        const string byIdPrefix = "app://speak?id=";
        const string byTextPrefix = "app://speak?text=";
        if (url.StartsWith(byIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var raw = Uri.UnescapeDataString(url[byIdPrefix.Length..]);
            return int.TryParse(raw, out var id) ? (id, "") : (0, "");
        }

        if (url.StartsWith(byTextPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var rawText = Uri.UnescapeDataString(url[byTextPrefix.Length..]);
            return (0, rawText);
        }

        return (0, "");
    }

    /// <summary>Sinh HTML Mapbox GL (token rỗng thì Mapbox báo lỗi cấu hình — không dùng OSM/Leaflet).</summary>
    private string BuildMapHtml(Location center, string markersJs) =>
        BuildMapboxHtml(center, markersJs, JsonSerializer.Serialize(MapboxConfig.GetAccessToken()));

    /// <summary>HTML Mapbox GL JS: style vector, marker tùy màu, API <c>addPlace</c>/<c>updateLocation</c>/<c>highlight</c>.</summary>
    private string BuildMapboxHtml(Location center, string markersJs, string accessTokenJson) => $@"
<!DOCTYPE html><html><head>
  <meta charset='utf-8'><meta name='viewport' content='initial-scale=1,maximum-scale=1,user-scalable=no'>
  <link href='https://api.mapbox.com/mapbox-gl-js/v3.1.2/mapbox-gl.css' rel='stylesheet' />
  <style>body{{margin:0;padding:0;}} #map{{position:absolute;top:0;bottom:0;width:100%;}} #locateBtn{{position:absolute;top:16px;right:16px;z-index:1;background:#1E88E5;color:white;border:none;padding:10px 14px;border-radius:10px;font-weight:bold;font-size:14px;}}</style>
</head><body>
  <div id='map'></div>
  <button id='locateBtn' type='button'>{T("📍 Vị trí của tôi", "📍 My location", "📍 現在地", "📍 내 위치", "📍 我的位置")}</button>
  <script src='https://api.mapbox.com/mapbox-gl-js/v3.1.2/mapbox-gl.js'></script>
  <script>
    window.onerror=function(msg,src,line,col,err){{window.location.href='app://error?msg='+encodeURIComponent(msg+' | '+src+':'+line);return true;}};
    mapboxgl.accessToken = {accessTokenJson};
    const map = new mapboxgl.Map({{
      container: 'map',
      style: 'mapbox://styles/mapbox/streets-v12',
      center: [{F(center.Longitude)},{F(center.Latitude)}],
      zoom: 15
    }});
    map.on('load', () => {{ setTimeout(() => window.location.href='app://loaded', 100); }});
    var userMarker = null, markerMap = {{}}, markerByCoord = {{}};
    function speakById(id){{ window.location.href='app://speak?id='+encodeURIComponent(String(id)); }}
    function addPlace(id,lng,lat,name,desc,color){{
      const markerId = String(id);
      const coordKey = lng+'_'+lat;
      const el = document.createElement('div');
      el.style.cssText = 'width:16px;height:16px;border-radius:50%;border:2px solid #fff;box-shadow:0 1px 4px rgba(0,0,0,.35);background:'+color+';cursor:pointer;';
      const popup = new mapboxgl.Popup({{ offset: 20 }}).setHTML('<b>'+name+'</b><p style=""font-size:12px;margin:4px 0 0"">'+desc+'</p>');
      const m = new mapboxgl.Marker({{ element: el }}).setLngLat([lng,lat]).setPopup(popup).addTo(map);
      el.addEventListener('click', () => speakById(id));
      markerMap[markerId] = {{ marker: m, el: el, defaultColor: color, isVisited: color === '#4CAF50' }};
      markerByCoord[coordKey] = markerId;
    }}
    function updateLocation(lng,lat){{
      if (userMarker) userMarker.remove();
      const uel = document.createElement('div');
      uel.style.cssText = 'width:14px;height:14px;border-radius:50%;background:#1E88E5;border:3px solid #fff;box-shadow:0 0 0 2px rgba(30,136,229,.35);';
      userMarker = new mapboxgl.Marker({{ element: uel }}).setLngLat([lng,lat]).addTo(map);
      map.flyTo({{ center: [lng, lat], zoom: 16 }});
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
    document.getElementById('locateBtn').addEventListener('click', () => window.location.href='app://locate');
    {markersJs}
    setTimeout(() => window.location.href='app://locate', 1500);
  </script>
</body></html>";
}
