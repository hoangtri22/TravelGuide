using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using TravelGuide.Platforms.Android;

namespace TravelGuide;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
    ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
    ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Không start service ở đây — Android 14 chặn FGS từ OnCreate
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android26.0")]
    protected override void OnResume()
    {
        base.OnResume();

        // Start LocationService từ OnResume — app đã fully visible
        // Android 14 cho phép start FGS khi app đang ở foreground
        try
        {
            var intent = new Intent(this, typeof(LocationService));
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                StartForegroundService(intent);
            else
                StartService(intent);

            System.Diagnostics.Debug.WriteLine("[MainActivity] LocationService started");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainActivity] Service error: {ex.Message}");
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        // KHÔNG stop service — để tiếp tục chạy nền
    }
}