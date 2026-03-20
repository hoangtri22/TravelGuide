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

[Service(ForegroundServiceType = ForegroundService.TypeLocation)]
[SupportedOSPlatform("android26.0")]
public class LocationService : Service
{
    private CancellationTokenSource? _cts;
    private static bool _isRunning = false;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (_isRunning)
        {
            System.Diagnostics.Debug.WriteLine("[LocationService] Already running, skip");
            return StartCommandResult.Sticky;
        }

        StartForegroundNotification();
        _cts = new CancellationTokenSource();
        _isRunning = true;
        Task.Run(() => RunLocationLoopAsync(_cts.Token));
        System.Diagnostics.Debug.WriteLine("[LocationService] Started");
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _isRunning = false;
        base.OnDestroy();
        System.Diagnostics.Debug.WriteLine("[LocationService] Destroyed");
    }

    private void StartForegroundNotification()
    {
        const string channelId = "location_channel";
        var notificationManager = GetSystemService(NotificationService) as NotificationManager;

        if (notificationManager != null &&
            notificationManager.GetNotificationChannel(channelId) == null)
        {
            var channel = new NotificationChannel(
                channelId, "Location Tracking", NotificationImportance.Low);
            channel.Description = "Theo doi vi tri de phat thuyet minh tu dong";
            notificationManager.CreateNotificationChannel(channel);
        }

        var notification = new Notification.Builder(this, channelId)
            .SetContentTitle("Travel Guide dang chay")
            .SetContentText("Dang theo doi vi tri de thuyet minh tu dong...")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMenuMyLocation)
            .SetOngoing(true)
            .Build();

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            StartForeground(1001, notification, ForegroundService.TypeLocation);
        else
            StartForeground(1001, notification);
    }

    private async Task RunLocationLoopAsync(CancellationToken token)
    {
        GeofenceEngine? geofenceEngine = null;
        try
        {
            geofenceEngine = IPlatformApplication.Current?.Services.GetService<GeofenceEngine>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocationService] GeofenceEngine error: {ex.Message}");
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
                    WeakReferenceMessenger.Default.Send(new LocationMessage(location));
                    if (geofenceEngine != null)
                        await geofenceEngine.ProcessLocationAsync(location);
                    System.Diagnostics.Debug.WriteLine(
                        $"[LocationService] {location.Latitude:F5}, {location.Longitude:F5}");
                }
            }
            catch (System.OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LocationService] Error: {ex.Message}");
            }

            try { await Task.Delay(10_000, token); }
            catch (System.OperationCanceledException) { break; }
        }

        _isRunning = false;
    }
}