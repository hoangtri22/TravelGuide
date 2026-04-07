using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Maui.Devices.Sensors;

namespace TravelGuide.Models;

/// <summary>
/// Message vận chuyển dữ liệu GPS giữa các component (MVVM).
/// Dùng WeakReferenceMessenger để tránh coupling.
/// </summary>
public class LocationMessage : ValueChangedMessage<Location>
{
    /// <summary>
    /// Thời điểm nhận GPS
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Nguồn gửi: foreground / background
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Độ chính xác (m)
    /// </summary>
    public double? Accuracy { get; }

    /// <summary>
    /// Tốc độ di chuyển (m/s)
    /// </summary>
    public double? Speed { get; }

    /// <summary>
    /// Constructor chính
    /// </summary>
    public LocationMessage(
        Location location,
        string source = "foreground",
        double? accuracy = null,
        double? speed = null
    ) : base(location)
    {
        Timestamp = DateTime.UtcNow;
        Source = source;
        Accuracy = accuracy;
        Speed = speed;
    }

    /// <summary>
    /// Helper: khoảng cách tới location khác (m)
    /// </summary>
    public double DistanceTo(Location other)
    {
        var km = Location.CalculateDistance(Value, other, DistanceUnits.Kilometers);
        return km * 1000;
    }

    /// <summary>
    /// Debug info
    /// </summary>
    public override string ToString()
    {
        return $"[{Timestamp:HH:mm:ss}] {Source} | Lat:{Value.Latitude:F6}, Lng:{Value.Longitude:F6}, Acc:{Accuracy}, Speed:{Speed}";
    }
}