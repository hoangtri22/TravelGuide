using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Maui.Devices.Sensors;

namespace TravelGuide.Models;

/// <summary>
/// Message vận chuyển tọa độ GPS qua WeakReferenceMessenger.
/// Đây là file duy nhất khai báo LocationMessage — đã xoá bản trùng trong MapPage.xaml.cs.
/// </summary>
public class LocationMessage : ValueChangedMessage<Location>
{
    public LocationMessage(Location location) : base(location) { }
}