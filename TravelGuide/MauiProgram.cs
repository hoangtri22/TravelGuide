using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Extensions.Logging;
using LocalizationResourceManager.Maui;
using System.Globalization;
using Plugin.Maui.Audio;
using TravelGuide;

namespace TravelGuide;

/// <summary>
/// Điểm vào cấu hình MAUI: fonts, localization, đăng ký DI (Database, TTS, Geofence, trang).
/// </summary>
public static class MauiProgram
{
    /// <summary>Tạo <see cref="MauiApp"/>, áp dụng culture đã lưu và trả về instance cho platform.</summary>
    public static MauiApp CreateMauiApp()
    {
        // ✅ Load ngôn ngữ đã lưu ngay khi app start
        AppLanguage.LoadSaved();
        MapboxTokenBootstrap.TryLoadFromBundledSecretFile();

        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiMaps()
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

        builder.AddAudio();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // ========================
        // 🔧 SERVICES (Singleton)
        // ========================
        builder.Services.AddSingleton<DatabaseService>();
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<TranslationService>();

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