using Microsoft.Maui.Devices;
using Microsoft.Maui.Hosting;
using Microsoft.Extensions.Logging;
using LocalizationResourceManager.Maui;
using System.Globalization;
using TravelGuide;
using ZXing.Net.Maui.Controls;

namespace TravelGuide;

/// <summary>
/// Điểm vào cấu hình MAUI: fonts, localization, đăng ký DI (Database, TTS, Geofence, trang).
/// </summary>
public static class MauiProgram
{
#if DEBUG
    /// <summary>
    /// Điện thoại thật: ép URL API = máy dev (Chrome bạn gõ tay được, app thì cần chỗ này).
    /// Để <paramref name="apiBase"/> rỗng để không đụng (emulator dùng 10.0.2.2 như cũ).
    /// Đổi IP PC: sửa chuỗi khi gọi bên dưới (ipconfig → IPv4 Wi-Fi).
    /// </summary>
    private static void ApplyDebugAndroidPhysicalApiBase(string apiBase)
    {
        apiBase = (apiBase ?? "").Trim().TrimEnd('/');
        if (apiBase.Length == 0) return;
        if (DeviceInfo.Platform != DevicePlatform.Android || DeviceInfo.DeviceType != DeviceType.Physical)
            return;

        Environment.SetEnvironmentVariable("TRAVELGUIDE_API_BASE_URL", apiBase);
        Preferences.Set("api_base_url", apiBase);
        Preferences.Set("tourist_api_base_url", apiBase);
    }
#endif

    /// <summary>Tạo <see cref="MauiApp"/>, áp dụng culture đã lưu và trả về instance cho platform.</summary>
    public static MauiApp CreateMauiApp()
    {
        // ✅ Load ngôn ngữ đã lưu ngay khi app start
        AppLanguage.LoadSaved();
        MapboxTokenBootstrap.TryLoadFromBundledSecretFile();

#if DEBUG
        // Để rỗng: không ép IP — đổi Wi‑Fi chỉ cần sửa URL trên màn đăng nhập / device_endpoints.json / biến TRAVELGUIDE_API_BASE_URL.
        // Chỉ điền URL khi muốn ép máy thật DEBUG luôn trỏ một máy dev cố định (ví dụ http://192.168.x.x:5096).
        ApplyDebugAndroidPhysicalApiBase("");
#endif
        var defaultApi = EndpointResolver.GetDefaultApiBaseUrl();
        var currentTouristApi = Preferences.Get("tourist_api_base_url", "");
        var currentApi = Preferences.Get("api_base_url", "");
        if (string.IsNullOrWhiteSpace(currentTouristApi))
            Preferences.Set("tourist_api_base_url", defaultApi);
        if (string.IsNullOrWhiteSpace(currentApi))
            Preferences.Set("api_base_url", defaultApi);

        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseBarcodeReader()
            .UseLocalizationResourceManager(settings =>
            {
                // ✅ Kết nối với AppResources.resx
                settings.AddResource(AppResources.ResourceManager);
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // ========================
        // 🔧 SERVICES (Singleton)
        // ========================
        builder.Services.AddSingleton<DatabaseService>();
        builder.Services.AddSingleton(_ =>
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            return c;
        });
        builder.Services.AddSingleton<TranslationService>();
        builder.Services.AddSingleton<TouristAuthService>();

        // Engine dùng chung state
        builder.Services.AddSingleton<NarrationEngine>();
        builder.Services.AddSingleton<GeofenceEngine>();

        // ❌ KHÔNG thêm service Android ở đây (Foreground Service không phải DI)
        builder.Services.AddSingleton<GpsBackgroundService>();

        // ========================
        // 📄 PAGES (Transient)
        // ========================
        builder.Services.AddTransient<PlaceDetailPage>();
        builder.Services.AddTransient<MapPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<AudioPage>();
        builder.Services.AddTransient<TouristLoginPage>();
        builder.Services.AddTransient<TouristRegisterPage>();
        builder.Services.AddTransient<QrScannerPage>();
        builder.Services.AddTransient<QrScanHistoryPage>();

        var app = builder.Build();

        // ========================
        // 🌍 SET CULTURE
        // ========================
        try
        {
            var savedLang = Preferences.Get("app_language", "vi");

            var culture = new CultureInfo(savedLang);

            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            var locMgr = app.Services.GetRequiredService<ILocalizationResourceManager>();
            locMgr.CurrentCulture = culture;
        }
        catch
        {
            // fallback nếu lỗi culture
            var culture = new CultureInfo("vi");

            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }

        return app;
    }
}