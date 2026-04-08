using Microsoft.Maui.Media;
using TravelGuide.Models;

namespace TravelGuide;

/// <summary>
/// Engine xử lý Text-To-Speech (TTS) cho ứng dụng TravelGuide.
/// Quản lý hàng đợi phát âm thanh, hỗ trợ nhiều ngôn ngữ,
/// cho phép dừng/bỏ qua từng địa điểm.
/// </summary>
public class NarrationEngine
{
    // ─── Trạng thái nội bộ ────────────────────────────────────────────────

    /// <summary>Cờ kiểm tra đang phát hay không</summary>
    private bool _isSpeaking = false;

    /// <summary>Token để hủy lệnh TTS hiện tại (dùng cho SkipNext)</summary>
    private CancellationTokenSource? _cts;

    /// <summary>Cache danh sách locale TTS có sẵn trên thiết bị</summary>
    private IEnumerable<Locale>? _cachedLocales;

    /// <summary>Hàng đợi các địa điểm chờ được phát</summary>
    private readonly Queue<TouristPlace> _queue = new();

    // ─── Public properties ────────────────────────────────────────────────

    /// <summary>Trả về true nếu TTS đang chạy</summary>
    public bool IsSpeaking => _isSpeaking;

    /// <summary>Địa điểm đang được phát hiện tại (null nếu rảnh)</summary>
    public TouristPlace? CurrentPlace { get; private set; }

    // ─── Events ───────────────────────────────────────────────────────────

    /// <summary>Kích hoạt khi bắt đầu phát một địa điểm mới</summary>
    public event Action<TouristPlace>? OnStartedPlaying;

    /// <summary>Kích hoạt khi dừng toàn bộ (hết hàng đợi hoặc bị stop)</summary>
    public event Action? OnStoppedPlaying;

    // ─── Public methods ───────────────────────────────────────────────────

    /// <summary>
    /// Thêm một địa điểm vào hàng đợi và bắt đầu phát nếu chưa phát.
    /// Nếu đang phát → địa điểm sẽ được xếp vào cuối hàng đợi.
    /// </summary>
    public async Task SpeakAsync(TouristPlace place)
    {
        _queue.Enqueue(place);

        // Chỉ gọi ProcessQueueAsync nếu chưa có vòng lặp nào đang chạy
        if (!_isSpeaking)
            await ProcessQueueAsync();
    }

