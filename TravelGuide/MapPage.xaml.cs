using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Devices.Sensors;
using System.Globalization;
using System.Text;
using TravelGuide.Models;

namespace TravelGuide;

public partial class MapPage : ContentPage
{
    private readonly DatabaseService _dbService;
    private readonly NarrationEngine _narrationEngine;
    private readonly GpsBackgroundService _gpsService;
    private readonly GeofenceEngine _geofenceEngine;

    private Location? _lastKnownLocation;
    private TouristPlace? _nearestPlace;

    private static string T(string vi, string en, string ja, string ko, string zh) =>
        AppLanguage.Current switch
        {
            "en" => en,
            "ja" => ja,
            "ko" => ko,
            "zh" => zh,
            _ => vi
        };

    // Shortcut format số không bị ảnh hưởng bởi locale
    private static string F(double d) => d.ToString(CultureInfo.InvariantCulture);

    public MapPage(
        DatabaseService dbService,
        NarrationEngine narrationEngine,
        GpsBackgroundService gpsService,
        GeofenceEngine geofenceEngine)
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
                    string js = $"updateLocation({F(m.Value.Longitude)}, {F(m.Value.Latitude)});";
                    await mapView.EvaluateJavaScriptAsync(js);
                    await UpdateNearbyBanner(m.Value);
                }
            });
        });

        _geofenceEngine.OnPoiEntered += OnPoiEntered;
        _geofenceEngine.OnPoiTriggered += OnPoiTriggered;
        _geofenceEngine.OnPoiExited += OnPoiExited;

        LoadMap();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        MiniPlayer.Attach(_narrationEngine);
        await _gpsService.StartAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _gpsService.Stop();
        WeakReferenceMessenger.Default.Unregister<LocationMessage>(this);
        _geofenceEngine.OnPoiEntered -= OnPoiEntered;
        _geofenceEngine.OnPoiTriggered -= OnPoiTriggered;
        _geofenceEngine.OnPoiExited -= OnPoiExited;
    }

    private void OnPoiEntered(TouristPlace poi)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _nearestPlace = poi;
            LblNearbyTitle.Text = poi.Name;
            LblNearbyDist.Text = T(
                "Bạn đang trong vùng này",
                "You are in this area",
                "このエリアにいます",
                "이 구역에 있습니다",
                "您正在此区域内");
            NearbyBanner.IsVisible = true;
            _ = mapView.EvaluateJavaScriptAsync(
                $"highlightMarker({F(poi.Longitude)}, {F(poi.Latitude)});");
        });
    }

    private void OnPoiTriggered(TouristPlace poi)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            NearbyBanner.BackgroundColor = Color.FromArgb("#2E7D32");
            LblNearbyDist.Text = T(
                "Đang phát thuyết minh...",
                "Playing commentary...",
                "解説を再生中...",
                "해설 재생 중...",
                "正在播放讲解...");
        });
    }

    private void OnPoiExited()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            NearbyBanner.IsVisible = false;
            NearbyBanner.BackgroundColor = Color.FromArgb("#2E86DE");
            _ = mapView.EvaluateJavaScriptAsync("clearHighlight();");
        });
    }

    private void OnReloadClicked(object sender, EventArgs e)
    {
        LoadingOverlay.IsVisible = true;
        LoadMap();
    }

    private async void OnSpeakNearbyClicked(object sender, EventArgs e)
    {
        if (_nearestPlace != null)
            await _narrationEngine.SpeakAsync(_nearestPlace);
    }

    private async void MapView_Navigating(object sender, WebNavigatingEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[WebView NAV] {e.Url}");

        if (!e.Url.StartsWith("app://")) return;
        e.Cancel = true;

        if (e.Url.Contains("error"))
        {
            string errorMsg = Uri.UnescapeDataString(
                e.Url.Replace("app://error?msg=", ""));
            System.Diagnostics.Debug.WriteLine($"[JS ERROR] {errorMsg}");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LoadingOverlay.IsVisible = false;
                LblGpsStatus.Text = $"JS ERR: {errorMsg[..Math.Min(errorMsg.Length, 60)]}";
            });
        }
        else if (e.Url.Contains("speak"))
        {
            string rawText = Uri.UnescapeDataString(
                e.Url.Replace("app://speak?text=", ""));
            var places = await _dbService.GetPlacesAsync();
            var matched = places.FirstOrDefault(p =>
                rawText.StartsWith(p.Name, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
                await _narrationEngine.SpeakAsync(matched);
        }
        else if (e.Url.Contains("locate"))
        {
            await GetUserLocationAsync();
        }
        else if (e.Url.Contains("loaded"))
        {
            System.Diagnostics.Debug.WriteLine("[WebView] Map loaded successfully!");
            MainThread.BeginInvokeOnMainThread(() =>
                LoadingOverlay.IsVisible = false);
        }
    }

    private async Task GetUserLocationAsync()
    {
        try
        {
            UpdateGpsStatus(T(
                "Đang định vị...", "Locating...",
                "位置取得中...", "위치 찾는 중...", "定位中..."), "#FFA726");

            var location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium,
                    TimeSpan.FromSeconds(5)));

            if (location == null)
            {
                UpdateGpsStatus(T(
                    "Không có GPS", "No GPS",
                    "GPSなし", "GPS 없음", "无GPS"), "#EF5350");
                return;
            }

            _lastKnownLocation = location;
            UpdateGpsStatus(T(
                $"Độ chính xác: {location.Accuracy:F0}m",
                $"Accuracy: {location.Accuracy:F0}m",
                $"精度: {location.Accuracy:F0}m",
                $"정확도: {location.Accuracy:F0}m",
                $"精度: {location.Accuracy:F0}m"), "#4CAF50");

            await mapView.EvaluateJavaScriptAsync(
                $"updateLocation({F(location.Longitude)}, {F(location.Latitude)});");
            await UpdateNearbyBanner(location);
        }
        catch (Exception ex)
        {
            UpdateGpsStatus(T(
                "Lỗi GPS", "GPS Error",
                "GPSエラー", "GPS 오류", "GPS错误"), "#EF5350");
            System.Diagnostics.Debug.WriteLine($"[GPS ERROR] {ex.Message}");
        }
    }

    private async Task UpdateNearbyBanner(Location userLocation)
    {
        var places = await _dbService.GetPlacesAsync();
        _nearestPlace = places
            .OrderBy(p => Location.CalculateDistance(
                userLocation.Latitude, userLocation.Longitude,
                p.Latitude, p.Longitude,
                DistanceUnits.Kilometers))
            .FirstOrDefault();

        if (_nearestPlace == null) return;

        double dist = Location.CalculateDistance(
            userLocation.Latitude, userLocation.Longitude,
            _nearestPlace.Latitude, _nearestPlace.Longitude,
            DistanceUnits.Kilometers) * 1000;

        if (dist <= _nearestPlace.Radius * 2)
        {
            LblNearbyTitle.Text = _nearestPlace.Name;
            LblNearbyDist.Text = dist < 1000
                ? T($"Cách bạn {dist:F0}m",
                    $"{dist:F0}m away",
                    $"{dist:F0}m先",
                    $"{dist:F0}m 거리",
                    $"距您{dist:F0}米")
                : T($"Cách bạn {dist / 1000:F1}km",
                    $"{dist / 1000:F1}km away",
                    $"{dist / 1000:F1}km先",
                    $"{dist / 1000:F1}km 거리",
                    $"距您{dist / 1000:F1}公里");
            NearbyBanner.IsVisible = true;
        }
        else
        {
            NearbyBanner.IsVisible = false;
        }
    }

    private void UpdateGpsStatus(string message, string colorHex)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LblGpsStatus.Text = message;
            GpsIndicator.Fill = new SolidColorBrush(Color.FromArgb(colorHex));
        });
    }

    async void LoadMap()
    {
        System.Diagnostics.Debug.WriteLine("[MAP] LoadMap START");
        string token = "pk.eyJ1IjoicGh3bmlpMTk5IiwiYSI6ImNtbXE3MzBiYjBwN2UyeHB6aTAweDY5bnUifQ.allfFmkZfixY6rEIanjhYQ";

        string mapboxJs = "";
        string mapboxCss = "";
        try
        {
            using var jsStream = await FileSystem.Current.OpenAppPackageFileAsync("mapbox-gl.js");
            using var jsReader = new StreamReader(jsStream);
            mapboxJs = await jsReader.ReadToEndAsync();
            System.Diagnostics.Debug.WriteLine($"[MAP] JS loaded: {mapboxJs.Length} chars");

            using var cssStream = await FileSystem.Current.OpenAppPackageFileAsync("mapbox-gl.css");
            using var cssReader = new StreamReader(cssStream);
            mapboxCss = await cssReader.ReadToEndAsync();
            System.Diagnostics.Debug.WriteLine($"[MAP] CSS loaded: {mapboxCss.Length} chars");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MAP] File load error: {ex.Message}");
        }

        Location userLocation;
        try
        {
            userLocation =
                await Geolocation.Default.GetLastKnownLocationAsync()
                ?? await Geolocation.Default.GetLocationAsync(
                       new GeolocationRequest(GeolocationAccuracy.Low,
                           TimeSpan.FromSeconds(5)))
                ?? new Location(10.7595, 106.7012);
        }
        catch
        {
            userLocation = new Location(10.7595, 106.7012);
        }

        _lastKnownLocation = userLocation;
        System.Diagnostics.Debug.WriteLine(
            $"[MAP] Loading at {F(userLocation.Latitude)}, {F(userLocation.Longitude)}");

        var allPlaces = await _dbService.GetPlacesAsync();
        System.Diagnostics.Debug.WriteLine($"[MAP] Total places: {allPlaces.Count}");

        var sb = new StringBuilder();
        foreach (var p in allPlaces)
        {
            double dist = Location.CalculateDistance(
                userLocation.Latitude, userLocation.Longitude,
                p.Latitude, p.Longitude,
                DistanceUnits.Kilometers) * 1000;

            if (dist <= 1000)
            {
                string color = p.Radius <= 100 ? "#F44336" : "#2196F3";
                string safeName = p.Name.Replace("'", "\\'");
                string safeDesc = p.Description.Replace("'", "\\'");
                sb.AppendLine(
                    $"addPlace({F(p.Longitude)},{F(p.Latitude)},'{safeName}','{safeDesc}','{color}');");
            }
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(15000);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (LoadingOverlay.IsVisible)
                {
                    System.Diagnostics.Debug.WriteLine("[MAP] Fallback: force hiding overlay after 15s");
                    LoadingOverlay.IsVisible = false;
                }
            });
        });

        if (mapView != null)
            mapView.Source = new HtmlWebViewSource
            {
                Html = BuildMapHtml(token, userLocation, sb.ToString(), mapboxJs, mapboxCss)
            };
    }

    private string BuildMapHtml(string token, Location center, string markersJs,
        string mapboxJs, string mapboxCss) => $@"
