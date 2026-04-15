namespace TravelGuide;

/// <summary>
/// Thanh điều hướng dùng chung cho các trang chính của app.
/// </summary>
public partial class BottomNavBarView : ContentView
{
    public BottomNavBarView()
    {
        InitializeComponent();
    }

    private static async Task NavigateAsync(string route)
    {
        try
        {
            await Shell.Current.GoToAsync(route);
        }
        catch
        {
            // Ignore navigation errors to keep UI responsive.
        }
    }

    private async void OnHomeTapped(object? sender, EventArgs e) => await NavigateAsync(nameof(HomePage));
    private async void OnMapTapped(object? sender, EventArgs e) => await NavigateAsync(nameof(MapPage));
    private async void OnAudioTapped(object? sender, EventArgs e) => await NavigateAsync(nameof(AudioPage));
    private async void OnQrTapped(object? sender, EventArgs e) => await NavigateAsync(nameof(QrScannerPage));
    private async void OnHistoryTapped(object? sender, EventArgs e) => await NavigateAsync(nameof(QrScanHistoryPage));
}
