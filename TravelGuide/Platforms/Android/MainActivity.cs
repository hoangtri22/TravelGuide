using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using TravelGuide.Platforms.Android;

namespace TravelGuide;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    // Thêm dấu ? vào Bundle để chấp nhận giá trị null
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        try
        {
            Intent intent = new Intent(this, typeof(LocationService));

            // Kiểm tra Android 8.0 (Oreo) trở lên để dùng StartForegroundService
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                StartForegroundService(intent);
            }
            else
            {
                StartService(intent);
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Service Error] {ex.Message}");
        }
    }
}