<!DOCTYPE html><html>
<head>
  <meta charset='utf-8'>
  <meta name='viewport' content='initial-scale=1,maximum-scale=1,user-scalable=no'>
  <style>{mapboxCss}</style>
  <style>
    body{{margin:0;padding:0;}}
    #map{{position:absolute;top:0;bottom:0;width:100%;}}
    #locateBtn{{
      position:absolute;top:16px;right:16px;z-index:10;
      background:#1E88E5;color:white;border:none;
      padding:10px 14px;border-radius:10px;
      font-weight:bold;font-size:14px;
      box-shadow:0 2px 8px rgba(0,0,0,0.25);cursor:pointer;
    }}
  </style>
</head>
<body>
  <div id='map'></div>
  <button id='locateBtn'>{T("📍 Vị trí của tôi", "📍 My location", "📍 現在地", "📍 내 위치", "📍 我的位置")}</button>
  <script>{mapboxJs}</script>
  <script>
    window.onerror = function(msg, src, line, col, err) {{
      window.location.href = 'app://error?msg=' + encodeURIComponent(msg + ' | ' + src + ':' + line);
      return true;
    }};

    mapboxgl.accessToken='{token}';
    const map=new mapboxgl.Map({{
      container:'map',
      style:'mapbox://styles/mapbox/streets-v12',
      center:[{F(center.Longitude)},{F(center.Latitude)}],
      zoom:15
    }});
    var userMarker=null;
    var markerMap={{}};

    map.on('load',()=>{{ window.location.href='app://loaded'; }});
    map.on('error',(e)=>{{
      window.location.href='app://error?msg='+encodeURIComponent('MapboxError: '+JSON.stringify(e.error));
    }});

    function speak(t){{ window.location.href='app://speak?text='+encodeURIComponent(t); }}

    function addPlace(lng,lat,name,desc,color){{
      const popup=new mapboxgl.Popup({{offset:25}})
        .setHTML('<b>'+name+'</b><p style=""font-size:12px;margin:4px 0 0"">'+desc+'</p>');
      const marker=new mapboxgl.Marker({{color:color}})
        .setLngLat([lng,lat]).setPopup(popup).addTo(map);
      markerMap[lng+'_'+lat]={{marker,defaultColor:color}};
      popup.on('open',()=>{{ speak(name+'. '+desc); }});
    }}

    function updateLocation(lng,lat){{
      if(userMarker) userMarker.remove();
      userMarker=new mapboxgl.Marker({{color:'#4CAF50'}})
        .setLngLat([lng,lat])
        .setPopup(new mapboxgl.Popup().setHTML('<b>{T("📍 Bạn đang ở đây", "📍 You are here", "📍 現在地", "📍 현재 위치", "📍 您在这里")}</b>'))
        .addTo(map);
      map.flyTo({{center:[lng,lat],zoom:16,speed:1.2}});
    }}

    function highlightMarker(lng,lat){{
      Object.values(markerMap).forEach(m=>{{
        m.marker.getElement().style.filter='none';
        m.marker.getElement().style.transform='scale(1)';
      }});
      const key=lng+'_'+lat;
      if(markerMap[key]){{
        markerMap[key].marker.getElement().style.filter=
          'drop-shadow(0 0 8px #FFD700)';
        markerMap[key].marker.getElement().style.transform='scale(1.4)';
      }}
    }}

    function clearHighlight(){{
      Object.values(markerMap).forEach(m=>{{
        m.marker.getElement().style.filter='none';
        m.marker.getElement().style.transform='scale(1)';
      }});
    }}

    document.getElementById('locateBtn').addEventListener('click',()=>{{
      window.location.href='app://locate';
    }});

    {markersJs}

    setTimeout(()=>{{ window.location.href='app://locate'; }},1500);
  </script>
</body></html>";
}