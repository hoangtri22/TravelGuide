using TravelGuide.Models;

namespace TravelGuide;

/// <summary>
/// Thanh phát nhỏ: hiển thị tên POI đang TTS, nút dừng/next; lắng nghe <see cref="NarrationEngine"/>.
/// </summary>
public partial class MiniPlayerView : ContentView
{
    private NarrationEngine? _engine;

    /// <summary>Khởi tạo XAML.</summary>
    public MiniPlayerView()
    {
        InitializeComponent();
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
            PlayerBar.IsVisible = false;
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
            PlayerBar.IsVisible = true;
        });
    }

    /// <summary>Callback <see cref="NarrationEngine.OnStoppedPlaying"/>: ẩn thanh.</summary>
    private void OnStopped()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PlayerBar.IsVisible = false;
        });
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