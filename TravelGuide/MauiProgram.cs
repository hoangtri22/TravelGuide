using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Extensions.Logging;
using TravelGuide.Resources;
using LocalizationResourceManager.Maui;

namespace TravelGuide;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // FIX: Load ngôn ngữ đã lưu ngay khi khởi động
        AppLanguage.LoadSaved();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiMaps()
            .UseLocalizationResourceManager(settings =>
            {
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

        // Infrastructure
        builder.Services.AddSingleton<DatabaseService>();
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<TranslationService>();

        // FIX: Đăng ký các engine còn thiếu — Singleton vì cần dùng chung state
        builder.Services.AddSingleton<NarrationEngine>();
        builder.Services.AddSingleton<GeofenceEngine>();
        builder.Services.AddSingleton<GpsBackgroundService>();

        // ── Pages (Transient — tạo mới mỗi lần navigate) ─────────────────
        // Thứ tự đăng ký không quan trọng với DI container,
        // nhưng liệt kê theo dependency tree để dễ đọc:
        // PlaceDetailPage ← HomePage ← MainPage
        builder.Services.AddTransient<PlaceDetailPage>();
        builder.Services.AddTransient<MapPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<AudioPage>();

        var app = builder.Build();

        // Set culture theo ngôn ngữ đã lưu
        var savedLang = Preferences.Get("app_language", "vi");
        var locMgr = app.Services.GetRequiredService<ILocalizationResourceManager>();
        locMgr.CurrentCulture = new System.Globalization.CultureInfo(savedLang);

        Task.Run(async () =>
        {
            var db = app.Services.GetRequiredService<DatabaseService>();
            await db.SeedDataAsync();
        });

        return app;
    }
}