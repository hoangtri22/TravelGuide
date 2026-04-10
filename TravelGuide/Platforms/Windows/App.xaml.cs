using Microsoft.UI.Xaml;

// WinUI / MAUI Windows: http://aka.ms/winui-project-info

namespace TravelGuide.WinUI;

/// <summary>
/// Ứng dụng WinUI cho Windows — bổ sung hành vi MAUI (<see cref="MauiWinUIApplication"/>).
/// </summary>
public partial class App : MauiWinUIApplication
{
    /// <summary>Khởi tạo singleton WinUI; tương đương entry WinMain.</summary>
    public App()
    {
        this.InitializeComponent();
    }

    /// <summary>Đồng bộ với <see cref="MauiProgram.CreateMauiApp"/>.</summary>
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
