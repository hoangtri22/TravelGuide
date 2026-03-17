using Android.App;
using Android.Content;
using Android.OS;
using System.Runtime.Versioning;
using Microsoft.Maui.Devices.Sensors;
using CommunityToolkit.Mvvm.Messaging;
using TravelGuide.Models;
using Android.Content.PM; // Thêm cái này để dùng ForegroundService ngắn gọn

namespace TravelGuide.Platforms.Android;

// SỬA Ở ĐÂY: Thêm DataSync vào attribute để khớp với code bên dưới
[Service(ForegroundServiceType = ForegroundService.TypeLocation | ForegroundService.TypeDataSync)]
[SupportedOSPlatform("android26.0")]
public class LocationService : Service
{
    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        string channelId = "location_notification_channel";
        var notificationManager = GetSystemService(NotificationService) as NotificationManager;

        if (notificationManager != null && notificationManager.GetNotificationChannel(channelId) == null)
        {
            var channel = new NotificationChannel(channelId, "Location Tracking", NotificationImportance.Low);
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
            // Đã khớp hoàn toàn với thuộc tính [Service] ở trên đầu
            var type = ForegroundService.TypeLocation | ForegroundService.TypeDataSync;
            StartForeground(1001, notification, type);
        }
        else
        {
            StartForeground(1001, notification);
        }

        // Vòng lặp lấy vị trí (Giữ nguyên logic của bạn)
        Task.Run(async () => {
            while (true)
            {
                try
                {
                    var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)));
                    if (location != null)
                    {
                        WeakReferenceMessenger.Default.Send(new LocationMessage(location));
                        System.Diagnostics.Debug.WriteLine($"[GPS] Lat: {location.Latitude}, Lon: {location.Longitude}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[GPS Error] {ex.Message}");
                }
                await Task.Delay(10000); // 10 giây quét một lần để đỡ tốn pin/RAM
            }
        });

        return StartCommandResult.Sticky;
    }
}