using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using TravelGuide.Models;

namespace TravelGuide;

public partial class App : Application
{
    private readonly DatabaseService _dbService;

    // 1. Sửa Constructor để nhận DatabaseService từ MauiProgram
    public App(DatabaseService dbService)
    {
        InitializeComponent();
        _dbService = dbService;

        // Gọi nạp dữ liệu từ file JSON vào SQLite khi khởi động
        Task.Run(async () => await _dbService.SeedDataAsync());

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
        // 2. Lấy dữ liệu thực từ SQLite thay vì DataService
        var places = await _dbService.GetPlacesAsync();

        foreach (var place in places)
        {
            double distance = userLocation.CalculateDistance(place.Latitude, place.Longitude, DistanceUnits.Kilometers) * 1000;
            System.Diagnostics.Debug.WriteLine($">>> Đang cách {place.Name}: {distance:F2} mét (Bán kính: {place.Radius}m)");
            if (distance <= place.Radius)
            {
                if (ShouldSpeak(place.Name))
                {
                    // Phát giọng nói thuyết minh chuyên nghiệp từ Description trong JSON
                    await SpeakVietnameseAsync($"{place.Name}. {place.Description}");
                }
            }
        }
    }
    private async Task SpeakVietnameseAsync(string text)
    {
        // Tìm tất cả ngôn ngữ có trên máy
        var locales = await TextToSpeech.Default.GetLocalesAsync();

        // Lọc ra tiếng Việt
        var vnLocale = locales.FirstOrDefault(l =>
            l.Language == "vi" ||
            l.Name.ToLower().Contains("vietnam"));

        // Nếu thấy tiếng Việt thì nói bằng giọng Việt, không thì nói mặc định
        var options = new SpeechOptions { Locale = vnLocale };
        await TextToSpeech.Default.SpeakAsync(text, options);
    }
    private bool ShouldSpeak(string placeName)
    {
        string key = $"LastSpoken_{placeName.Replace(" ", "_")}";
        DateTime lastSpoken = Preferences.Get(key, DateTime.MinValue);

        // Giữ 5 phút
        if ((DateTime.Now - lastSpoken).TotalMinutes > 5)
        {
            Preferences.Set(key, DateTime.Now);
            return true;
        }
        return false;
    }
}