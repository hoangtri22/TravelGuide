using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using System;

namespace TravelGuide;

/// <summary>Host MAUI cho Tizen (nếu bật target Tizen trong solution).</summary>
internal class Program : MauiApplication
{
    /// <inheritdoc />
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    /// <summary>Khởi chạy ứng dụng Tizen.</summary>
    static void Main(string[] args)
    {
        var app = new Program();
        app.Run(args);
    }
}
