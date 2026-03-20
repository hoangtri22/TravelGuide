using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Devices.Sensors;
using System.Text;
using TravelGuide.Models; // LocationMessage khai báo trong LocationMessage.cs — không duplicate ở đây

namespace TravelGuide;

public partial class MapPage : ContentPage
{
    private readonly DatabaseService _dbService;

    // FIX: Inject NarrationEngine để TTS đúng locale
    private readonly NarrationEngine _narrationEngine;

    private Location? _lastKnownLocation;
    private TouristPlace? _nearestPlace;

    public MapPage(DatabaseService dbService, NarrationEngine narrationEngine)
    {
        InitializeComponent();
        _dbService = dbService;
        _narrationEngine = narrationEngine;

        WeakReferenceMessenger.Default.Register<LocationMessage>(this, (r, m) =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                _lastKnownLocation = m.Value;
                if (mapView != null)
                {
                    string jsCode = $"updateLocation({m.Value.Longitude}, {m.Value.Latitude});";
                    await mapView.EvaluateJavaScriptAsync(jsCode);
                    await UpdateNearbyBanner(m.Value);
                }
            });
        });

        LoadMap();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        WeakReferenceMessenger.Default.Unregister<LocationMessage>(this);
        // FIX: Dùng StopAsync() qua engine, dùng _ = để tránh CS4014 warning
        _ = _narrationEngine.StopAsync();
    }

    private void OnReloadClicked(object sender, EventArgs e)
    {
        LoadingOverlay.IsVisible = true;
        LoadMap();
    }

    // FIX: Dùng NarrationEngine thay vì TextToSpeech.Default — đúng locale + intro
    private async void OnSpeakNearbyClicked(object sender, EventArgs e)
    {
        if (_nearestPlace != null)
            await _narrationEngine.SpeakAsync(_nearestPlace);
    }

    private async void MapView_Navigating(object sender, WebNavigatingEventArgs e)
    {
        if (!e.Url.StartsWith("app://")) return;
        e.Cancel = true;

        if (e.Url.Contains("speak"))
        {
            // FIX: Tìm POI theo tên để dùng NarrationEngine đúng locale
            string rawText = Uri.UnescapeDataString(e.Url.Replace("app://speak?text=", ""));
            var places = await _dbService.GetPlacesAsync();
            var matched = places.FirstOrDefault(p =>
                rawText.StartsWith(p.Name, StringComparison.OrdinalIgnoreCase));

            if (matched != null)
                await _narrationEngine.SpeakAsync(matched);
            else
                // Fallback: không tìm được POI, phát thẳng qua engine với text hiện tại
                System.Diagnostics.Debug.WriteLine($"[MAP] POI not found for speak: {rawText}");
        }
        else if (e.Url.Contains("locate"))
        {
            await GetUserLocationAsync();
        }
        else if (e.Url.Contains("loaded"))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                LoadingOverlay.IsVisible = false;
            });
        }
    }

    private async Task GetUserLocationAsync()
    {
        try
        {
            UpdateGpsStatus("Đang định vị...", "#FFA726");

            var location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)));

            if (location == null)
            {
                UpdateGpsStatus("Không có GPS", "#EF5350");
                return;
            }

            _lastKnownLocation = location;
            UpdateGpsStatus($"Độ chính xác: {location.Accuracy:F0}m", "#4CAF50");

            string jsCode = $"updateLocation({location.Longitude}, {location.Latitude});";
            await mapView.EvaluateJavaScriptAsync(jsCode);
            await UpdateNearbyBanner(location);
        }
        catch (Exception ex)
        {
            UpdateGpsStatus("Lỗi GPS", "#EF5350");
            System.Diagnostics.Debug.WriteLine($"GPS error: {ex.Message}");
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

        double distMeters = Location.CalculateDistance(
            userLocation.Latitude, userLocation.Longitude,
            _nearestPlace.Latitude, _nearestPlace.Longitude,
            DistanceUnits.Kilometers) * 1000;

        if (distMeters <= _nearestPlace.Radius * 2)
        {
            LblNearbyTitle.Text = _nearestPlace.Name;
            LblNearbyDist.Text = distMeters < 1000
                ? $"Cách bạn {distMeters:F0}m"
                : $"Cách bạn {distMeters / 1000:F1}km";
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
        string token = "pk.eyJ1IjoicGh3bmlpMTk5IiwiYSI6ImNtbXE3MzBiYjBwN2UyeHB6aTAweDY5bnUifQ.allfFmkZfixY6rEIanjhYQ";

        Location userLocation;
        try
        {
            userLocation = await Geolocation.Default.GetLastKnownLocationAsync()
                        ?? await Geolocation.Default.GetLocationAsync(
                               new GeolocationRequest(GeolocationAccuracy.Low, TimeSpan.FromSeconds(5)))
                        ?? new Location(10.7595, 106.7012);
        }
        catch
        {
            userLocation = new Location(10.7595, 106.7012);
        }

        _lastKnownLocation = userLocation;

        var allPlaces = await _dbService.GetPlacesAsync();
        var sbMarkers = new StringBuilder();

        foreach (var p in allPlaces)
        {
            double distance = Location.CalculateDistance(
                userLocation.Latitude, userLocation.Longitude,
                p.Latitude, p.Longitude,
                DistanceUnits.Kilometers) * 1000;

            if (distance <= 1000)
            {
                string color = p.Radius <= 100 ? "#F44336" : "#2196F3";
                string safeName = p.Name.Replace("'", "\\'");
                string safeDesc = p.Description.Replace("'", "\\'");
                sbMarkers.AppendLine($"addPlace({p.Longitude}, {p.Latitude}, '{safeName}', '{safeDesc}', '{color}');");
            }
        }

        if (mapView != null)
            mapView.Source = new HtmlWebViewSource { Html = BuildMapHtml(token, userLocation, sbMarkers.ToString()) };
    }

    private string BuildMapHtml(string token, Location center, string markersJs) => $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='initial-scale=1,maximum-scale=1,user-scalable=no'>
    <link href='https://api.mapbox.com/mapbox-gl-js/v2.15.0/mapbox-gl.css' rel='stylesheet'>
    <script src='https://api.mapbox.com/mapbox-gl-js/v2.15.0/mapbox-gl.js'></script>
    <style>
        body {{margin:0;padding:0;}}
        #map {{position:absolute;top:0;bottom:0;width:100%;}}
        #locateBtn {{
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
    <button id='locateBtn'>📍 Vị trí của tôi</button>
    <script>
        mapboxgl.accessToken='{token}';
        const map = new mapboxgl.Map({{
            container:'map',
            style:'mapbox://styles/mapbox/streets-v12',
            center:[{center.Longitude},{center.Latitude}],
            zoom:15
        }});
        let userMarker = null;

        map.on('load', function() {{
            window.location.href = 'app://loaded';
        }});

        function speak(text) {{
            window.location.href = 'app://speak?text=' + encodeURIComponent(text);
        }}

        function addPlace(lng, lat, name, desc, color) {{
            const popup = new mapboxgl.Popup({{offset:25}})
                .setHTML('<b>'+name+'</b><p style=""font-size:12px;margin:4px 0 0"">'+desc+'</p>');
            new mapboxgl.Marker({{color:color}})
                .setLngLat([lng,lat]).setPopup(popup).addTo(map);
            popup.on('open', function() {{ speak(name+'. '+desc); }});
        }}

        function updateLocation(lng, lat) {{
            if(userMarker) userMarker.remove();
            userMarker = new mapboxgl.Marker({{color:'#4CAF50'}})
                .setLngLat([lng,lat])
                .setPopup(new mapboxgl.Popup().setHTML('<b>📍 Bạn đang ở đây</b>'))
                .addTo(map);
            map.flyTo({{center:[lng,lat], zoom:16, speed:1.2}});
        }}

        document.getElementById('locateBtn').addEventListener('click', () => {{
            window.location.href = 'app://locate';
        }});

        {markersJs}

        setTimeout(() => {{ window.location.href = 'app://locate'; }}, 1500);
    </script>
</body>
</html>";
}