using Android.App;
using Android.Content;
using Android.OS;
using System.Runtime.Versioning;
using Microsoft.Maui.Devices.Sensors;
using CommunityToolkit.Mvvm.Messaging;
using TravelGuide.Models;
using Android.Content.PM;
using Microsoft.Extensions.DependencyInjection;

namespace TravelGuide.Platforms.Android;

[Service(ForegroundServiceType = ForegroundService.TypeLocation | ForegroundService.TypeDataSync)]
[SupportedOSPlatform("android26.0")]
public class LocationService : Service
{
    // FIX: CancellationTokenSource để dừng vòng lặp khi service bị kill
    private CancellationTokenSource? _cts;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        StartForegroundNotification();

        _cts = new CancellationTokenSource();
        Task.Run(() => RunLocationLoopAsync(_cts.Token));

        return StartCommandResult.Sticky;
    }

    // FIX: Override OnDestroy để huỷ loop khi service dừng
    public override void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        base.OnDestroy();
        System.Diagnostics.Debug.WriteLine("[LocationService] Destroyed, GPS loop cancelled");
    }

    private void StartForegroundNotification()
    {
        const string channelId = "location_notification_channel";
        var notificationManager = GetSystemService(NotificationService) as NotificationManager;

        if (notificationManager != null && notificationManager.GetNotificationChannel(channelId) == null)
        {
            var channel = new NotificationChannel(
                channelId, "Location Tracking", NotificationImportance.Low);
            notificationManager.CreateNotificationChannel(channel);
        }

        var notification = new Notification.Builder(this, channelId)
            .SetContentTitle("TravelGuide đang chạy")
            .SetContentText("Ứng dụng đang theo dõi vị trí để thuyết minh...")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMenuMyLocation)
            .SetOngoing(true)
            .Build();

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            var type = ForegroundService.TypeLocation | ForegroundService.TypeDataSync;
            StartForeground(1001, notification, type);
        }
        else
        {
            StartForeground(1001, notification);
        }
    }

    private async Task RunLocationLoopAsync(CancellationToken token)
    {
        // FIX: Lấy GeofenceEngine từ DI để feed trực tiếp
        // (tránh duplicate: LocationService + GpsBackgroundService cùng send LocationMessage)
        GeofenceEngine? geofenceEngine = null;
        try
        {
            geofenceEngine = IPlatformApplication.Current?.Services
                .GetService<GeofenceEngine>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocationService] GeofenceEngine resolve error: {ex.Message}");
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                var location = await Geolocation.Default.GetLocationAsync(
                    new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)),
                    token);

                if (location != null)
                {
                    // Gửi cho MapPage cập nhật marker
                    WeakReferenceMessenger.Default.Send(new LocationMessage(location));

                    // FIX: Feed GeofenceEngine để trigger thuyết minh
                    if (geofenceEngine != null)
                        await geofenceEngine.ProcessLocationAsync(location);

                    System.Diagnostics.Debug.WriteLine(
                        $"[LocationService] Lat: {location.Latitude:F5}, Lon: {location.Longitude:F5}");
                }
            }
            catch (System.OperationCanceledException)
            {
                break; // thoát loop sạch khi bị cancel
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocationService] GPS Error: {ex.Message}");
            }

            // Dùng Task.Delay với token — thoát ngay khi cancel thay vì đợi đủ 10s
            try { await Task.Delay(10_000, token); }
            catch (System.OperationCanceledException) { break; }
        }

        System.Diagnostics.Debug.WriteLine("[LocationService] GPS loop exited cleanly");
    }
}