using TravelGuide.Models;

namespace TravelGuide;

public partial class MiniPlayerView : ContentView
{
    private NarrationEngine? _engine;

    public MiniPlayerView()
    {
        InitializeComponent();
    }

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

    private void OnStopped()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PlayerBar.IsVisible = false;
        });
    }

    private async void OnStopTapped(object sender, TappedEventArgs e)
    {
        if (_engine != null)
            await _engine.StopAsync();
    }

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