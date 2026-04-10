using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Maui.Devices.Sensors;
using TravelGuide.Models; // FIX CS0246: LocationMessage nằm trong namespace này

namespace TravelGuide;

/// <summary>
/// GPS Background Service (cross-platform MAUI):
/// Lấy vị trí liên tục, gửi LocationMessage + feed vào GeofenceEngine.
/// Dùng cho foreground / iOS. Android production dùng LocationService (Foreground Service).
/// </summary>
public class GpsBackgroundService
{
    private readonly GeofenceEngine _geofenceEngine;
    private CancellationTokenSource? _cts;
    private bool _isRunning = false;

    private const int IntervalSeconds = 5;

    public bool IsRunning => _isRunning;

    /// <summary>Tiêm <see cref="GeofenceEngine"/> để feed vị trí sau mỗi lần đọc GPS.</summary>
    public GpsBackgroundService(GeofenceEngine geofenceEngine)
    {
        _geofenceEngine = geofenceEngine;
    }

    /// <summary>Xin quyền vị trí, bật vòng lặp định kỳ gửi <see cref="LocationMessage"/> và gọi geofence.</summary>
    public async Task StartAsync()
    {
        if (_isRunning) return;

        var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
        {
            System.Diagnostics.Debug.WriteLine("[GPS] Permission denied");
            return;
        }

        _isRunning = true;
        _cts = new CancellationTokenSource();
        _ = RunLoopAsync(_cts.Token);
        System.Diagnostics.Debug.WriteLine("[GPS] Service started");
    }

    /// <summary>Hủy vòng lặp GPS (khi rời trang map hoặc tắt tính năng).</summary>
    public void Stop()
    {
        _cts?.Cancel();
        _isRunning = false;
        System.Diagnostics.Debug.WriteLine("[GPS] Service stopped");
    }

    /// <summary>Vòng lặp nền: đọc GPS mỗi <see cref="IntervalSeconds"/> giây, messenger + geofence.</summary>
    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var location = await Geolocation.Default.GetLocationAsync(
                    new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(3)),
                    token);

                if (location != null)
                {
                    WeakReferenceMessenger.Default.Send(new LocationMessage(location));
                    await _geofenceEngine.ProcessLocationAsync(location);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GPS] Error: {ex.Message}");
            }

            // FIX: Dùng try/catch thay vì .ContinueWith để xử lý cancel sạch hơn
            try { await Task.Delay(TimeSpan.FromSeconds(IntervalSeconds), token); }
            catch (OperationCanceledException) { break; }
        }

        _isRunning = false;
    }
}