using TravelGuide.Models;
using System.Collections.ObjectModel;

namespace TravelGuide;

/// <summary>
/// ViewModel/item đại diện cho một địa điểm trong danh sách AudioPage.
/// Bao bọc TouristPlace và thêm các property phục vụ hiển thị UI.
/// </summary>
public class AudioItem
{
    /// <summary>Địa điểm gốc từ database</summary>
    public TouristPlace Place { get; set; } = null!;

    /// <summary>Số thứ tự hiển thị trong danh sách (1, 2, 3...)</summary>
    public string OrderIndex { get; set; } = "";

    // FIX: Dùng null-coalescing để tránh binding null crash UI
    // khi bản dịch chưa load hoặc chưa có dữ liệu

    /// <summary>Tên địa điểm theo ngôn ngữ hiện tại (fallback về NameVi nếu null)</summary>
    public string Name => Place?.Name ?? Place?.NameVi ?? "";

    /// <summary>Tóm tắt mô tả địa điểm (fallback về DescVi nếu null)</summary>
    public string Summary => Place?.Summary ?? Place?.DescVi ?? "";

    /// <summary>Icon nút play/pause: "▶" khi dừng, "⏸" khi đang phát</summary>
    public string PlayBtnIcon { get; set; } = "▶";

    /// <summary>Màu nền nút play: xanh nhạt khi đang phát, trắng xám khi dừng</summary>
    public string PlayBtnColor { get; set; } = "#EFF6FF";
}

/// <summary>
/// Trang Audio — hiển thị danh sách địa điểm với chức năng phát TTS.
/// Hỗ trợ: phát tất cả, phát ngẫu nhiên (shuffle), phát từng bài, skip, stop.
/// Tự động reload khi người dùng đổi ngôn ngữ.
/// </summary>
public partial class AudioPage : ContentPage
{
    // ─── Dependencies ──────────────────────────────────────────────────────

    private readonly DatabaseService _dbService;
    private readonly NarrationEngine _narrationEngine;

    // ─── Trạng thái UI ─────────────────────────────────────────────────────

    /// <summary>
    /// ObservableCollection để UI tự cập nhật khi danh sách thay đổi.
    /// (Dùng thay cho List thông thường để tránh phải gán lại ItemsSource)
    /// </summary>
    private ObservableCollection<AudioItem> _items = new();

    /// <summary>Cờ theo dõi đang phát hay không (dùng để toggle PlayAll button)</summary>
    private bool _isPlaying = false;

    /// <summary>Chế độ phát ngẫu nhiên (shuffle)</summary>
    private bool _shuffle = false;

    /// <summary>Token để hủy PlaySequenceAsync khi người dùng nhấn Stop</summary>
    private CancellationTokenSource? _cts;

    // ─── Constructor ───────────────────────────────────────────────────────

    public AudioPage(DatabaseService dbService, NarrationEngine narrationEngine)
    {
        InitializeComponent();
        _dbService = dbService;
        _narrationEngine = narrationEngine;

        // Gán data source cho CollectionView
        AudioList.ItemsSource = _items;

        // Lắng nghe sự kiện đổi ngôn ngữ để reload nội dung
        // Nhớ unsubscribe trong OnDisappearing để tránh memory leak
        AppLanguage.OnLanguageChanged += OnLanguageChanged;
    }

    // ─── Lifecycle ─────────────────────────────────────────────────────────

