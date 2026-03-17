using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using TravelGuide.Models;

namespace TravelGuide;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // Đăng ký nhận tin nhắn tọa độ từ LocationService
        WeakReferenceMessenger.Default.Register<LocationMessage>(this, (r, m) =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await CheckGeofenceAndSpeak(m.Value);
            });
        });
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }

    private async Task CheckGeofenceAndSpeak(Location userLocation)
    {
        var places = DataService.GetPlaces();

        foreach (var place in places)
        {
            double distance = userLocation.CalculateDistance(place.Latitude, place.Longitude, DistanceUnits.Kilometers) * 1000;

            if (distance <= place.Radius)
            {
                if (ShouldSpeak(place.Name))
                {
                    // Phát giọng nói thuyết minh chuyên nghiệp
                    await TextToSpeech.Default.SpeakAsync($"{place.Name}. {place.Description}");
                }
            }
        }
    }

    private bool ShouldSpeak(string placeName)
    {
        string key = $"LastSpoken_{placeName.Replace(" ", "_")}";
        DateTime lastSpoken = Preferences.Get(key, DateTime.MinValue);

        // Giữ 5 phút như bạn mong muốn
        if ((DateTime.Now - lastSpoken).TotalMinutes > 5)
        {
            Preferences.Set(key, DateTime.Now);
            return true;
        }
        return false;
    }
}