    /// <summary>
    /// Dừng hoàn toàn: hủy TTS hiện tại, xóa hàng đợi, reset trạng thái.
    /// ✅ FIX: Không dùng SpeakAsync("") vì Android ném ArgumentNullException
    ///         với chuỗi rỗng — thay bằng Cancel token và reset thủ công.
    /// </summary>
    public Task StopAsync()
    {
        // Hủy lệnh TTS đang chạy (nếu có)
        _cts?.Cancel();

        // Xóa toàn bộ hàng đợi chờ
        _queue.Clear();

        // Reset trạng thái về idle
        _isSpeaking = false;
        CurrentPlace = null;

        // Thông báo UI rằng đã dừng
        OnStoppedPlaying?.Invoke();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Bỏ qua địa điểm đang phát, chuyển sang địa điểm tiếp theo trong hàng đợi.
    /// Hoạt động bằng cách cancel token hiện tại — ProcessQueueAsync sẽ tự
    /// bắt OperationCanceledException và tiếp tục item kế.
    /// </summary>
    public void SkipNext() => _cts?.Cancel();

    // ─── Private: Vòng lặp xử lý hàng đợi ───────────────────────────────

    /// <summary>
    /// Vòng lặp chính: lấy từng địa điểm trong hàng đợi và phát TTS tuần tự.
    /// Chạy cho đến khi hàng đợi rỗng hoặc bị StopAsync() hủy.
    /// </summary>
    private async Task ProcessQueueAsync()
    {
        _isSpeaking = true;

        // Reset cache locale mỗi lần bắt đầu để lấy đúng ngôn ngữ hiện tại
        // (người dùng có thể đã thay đổi ngôn ngữ hệ thống)
        _cachedLocales = await TextToSpeech.Default.GetLocalesAsync();

        try
        {
            while (_queue.Count > 0)
            {
                // Tạo CancellationToken mới cho mỗi địa điểm
                // → SkipNext() chỉ cancel item hiện tại, không ảnh hưởng item sau
                _cts = new CancellationTokenSource();

                var place = _queue.Dequeue();
                CurrentPlace = place;

                // Thông báo UI địa điểm đang được phát
                OnStartedPlaying?.Invoke(place);

                // Tạo nội dung văn bản và cài đặt giọng đọc
                var text = BuildNarrationText(place);
                var opts = BuildSpeechOptions(_cachedLocales);

                // Log debug ra Output window của Visual Studio
                System.Diagnostics.Debug.WriteLine(
                    $"[TTS] Speaking [{AppLanguage.Current}]: {place.Name}");
                System.Diagnostics.Debug.WriteLine(
                    $"[TTS] Text: {text}");
                System.Diagnostics.Debug.WriteLine(
                    $"[TTS] Locale: {opts.Locale?.Language}-{opts.Locale?.Country}");

                try
                {
                    // Phát TTS — có thể bị cancel bởi SkipNext() hoặc StopAsync()
                    await TextToSpeech.Default.SpeakAsync(text, opts, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Bị skip/stop — không làm gì, tiếp tục vòng lặp
                    // Nếu hàng đợi còn item → phát tiếp; nếu rỗng → thoát while
                }

                // Nghỉ 600ms giữa các địa điểm để tránh TTS bị cắt giữa chừng
                if (_queue.Count > 0)
                    await Task.Delay(600).ContinueWith(_ => { });
            }
        }
        finally
        {
            // Luôn reset trạng thái dù thành công hay bị exception
            _isSpeaking = false;
            CurrentPlace = null;
            OnStoppedPlaying?.Invoke();
        }
    }

    // ─── Private: Tạo nội dung TTS ────────────────────────────────────────

    /// <summary>
    /// Tạo câu giới thiệu địa điểm theo ngôn ngữ hiện tại của ứng dụng.
    /// Kết hợp tên địa điểm (đã dịch) và mô tả vào một câu hoàn chỉnh.
    /// </summary>
    private static string BuildNarrationText(TouristPlace place) =>
        AppLanguage.Current switch
        {
            "vi" => $"Bạn đang đến gần {place.Name}. {place.Description}",
            "en" => $"You are approaching {place.Name}. {place.Description}",
            "ja" => $"{place.Name}に近づいています。{place.Description}",
            "ko" => $"{place.Name}에 가까워지고 있습니다. {place.Description}",
            "zh" => $"您正在接近{place.Name}。{place.Description}",
            _ => $"{place.Name}. {place.Description}" // Fallback cho ngôn ngữ không hỗ trợ
        };

    /// <summary>
    /// Tìm locale TTS phù hợp nhất với ngôn ngữ hiện tại từ danh sách thiết bị hỗ trợ.
    /// Ưu tiên: khớp cả language + country → chỉ khớp language → null (system default).
    /// ✅ FIX: Bỏ fallback cứng về en-US để tránh đọc sai ngôn ngữ khi không tìm thấy locale.
    /// </summary>
    private static SpeechOptions BuildSpeechOptions(IEnumerable<Locale> available)
    {
        // Xác định cặp (ngôn ngữ, quốc gia) cần tìm dựa trên AppLanguage hiện tại
        var (lang, country) = AppLanguage.Current switch
        {
            "vi" => ("vi", "VN"),
            "en" => ("en", "US"),
            "ja" => ("ja", "JP"),
            "ko" => ("ko", "KR"),
            "zh" => ("zh", "CN"),
            _ => ("vi", "VN") // Fallback về tiếng Việt thay vì tiếng Anh
        };

        var list = available.ToList();

        // Log tất cả locale có sẵn — giúp debug trên thiết bị thực
        System.Diagnostics.Debug.WriteLine(
            $"[TTS] Available locales ({list.Count}): " +
            string.Join(", ", list.Select(l => $"{l.Language}-{l.Country}")));

        // Bước 1: Tìm locale khớp chính xác cả language lẫn country (vd: vi-VN)
        // Bước 2: Fallback — chỉ khớp language (vd: en-GB khi không có en-US)
        // Bước 3: Nếu không tìm thấy gì → trả null, để TTS tự chọn mặc định hệ thống
        var matched =
            list.FirstOrDefault(l =>
                string.Equals(l.Language, lang, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(l.Country, country, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(l =>
                string.Equals(l.Language, lang, StringComparison.OrdinalIgnoreCase));

        System.Diagnostics.Debug.WriteLine(
            $"[TTS] Matched locale: {matched?.Language}-{matched?.Country} " +
            $"(requested: {lang}-{country})");

        return new SpeechOptions
        {
            Volume = 1.0f, // Âm lượng tối đa
            Pitch = 1.0f, // Cao độ bình thường
            Locale = matched // null = TTS tự chọn locale hệ thống
        };
    }
}