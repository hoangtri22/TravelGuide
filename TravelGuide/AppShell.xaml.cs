namespace TravelGuide;

/// <summary>
/// Shell điều hướng chính; đăng ký route tới <see cref="MapPage"/> và <see cref="AudioPage"/>.
/// </summary>
public partial class AppShell : Shell
{
    /// <summary>Khởi tạo flyout/tab theo XAML và <see cref="Routing.RegisterRoute(string, Type)"/>.</summary>
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(nameof(MapPage), typeof(MapPage));
        Routing.RegisterRoute(nameof(AudioPage), typeof(AudioPage));
    }
}