using ObjCRuntime;
using UIKit;

namespace TravelGuide;

/// <summary>Hàm <c>Main</c> native iOS — chuyển điều khiển cho <see cref="AppDelegate"/>.</summary>
public class Program
{
    /// <summary>Entry point runtime iOS.</summary>
    static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
