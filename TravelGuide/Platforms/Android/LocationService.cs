using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Devices.Sensors;
using System.Runtime.Versioning;
using TravelGuide.Models;
using OperationCanceledException = System.OperationCanceledException;

namespace TravelGuide.Platforms.Android;

/// <summary>
/// Foreground Service dùng để theo dõi GPS liên tục ở chế độ nền.
/// - Lấy vị trí định kỳ
/// - Gửi dữ liệu qua Messenger
/// - Kết hợp Geofence để kích hoạt audio / sự kiện
/// </summary>
[Service(ForegroundServiceType = ForegroundService.TypeLocation)]
[SupportedOSPlatform("android29.0")]
public class LocationService : Service
{
    /// <summary>
    /// Token dùng để huỷ vòng lặp async khi stop service
    /// </summary>
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Không hỗ trợ binding → đây là Started Service
    /// </summary>
    public override IBinder? OnBind(Intent? intent) => null;

    /// <summary>
    /// Hàm được gọi khi service được start
    /// - Kiểm tra đã chạy chưa
    /// - Tạo notification (foreground)
    /// - Bắt đầu vòng lặp GPS
    /// </summary>
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (_cts != null)
        {
            System.Diagnostics.Debug.WriteLine("[LocationService] Already running");
            return StartCommandResult.Sticky;
        }

        // Tạo notification để service chạy nền hợp lệ
        StartForegroundNotification();

        _cts = new CancellationTokenSource();

        // Chạy vòng lặp GPS ở background thread
        _ = Task.Run(() => RunLocationLoopAsync(_cts.Token));

        System.Diagnostics.Debug.WriteLine("[LocationService] Started");

        // Sticky → nếu bị kill sẽ được hệ thống restart
        return StartCommandResult.Sticky;
    }

    /// <summary>
    /// Hàm được gọi khi service bị destroy
    /// - Huỷ task
    /// - Giải phóng tài nguyên
    /// </summary>
    public override void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        System.Diagnostics.Debug.WriteLine("[LocationService] Destroyed");

        base.OnDestroy();
    }

    /// <summary>
    /// Hàm dừng service thủ công từ UI
    /// - Huỷ task
    /// - Tắt notification
    /// - Dừng service
    /// </summary>
    public void StopServiceManually()
    {
        _cts?.Cancel();

        // Android 13+ dùng API mới
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            StopForeground(StopForegroundFlags.Remove);
        }
        else
        {
#pragma warning disable CS0618
            StopForeground(true);
#pragma warning restore CS0618
        }

        StopSelf();
    }

    /// <summary>
    /// Tạo notification để service chạy foreground
    /// Android yêu cầu bắt buộc khi chạy nền
    /// </summary>
    private void StartForegroundNotification()
    {
        const string channelId = "location_channel";

        var notificationManager = GetSystemService(NotificationService) as NotificationManager;

        // Tạo Notification Channel (Android 8+)
        if (notificationManager != null &&
            notificationManager.GetNotificationChannel(channelId) == null)
        {
            var channel = new NotificationChannel(
                channelId,
                "Location Tracking",
                NotificationImportance.Low)
            {
                Description = "Theo doi vi tri de phat thuyet minh tu dong"
            };

            notificationManager.CreateNotificationChannel(channel);
        }

        // Intent để mở lại app khi bấm notification
        var intent = new Intent(this, typeof(MainActivity));
        var pendingIntent = PendingIntent.GetActivity(
            this,
            0,
            intent,
            PendingIntentFlags.Immutable);

        var notification = new Notification.Builder(this, channelId)
            .SetContentTitle("Travel Guide đang chạy")
            .SetContentText("Đang theo dõi vị trí để thuyết minh...")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMenuMyLocation)
            .SetContentIntent(pendingIntent)
            .SetOngoing(true)
            .Build();

        // Start foreground service
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            StartForeground(1001, notification, ForegroundService.TypeLocation);
        else
            StartForeground(1001, notification);
    }

    /// <summary>
    /// Vòng lặp chính:
    /// - Lấy GPS định kỳ
    /// - Gửi vị trí qua Messenger
    /// - Xử lý Geofence
    /// </summary>
    private async Task RunLocationLoopAsync(CancellationToken token)
    {
        GeofenceEngine? geofenceEngine = null;

        // Lấy service từ DI container
        try
        {
            geofenceEngine = IPlatformApplication.Current?
                .Services.GetService<GeofenceEngine>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocationService] DI error: {ex.Message}");
        }

        // Vòng lặp chạy liên tục
        while (!token.IsCancellationRequested)
        {
            try
            {
                // Kiểm tra quyền GPS
                var status = await Permissions.CheckStatusAsync<Permissions.LocationAlways>();
                if (status != PermissionStatus.Granted)
                {
                    System.Diagnostics.Debug.WriteLine("[LocationService] No location permission");
                    return;
                }

                // Lấy vị trí hiện tại
                var location = await Geolocation.Default.GetLocationAsync(
                    new GeolocationRequest(
                        GeolocationAccuracy.High,
                        TimeSpan.FromSeconds(10)),
                    token);

                if (location != null)
                {
                    // Gửi vị trí cho các component khác
                    WeakReferenceMessenger.Default.Send(new LocationMessage(location));

                    // Xử lý geofence
                    if (geofenceEngine != null)
                        await geofenceEngine.ProcessLocationAsync(location);

                    System.Diagnostics.Debug.WriteLine(
                        $"[LocationService][BG] {location.Latitude:F5}, {location.Longitude:F5}");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocationService] Error: {ex.Message}");
            }

            // Delay giữa các lần lấy GPS
            try
            {
                await Task.Delay(10_000, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}