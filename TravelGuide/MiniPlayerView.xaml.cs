using TravelGuide.Models;
using Microsoft.Extensions.DependencyInjection;

namespace TravelGuide;

/// <summary>
/// Thanh phát nhỏ: hiển thị tên POI đang TTS, nút dừng/next; lắng nghe <see cref="NarrationEngine"/>.
/// </summary>
public partial class MiniPlayerView : ContentView
{
    private NarrationEngine? _engine;
    private bool _isAttached;

    /// <summary>Khởi tạo XAML.</summary>
    public MiniPlayerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (_isAttached) return;
        var engine = Handler?.MauiContext?.Services.GetService<NarrationEngine>();
        if (engine is null) return;
        Attach(engine);
        _isAttached = true;
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (_engine != null)
        {
            _engine.OnStartedPlaying -= OnStarted;
            _engine.OnStoppedPlaying -= OnStopped;
            _engine = null;
        }

        _isAttached = false;
    }

    /// <summary>Gỡ/bật handler cũ, subscribe sự kiện engine và đồng bộ trạng thái đang phát.</summary>
    public void Attach(NarrationEngine engine)
    {
        if (_engine != null)
        {
            _engine.OnStartedPlaying -= OnStarted;
            _engine.OnStoppedPlaying -= OnStopped;
        }
        _engine = engine;
        _engine.OnStartedPlaying += OnStarted;
        _engine.OnStoppedPlaying += OnStopped;

        if (_engine.IsSpeaking && _engine.CurrentPlace != null)
            OnStarted(_engine.CurrentPlace);
        else
            _ = HidePlayerAsync();
    }

    /// <summary>Callback <see cref="NarrationEngine.OnStartedPlaying"/>: hiện thanh và cập nhật nhãn.</summary>
    private void OnStarted(TouristPlace place)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LblPlaceName.Text = place.Name;
            LblStatus.Text = AppLanguage.Current switch
            {
                "en" => "Playing...",
                "ja" => "再生中...",
                "ko" => "재생 중...",
                "zh" => "播放中...",
                _ => "Đang phát..."
            };
            StatusDot.Fill = new SolidColorBrush(Color.FromArgb("#22C55E"));
            _ = ShowPlayerAsync();
        });
    }

    /// <summary>Callback <see cref="NarrationEngine.OnStoppedPlaying"/>: ẩn thanh.</summary>
    private void OnStopped()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _ = HidePlayerAsync();
        });
    }

    private async Task ShowPlayerAsync()
    {
        IsVisible = true;
        PlayerBar.IsVisible = true;
        this.TranslationY = 8;
        await this.FadeTo(1, 120, Easing.CubicOut);
        await this.TranslateTo(0, 0, 120, Easing.CubicOut);
    }

    private async Task HidePlayerAsync()
    {
        await this.FadeTo(0, 100, Easing.CubicIn);
        PlayerBar.IsVisible = false;
        IsVisible = false;
        this.TranslationY = 0;
    }

    /// <summary>Người dùng chạm dừng → <see cref="NarrationEngine.StopAsync"/>.</summary>
    private async void OnStopTapped(object sender, TappedEventArgs e)
    {
        if (_engine != null)
            await _engine.StopAsync();
    }

    /// <summary>Người dùng chạm bỏ qua → <see cref="NarrationEngine.SkipNext"/>.</summary>
    private void OnNextTapped(object sender, TappedEventArgs e)
    {
        _engine?.SkipNext();
        LblStatus.Text = AppLanguage.Current switch
        {
            "en" => "Switching...",
            "ja" => "切り替え中...",
            "ko" => "전환 중...",
            "zh" => "切换中...",
            _ => "Đang chuyển..."
        };
    }
}