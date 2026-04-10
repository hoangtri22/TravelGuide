using Foundation;

namespace TravelGuide;

/// <summary>Điểm vào MAUI trên Mac Catalyst.</summary>
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    /// <summary>Tạo <see cref="MauiApp"/> dùng chung <see cref="MauiProgram"/>.</summary>
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
