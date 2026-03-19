using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Extensions.Logging;
using TravelGuide.Resources;
using System.Globalization; 
using LocalizationResourceManager.Maui; 

namespace TravelGuide;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiMaps()
            // --- ĐĂNG KÝ BỘ DỊCH THUẬT ---
            .UseLocalizationResourceManager(settings =>
            {
                // Chỉ cần dòng này để nạp file ngôn ngữ là đủ
                settings.AddResource(AppResources.ResourceManager);
            })
            // -----------------------------
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Đăng ký các Service cho App
        builder.Services.AddSingleton<DatabaseService>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<MapPage>();

        // Nhớ thêm cả MainPage nếu bạn dùng DI (Dependency Injection)
        builder.Services.AddTransient<MainPage>();

        return builder.Build();
    }
}