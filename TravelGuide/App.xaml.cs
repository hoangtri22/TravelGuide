using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Media;
using TravelGuide.Models;

namespace TravelGuide;

/// <summary>
/// Ứng dụng gốc: seed/sync POI khi mở, lắng nghe <see cref="LocationMessage"/> và logic geofence/TTS song song với <see cref="GeofenceEngine"/> (legacy path).
/// </summary>
public partial class App : Application
{
    private readonly DatabaseService _dbService;
    private readonly NarrationEngine _narrationEngine;

    /// <summary>Khởi tạo app, chạy <see cref="DatabaseService.SeedDataAsync"/> nền và đăng ký messenger GPS.</summary>
    public App(DatabaseService dbService, NarrationEngine narrationEngine)
    {
        InitializeComponent();
        _dbService = dbService;
        _narrationEngine = narrationEngine;

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

    /// <summary>Tạo cửa sổ chính chứa <see cref="AppShell"/> (điều hướng Shell).</summary>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }

    /// <summary>App vào nền / mất focus: dừng TTS và audio để tránh chồng lấn với thông báo hệ thống.</summary>
    protected override void OnSleep()
    {
        base.OnSleep();
        _ = _narrationEngine.StopAsync();
    }

    /// <summary>
    /// (Legacy) So khoảng cách user–POI; nếu trong bán kính và qua cooldown 5 phút thì gọi TTS tiếng Việt trực tiếp.
    /// Luồng chính trên map/home thường dùng <see cref="GeofenceEngine"/> + <see cref="NarrationEngine"/>.
    /// </summary>
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
                    var narration = string.IsNullOrWhiteSpace(place.Description) ? place.Name : place.Description;
                    await SpeakVietnameseAsync(narration);
                }
            }
        }
    }

    /// <summary>Phát một đoạn văn bản bằng TTS, ưu tiên locale tiếng Việt nếu thiết bị có.</summary>
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

    /// <summary>Cooldown theo tên POI (Preferences) để tránh lặp lại thuyết minh quá sớm (~5 phút).</summary>
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