    /// <summary>
    /// Gọi mỗi khi trang hiện ra (kể cả khi quay lại từ trang khác).
    /// Attach MiniPlayer và load dữ liệu mới nhất từ database.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Kết nối MiniPlayer với NarrationEngine để hiển thị trạng thái phát
        MiniPlayer.Attach(_narrationEngine);
        _dbService.ClearCache();
        await LoadAsync();
    }

    /// <summary>
    /// Gọi khi trang bị ẩn/đóng.
    /// Quan trọng: Unsubscribe event để tránh memory leak và callback
    ///    vào trang đã bị dispose.
    /// </summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        AppLanguage.OnLanguageChanged -= OnLanguageChanged;
    }

    // ─── Event handlers ────────────────────────────────────────────────────

    /// <summary>
    /// Xử lý khi ngôn ngữ ứng dụng thay đổi.
    /// Phải chạy trên MainThread vì thao tác UI (LoadAsync cập nhật CollectionView).
    /// </summary>
    private void OnLanguageChanged(string _)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await LoadAsync();
        });
    }

    /// <summary>
    /// Nhấn nút "Phát tất cả" / "Dừng":
    /// - Nếu đang phát → dừng lại
    /// - Nếu đang dừng → phát toàn bộ danh sách (hoặc shuffle nếu bật)
    /// </summary>
    private async void OnPlayAllClicked(object sender, EventArgs e)
    {
        if (_isPlaying)
        {
            StopPlayback();
            return;
        }

        // Sắp xếp danh sách: ngẫu nhiên nếu shuffle, giữ thứ tự gốc nếu không
        var list = _shuffle
            ? _items.OrderBy(_ => Guid.NewGuid()).ToList()
            : _items.ToList();

        await PlaySequenceAsync(list);
    }

    /// <summary>
    /// Toggle chế độ Shuffle (phát ngẫu nhiên).
    /// Đổi màu nút để phản hồi trạng thái bật/tắt.
    /// Nếu đang phát → dừng và bắt đầu lại với thứ tự mới.
    /// </summary>
    private async void OnShuffleClicked(object sender, EventArgs e)
    {
        _shuffle = !_shuffle;

        // Đổi màu nút để biểu thị trạng thái shuffle
        BtnShuffle.BackgroundColor = _shuffle
            ? Color.FromArgb("#DBEAFE") // Xanh = đang bật
            : Color.FromArgb("#F1F5F9"); // Xám = đang tắt

        BtnShuffle.TextColor = _shuffle
            ? Color.FromArgb("#1A56DB")
            : Color.FromArgb("#475569");

        // Nếu đang phát → restart với thứ tự mới
        if (_isPlaying)
        {
            StopPlayback();
            var list = _items.OrderBy(_ => Guid.NewGuid()).ToList();
            await PlaySequenceAsync(list);
        }
    }

    /// <summary>
    /// Tap vào nút play của một item cụ thể trong danh sách.
    /// Dừng bài đang phát (nếu có) và phát riêng item được chọn.
    /// </summary>
    private async void OnPlayItemTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not AudioItem item) return;

        // Reset icon tất cả item về trạng thái dừng
        ResetAllItems();

        // Set item được chọn sang trạng thái đang phát
        SetItemState(item, true);
        UpdateNowPlaying(item.Name);

        _isPlaying = true;
        UpdatePlayAllBtn(true);

        // Phát đúng item này (hủy hàng đợi geofence / bài khác)
        await _narrationEngine.SpeakExclusiveAsync(item.Place);

        // Kết thúc → reset UI
        SetItemState(item, false);
        _isPlaying = false;
        UpdatePlayAllBtn(false);
        NowPlayingBar.IsVisible = false;
    }

    /// <summary>
    /// Nhấn nút ⏭ (Next) trên NowPlayingBar → bỏ qua bài hiện tại.
    /// NarrationEngine sẽ tự chuyển sang item tiếp theo trong queue.
    /// </summary>
    private void OnNextClicked(object sender, EventArgs e)
    {
        _narrationEngine.SkipNext();
    }

    /// <summary>
    /// Nhấn nút ⏹ (Stop) → dừng toàn bộ, reset UI và hủy queue.
    /// </summary>
    private async void OnStopClicked(object sender, EventArgs e)
    {
        StopPlayback(); // Reset UI ngay lập tức
        await _narrationEngine.StopAsync(); // Dừng TTS engine
    }

    // ─── Private: Load dữ liệu ─────────────────────────────────────────────

    /// <summary>
    /// Load danh sách địa điểm từ database và cập nhật CollectionView.
    /// Cũng cập nhật label đếm số lượng và tên ngôn ngữ hiện tại.
    /// </summary>
    private async Task LoadAsync()
    {
        var places = await _dbService.GetPlacesAsync();

        _items.Clear();

        int index = 1;
        foreach (var p in places)
        {
            _items.Add(new AudioItem
            {
                Place = p,
                OrderIndex = index.ToString()
            });
            index++;
        }

        // Cập nhật label đếm số địa điểm theo ngôn ngữ hiện tại
        LblCount.Text = $"{_items.Count} {AppLanguage.Current switch
        {
            "en" => "places",
            "ja" => "スポット",
            "ko" => "장소",
            "zh" => "个景点",
            _ => "địa điểm"
        }}";

        // Hiển thị tên đầy đủ của ngôn ngữ đang chọn (vd: "Tiếng Việt")
        LblLang.Text = AppLanguage.GetLanguageName(AppLanguage.Current);
    }

    // ─── Private: Điều khiển phát ──────────────────────────────────────────

    /// <summary>
    /// Phát tuần tự toàn bộ danh sách truyền vào.
    /// Mỗi item được phát xong mới chuyển sang item tiếp theo.
    /// Có thể bị hủy bởi StopPlayback() qua CancellationToken.
    /// </summary>
    private async Task PlaySequenceAsync(List<AudioItem> list)
    {
        _isPlaying = true;
        _cts = new CancellationTokenSource();

        UpdatePlayAllBtn(true);

        try
        {
            foreach (var item in list)
            {
                // Kiểm tra đã bị cancel chưa (người dùng nhấn Stop)
                if (_cts.Token.IsCancellationRequested) break;

                SetItemState(item, true);
                UpdateNowPlaying(item.Name);

                await _narrationEngine.SpeakExclusiveAsync(item.Place);

                SetItemState(item, false);

                // Nghỉ ngắn giữa các địa điểm cho tự nhiên hơn
                await Task.Delay(300);
            }
        }
        finally
        {
            // Luôn reset UI dù phát xong bình thường hay bị dừng
            StopPlayback();
        }
    }

    /// <summary>
    /// Phát một item đơn lẻ (không dùng sequence).
    /// Hiện không được gọi trực tiếp, dự phòng cho tính năng sau.
    /// </summary>
    private async Task PlaySingleAsync(AudioItem item)
    {
        _isPlaying = true;

        UpdatePlayAllBtn(true);
        SetItemState(item, true);
        UpdateNowPlaying(item.Name);

        await _narrationEngine.SpeakExclusiveAsync(item.Place);

        StopPlayback();
    }

    /// <summary>
    /// Dừng phát: cancel sequence, reset trạng thái UI, ẩn NowPlayingBar.
    /// Không trực tiếp dừng NarrationEngine — gọi _narrationEngine.StopAsync() riêng.
    /// </summary>
    private void StopPlayback()
    {
        _cts?.Cancel();
        _isPlaying = false;

        UpdatePlayAllBtn(false);
        ResetAllItems();

        NowPlayingBar.IsVisible = false;
    }

    // ─── Private: Cập nhật UI ─────────────────────────────────────────────

    /// <summary>
    /// Đổi icon và màu nút play của một item.
    /// isPlaying=true → icon pause (⏸), màu xanh; false → icon play (▶), màu trắng xám.
    /// </summary>
    private void SetItemState(AudioItem item, bool isPlaying)
    {
        item.PlayBtnIcon = isPlaying ? "⏸" : "▶";
        item.PlayBtnColor = isPlaying ? "#DBEAFE" : "#EFF6FF";
        RefreshList();
    }

    /// <summary>
    /// Reset toàn bộ item trong danh sách về trạng thái "dừng".
    /// Gọi khi Stop hoặc khi bắt đầu play một item mới (để xóa highlight cũ).
    /// </summary>
    private void ResetAllItems()
    {
        foreach (var i in _items)
        {
            i.PlayBtnIcon = "▶";
            i.PlayBtnColor = "#EFF6FF";
        }
        RefreshList();
    }

    /// <summary>
    /// Refresh CollectionView nhẹ bằng cách gán lại ItemsSource.
    /// Dùng thay cho NotifyPropertyChanged vì AudioItem chưa implement INotifyPropertyChanged.
    /// Chạy trên MainThread vì thao tác UI.
    /// </summary>
    private void RefreshList()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            AudioList.ItemsSource = null;
            AudioList.ItemsSource = _items;
        });
    }

    /// <summary>
    /// Cập nhật text và trạng thái nút "Phát tất cả" theo ngôn ngữ hiện tại.
    /// Hiện/ẩn nút Stop tương ứng với trạng thái phát.
    /// </summary>
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

            // Hiện nút Stop khi đang phát, ẩn khi dừng
            BtnStop.IsVisible = isPlaying;
        });
    }

    /// <summary>
    /// Cập nhật NowPlayingBar với tên địa điểm đang phát và trạng thái ngôn ngữ.
    /// Hiển thị thanh bar ở cuối màn hình.
    /// </summary>
    private void UpdateNowPlaying(string name)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Tên địa điểm đang phát
            LblNowPlaying.Text = name;

            // Status text theo ngôn ngữ hiện tại
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