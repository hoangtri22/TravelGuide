using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Maui.Devices.Sensors; 

namespace TravelGuide.Models
{
    // Class dùng để vận chuyển tọa độ
    public class LocationMessage : ValueChangedMessage<Location>
    {
        public LocationMessage(Location location) : base(location)
        {
        }
    }
}