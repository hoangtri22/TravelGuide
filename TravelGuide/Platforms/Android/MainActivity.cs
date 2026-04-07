using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Maui.Devices.Sensors;
using System.Runtime.Versioning;
using TravelGuide.Platforms.Android;

namespace TravelGuide.Platforms.Android;

/// <summary>
/// Activity chính của ứng dụng
/// - Khởi động app
/// - Start LocationService đúng lifecycle Android 14
/// </summary>
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
    ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
    ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>
    /// Tránh start service nhiều lần khi OnResume bị gọi lại
    /// </summary>
    private static bool _serviceStarted = false;

    /// <summary>
    /// Hàm khởi tạo Activity
    /// </summary>
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // ❗ Không start service ở đây
        // Android 14 chặn start Foreground Service khi app chưa fully visible
    }

    /// <summary>
    /// Hàm được gọi khi Activity vào foreground
    /// Đây là thời điểm an toàn để start Foreground Service
    /// </summary>
    [SupportedOSPlatform("android26.0")]
    protected override async void OnResume()
    {
        base.OnResume();

        try
        {
            // ✅ Tránh start nhiều lần
            if (_serviceStarted)
            {
                System.Diagnostics.Debug.WriteLine("[MainActivity] Service already started");
                return;
            }

            // ✅ Check permission trước khi start
            var status = await Permissions.CheckStatusAsync<Permissions.LocationAlways>();
            if (status != PermissionStatus.Granted)
            {
                System.Diagnostics.Debug.WriteLine("[MainActivity] No location permission");
                return;
            }

            // Tạo intent gọi service
            var intent = new Intent(this, typeof(LocationService));

            // Android 8+ dùng ForegroundService
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                StartForegroundService(intent);
            else
                StartService(intent);

            _serviceStarted = true;

            System.Diagnostics.Debug.WriteLine("[MainActivity] LocationService started");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainActivity] Service error: {ex.Message}");
        }
    }

    /// <summary>
    /// Hàm được gọi khi Activity bị destroy
    /// </summary>
    protected override void OnDestroy()
    {
        base.OnDestroy();

        // ❗ Không stop service
        // Giữ service chạy nền ngay cả khi app đóng
    }
}