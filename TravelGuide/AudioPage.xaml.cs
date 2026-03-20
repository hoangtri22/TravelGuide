using TravelGuide.Models;

namespace TravelGuide;

public class AudioItem
{
    public TouristPlace Place { get; set; } = null!;
    public string OrderIndex { get; set; } = "";
    public string Name => Place.Name;
    public string Summary => Place.Summary;
    public string PlayBtnIcon { get; set; } = "▶";
    public string PlayBtnColor { get; set; } = "#EFF6FF";
}

public partial class AudioPage : ContentPage
{
    private readonly DatabaseService _dbService;
    private readonly NarrationEngine _narrationEngine;

    private List<AudioItem> _items = new();
    private bool _isPlaying = false;
    private bool _shuffle = false;
    private CancellationTokenSource? _cts;

    public AudioPage(DatabaseService dbService, NarrationEngine narrationEngine)
    {
        InitializeComponent();
        _dbService = dbService;
        _narrationEngine = narrationEngine;

        // Reload khi đổi ngôn ngữ
        AppLanguage.OnLanguageChanged += _ =>
            MainThread.BeginInvokeOnMainThread(async () =>
                await LoadAsync());
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        MiniPlayer.Attach(_narrationEngine);
        await LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
    }

    private async Task LoadAsync()
    {
        var places = await _dbService.GetPlacesAsync();
        _items = places.Select((p, i) => new AudioItem
        {
            Place = p,
            OrderIndex = (i + 1).ToString(),
            PlayBtnIcon = "▶",
            PlayBtnColor = "#EFF6FF"
        }).ToList();

        // Force refresh để Name/Summary lấy đúng ngôn ngữ
        AudioList.ItemsSource = null;
        AudioList.ItemsSource = _items;

        LblCount.Text = $"{_items.Count} {AppLanguage.Current switch
        {
            "en" => "places",
            "ja" => "スポット",
            "ko" => "장소",
            "zh" => "个景点",
            _ => "địa điểm"
        }}";
        LblLang.Text = $"{AppLanguage.GetLanguageName(AppLanguage.Current)}";
    }

    private async void OnPlayAllClicked(object sender, EventArgs e)
    {
        if (_isPlaying) { StopPlayback(); return; }
        var list = _shuffle
            ? _items.OrderBy(_ => Guid.NewGuid()).ToList()
            : _items;
        await PlaySequenceAsync(list);
    }

    private async void OnShuffleClicked(object sender, EventArgs e)
    {
        _shuffle = !_shuffle;
        BtnShuffle.BackgroundColor = _shuffle
            ? Color.FromArgb("#DBEAFE") : Color.FromArgb("#F1F5F9");
        BtnShuffle.TextColor = _shuffle
            ? Color.FromArgb("#1A56DB") : Color.FromArgb("#475569");

        if (_isPlaying)
        {
            StopPlayback();
            var list = _items.OrderBy(_ => Guid.NewGuid()).ToList();
            await PlaySequenceAsync(list);
        }
    }

    private void OnPlayItemTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not AudioItem item) return;
        if (_isPlaying) StopPlayback();
        _ = PlaySingleAndUpdateUIAsync(item);
    }

    private void OnNextClicked(object sender, EventArgs e)
    {
        _narrationEngine.SkipNext();
    }

    private async void OnStopClicked(object sender, EventArgs e)
    {
        StopPlayback();
        await _narrationEngine.StopAsync();
    }

    private async Task PlaySequenceAsync(List<AudioItem> list)
    {
        _isPlaying = true;
        _cts = new CancellationTokenSource();
        UpdatePlayAllBtn(true);

        try
        {
            foreach (var item in list)
            {
                if (_cts.Token.IsCancellationRequested) break;
                SetItemState(item, true);
                UpdateNowPlaying(item.Name);
                await _narrationEngine.SpeakAsync(item.Place);

                while (_narrationEngine.IsSpeaking &&
                       _narrationEngine.CurrentPlace?.Id == item.Place.Id &&
                       !_cts.Token.IsCancellationRequested)
                    await Task.Delay(200);

                SetItemState(item, false);
                if (!_cts.Token.IsCancellationRequested)
                    await Task.Delay(400);
            }
        }
        finally
        {
            _isPlaying = false;
            UpdatePlayAllBtn(false);
            ResetAllItems();
            NowPlayingBar.IsVisible = false;
        }
    }

    private async Task PlaySingleAndUpdateUIAsync(AudioItem item)
    {
        _isPlaying = true;
        UpdatePlayAllBtn(true);
        SetItemState(item, true);
        UpdateNowPlaying(item.Name);

        await _narrationEngine.SpeakAsync(item.Place);

        while (_narrationEngine.IsSpeaking &&
               _narrationEngine.CurrentPlace?.Id == item.Place.Id)
            await Task.Delay(200);

        SetItemState(item, false);
        _isPlaying = false;
        UpdatePlayAllBtn(false);
        NowPlayingBar.IsVisible = false;
    }

    private void StopPlayback()
    {
        _cts?.Cancel();
        _isPlaying = false;
        UpdatePlayAllBtn(false);
        ResetAllItems();
        NowPlayingBar.IsVisible = false;
    }

    private void SetItemState(AudioItem item, bool isPlaying)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            item.PlayBtnIcon = isPlaying ? "⏸" : "▶";
            item.PlayBtnColor = isPlaying ? "#DBEAFE" : "#EFF6FF";
            AudioList.ItemsSource = null;
            AudioList.ItemsSource = _items;
        });
    }

    private void ResetAllItems()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var i in _items)
            {
                i.PlayBtnIcon = "▶";
                i.PlayBtnColor = "#EFF6FF";
            }
            AudioList.ItemsSource = null;
            AudioList.ItemsSource = _items;
        });
    }

    private void UpdatePlayAllBtn(bool isPlaying)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            BtnPlayAll.Text = isPlaying
                ? AppLanguage.Current switch
                {
                    "en" => "⏸  Playing...",
                    "ja" => "⏸  再生中...",
                    "ko" => "⏸  재생 중...",
                    "zh" => "⏸  播放中...",
                    _ => "⏸  Đang phát..."
                }
                : AppLanguage.Current switch
                {
                    "en" => "▶  Play all",
                    "ja" => "▶  すべて再生",
                    "ko" => "▶  전체 재생",
                    "zh" => "▶  全部播放",
                    _ => "▶  Phát tất cả"
                };
            BtnPlayAll.BackgroundColor = isPlaying
                ? Color.FromArgb("#0F4C81") : Color.FromArgb("#1A56DB");
            BtnStop.IsVisible = isPlaying;
        });
    }

    private void UpdateNowPlaying(string name)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LblNowPlaying.Text = name;
            LblNowPlayingStatus.Text = AppLanguage.Current switch
            {
                "en" => "Playing...",
                "ja" => "再生中...",
                "ko" => "재생 중...",
                "zh" => "播放中...",
                _ => "Đang phát..."
            };
            NowPlayingBar.IsVisible = true;
        });
    }
}