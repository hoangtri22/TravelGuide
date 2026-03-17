using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using TravelGuide.Models;
using System.Text;
using CommunityToolkit.Mvvm.Messaging; // Dùng Messenger cho hiện đại

namespace TravelGuide;

public partial class MapPage : ContentPage
{
    public MapPage()
    {
        InitializeComponent();

        // ĐĂNG KÝ NHẬN VỊ TRÍ (Dùng Messenger thay cho MessagingCenter lỗi thời)
        WeakReferenceMessenger.Default.Register<LocationMessage>(this, (r, m) =>
        {
            MainThread.BeginInvokeOnMainThread(async () => {
                if (mapView != null && m.Value != null)
                {
                    string jsCode = $"updateLocation({m.Value.Longitude}, {m.Value.Latitude});";
                    await mapView.EvaluateJavaScriptAsync(jsCode);
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
        catch { /* Xử lý lỗi GPS nếu cần */ }
    }

    void LoadMap()
    {
        // Token của bạn
        string token = "pk.eyJ1IjoicGh3bmlpMTk5IiwiYSI6ImNtbXE3MzBiYjBwN2UyeHB6aTAweDY5bnUifQ.allfFmkZfixY6rEIanjhYQ";

        // ĐỒNG BỘ: Lấy dữ liệu từ kho chung DataService
        var places = DataService.GetPlaces();
        StringBuilder sbMarkers = new StringBuilder();

        foreach (var p in places)
        {
            // Tự động chọn màu: Đỏ cho quán ăn, Xanh cho di tích
            string color = (p.Name.Contains("Phở") || p.Name.Contains("Bánh mì")) ? "red" : "blue";

            // Escape chuỗi để tránh lỗi JS khi có dấu nháy đơn
            string safeName = p.Name.Replace("'", "\\'");
            string safeDesc = p.Description.Replace("'", "\\'");

            sbMarkers.AppendLine($"addPlace({p.Longitude}, {p.Latitude}, '{safeName}', '{safeDesc}', '{color}');");
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
            center:[106.6990, 10.7797], 
            zoom:14 
        }});
        
        let userMarker=null;

        function speak(text){{ window.location.href = 'app://speak?text=' + encodeURIComponent(text); }}

        function addPlace(lng,lat,name,desc,color){{
            const popup = new mapboxgl.Popup({{ offset: 25 }}).setHTML('<b>'+name+'</b><p style=""font-size:12px"">'+desc+'</p>');
            new mapboxgl.Marker({{color:color}}).setLngLat([lng,lat]).setPopup(popup).addTo(map);
            
            // Sự kiện khi người dùng nhấn vào Marker trên bản đồ
            popup.on('open', function(){{ 
                speak(name + '. ' + desc); 
            }});
        }}

        function updateLocation(lng, lat){{
            if(userMarker) userMarker.remove();
            userMarker = new mapboxgl.Marker({{color:'green'}}).setLngLat([lng,lat]).setPopup(new mapboxgl.Popup().setHTML('Bạn đang ở đây')).addTo(map);
            map.flyTo({{ center:[lng,lat], zoom:15, speed: 1.2 }});
        }}

        document.getElementById('locateBtn').addEventListener('click',()=>{{ window.location.href = 'app://locate'; }});

        // Chèn các Marker từ C#
        {sbMarkers.ToString()}

        // Tự động định vị sau 5 giây
        setTimeout(()=>{{ window.location.href = 'app://locate'; }}, 5000);
    </script>
</body>
</html>";

        if (mapView != null)
        {
            mapView.Source = new HtmlWebViewSource { Html = html };
        }
    }
}