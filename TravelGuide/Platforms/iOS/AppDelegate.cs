using Foundation;

namespace TravelGuide;

/// <summary>Điểm vào MAUI trên iOS: tạo <see cref="MauiApp"/> qua <see cref="MauiProgram"/>.</summary>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    /// <summary>Khởi tạo ứng dụng đa nền tảng cho platform iOS.</summary>
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
