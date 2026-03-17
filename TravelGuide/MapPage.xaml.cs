using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using System.Text;
using TravelGuide.Models;

namespace TravelGuide;

public partial class MapPage : ContentPage
{
    // Phải khai báo biến này để dùng được Database
    private readonly DatabaseService _dbService;

    public MapPage(DatabaseService dbService)
    {
        InitializeComponent();
        _dbService = dbService; // Gán service vào biến local

        // ĐĂNG KÝ NHẬN VỊ TRÍ
        WeakReferenceMessenger.Default.Register<LocationMessage>(this, (r, m) =>
        {
            MainThread.BeginInvokeOnMainThread(async () => {
                if (mapView != null && m.Value != null)
                {
                    // Cập nhật vị trí người dùng trên bản đồ
                    string jsCode = $"updateLocation({m.Value.Longitude}, {m.Value.Latitude});";
                    await mapView.EvaluateJavaScriptAsync(jsCode);

                    // Tùy chọn: Gọi lại LoadMap() nếu muốn marker tự hiện ra khi đi tới vùng mới
                    // await LoadMap(); 
                }
            });
        });

        LoadMap();
    }

    private async void MapView_Navigating(object sender, WebNavigatingEventArgs e)
    {
        if (e.Url.StartsWith("app://"))
        {
            e.Cancel = true;
            if (e.Url.Contains("speak"))
            {
                string text = Uri.UnescapeDataString(e.Url.Replace("app://speak?text=", ""));
                await TextToSpeech.Default.SpeakAsync(text);
            }
            else if (e.Url.Contains("locate"))
            {
                await GetUserLocationAsync();
            }
        }
    }

    private async Task GetUserLocationAsync()
    {
        try
        {
            var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)));
            if (location != null && mapView != null)
            {
                string jsCode = $"updateLocation({location.Longitude}, {location.Latitude});";
                await mapView.EvaluateJavaScriptAsync(jsCode);
            }
        }
        catch { }
    }

    async void LoadMap()
    {
        string token = "pk.eyJ1IjoicGh3bmlpMTk5IiwiYSI6ImNtbXE3MzBiYjBwN2UyeHB6aTAweDY5bnUifQ.allfFmkZfixY6rEIanjhYQ";

        // 1. Lấy vị trí hiện tại
        var userLocation = await Geolocation.Default.GetLastKnownLocationAsync()
                           ?? await Geolocation.Default.GetLocationAsync();

        // 2. Lấy dữ liệu từ SQLite
        var allPlaces = await _dbService.GetPlacesAsync();
        StringBuilder sbMarkers = new StringBuilder();

        foreach (var p in allPlaces)
        {
            // 3. Tính khoảng cách (mét)
            double distance = Location.CalculateDistance(
                userLocation.Latitude, userLocation.Longitude,
                p.Latitude, p.Longitude,
                DistanceUnits.Kilometers) * 1000;

            // 4. CHỈ THÊM MARKER TRONG PHẠM VI 1KM
            if (distance <= 1000)
            {
                // Màu đỏ cho quán ăn (Radius nhỏ), xanh cho di tích
                string color = p.Radius <= 100 ? "#F44336" : "#2196F3";

                string safeName = p.Name.Replace("'", "\\'");
                string safeDesc = p.Description.Replace("'", "\\'");

                sbMarkers.AppendLine($"addPlace({p.Longitude}, {p.Latitude}, '{safeName}', '{safeDesc}', '{color}');");
            }
        }

        string html = $@"
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
        #locateBtn {{position:absolute;top:20px;right:20px;z-index:10;background:#1E88E5;color:white;border:none;padding:12px;border-radius:10px;font-weight:bold;box-shadow:0 2px 5px rgba(0,0,0,0.2);}}
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
            center:[{userLocation.Longitude}, {userLocation.Latitude}], 
            zoom:14 
        }});
        
        let userMarker=null;

        function speak(text){{ window.location.href = 'app://speak?text=' + encodeURIComponent(text); }}

        function addPlace(lng,lat,name,desc,color){{
            const popup = new mapboxgl.Popup({{ offset: 25 }}).setHTML('<b>'+name+'</b><p style=""font-size:12px"">'+desc+'</p>');
            new mapboxgl.Marker({{color:color}}).setLngLat([lng,lat]).setPopup(popup).addTo(map);
            
            popup.on('open', function(){{ 
                speak(name + '. ' + desc); 
            }});
        }}

        function updateLocation(lng, lat){{
            if(userMarker) userMarker.remove();
            userMarker = new mapboxgl.Marker({{color:'#4CAF50'}}).setLngLat([lng,lat]).setPopup(new mapboxgl.Popup().setHTML('Bạn đang ở đây')).addTo(map);
            map.flyTo({{ center:[lng,lat], zoom:15, speed: 1.2 }});
        }}

        document.getElementById('locateBtn').addEventListener('click',()=>{{ window.location.href = 'app://locate'; }});

        {sbMarkers.ToString()}

        setTimeout(()=>{{ window.location.href = 'app://locate'; }}, 2000);
    </script>
</body>
</html>";

        if (mapView != null)
        {
            mapView.Source = new HtmlWebViewSource { Html = html };
        }
    }
}