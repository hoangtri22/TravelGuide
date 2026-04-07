using Android.App;
using Android.Runtime;

namespace TravelGuide.Platforms.Android;

/// <summary>
/// Entry point của ứng dụng Android.
/// - Khởi tạo MAUI App
/// - Liên kết với MauiProgram (DI, Services)
/// </summary>
[Application]
public class MainApplication : MauiApplication
{
    /// <summary>
    /// Constructor được Android runtime gọi
    /// </summary>
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    /// <summary>
    /// Tạo instance MauiApp
    /// - Đây là nơi khởi tạo toàn bộ ứng dụng
    /// - Gọi MauiProgram để setup DI, Services, Config
    /// </summary>
    protected override MauiApp CreateMauiApp()
    {
        return MauiProgram.CreateMauiApp();
    }
}