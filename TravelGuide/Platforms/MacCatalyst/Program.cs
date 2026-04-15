using ObjCRuntime;
using UIKit;

namespace TravelGuide;

/// <summary>Entry Mac Catalyst — <see cref="UIApplication.Main"/> với <see cref="AppDelegate"/>.</summary>
public class Program
{
    /// <summary>Điểm vào native.</summary>
    static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
