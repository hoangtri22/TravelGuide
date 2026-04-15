using System.Text.Json;
using System.Web;
using TravelGuide.Models;

namespace TravelGuide;

/// <summary>
/// Service dịch nội dung địa điểm từ tiếng Việt sang các ngôn ngữ khác.
/// Sử dụng MyMemory API (miễn phí, không cần key, giới hạn 1000 req/ngày).
/// Bản dịch được lưu vào SQLite qua DatabaseService để dùng lại offline.
/// </summary>
public class TranslationService
{
    // ─── Dependencies ──────────────────────────────────────────────────────

    private readonly HttpClient _http;
    private readonly DatabaseService _db;

    // ─── Hằng số ───────────────────────────────────────────────────────────

    /// <summary>Endpoint của MyMemory Translation API</summary>
    private const string ApiUrl = "https://api.mymemory.translated.net/get";

    /// <summary>
    /// Mapping AppLanguage code → cặp ngôn ngữ MyMemory (source|target).
    /// Luôn dịch từ tiếng Việt (vi) sang ngôn ngữ đích.
    /// Không có "vi" vì không cần dịch sang chính nó.
    /// </summary>
    private static readonly Dictionary<string, string> LangPair = new()
    {
        { "en", "vi|en" },
        { "ja", "vi|ja" },
        { "ko", "vi|ko" },
        { "zh", "vi|zh" },
    };

    // ─── Constructor ───────────────────────────────────────────────────────

    /// <summary>Tiêm HTTP client và database để dịch + cập nhật cache cục bộ.</summary>
    public TranslationService(HttpClient http, DatabaseService db)
    {
        _http = http;
        _db = db;
    }

    // ─── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Dịch một địa điểm sang ngôn ngữ đích nếu chưa có bản dịch.
    /// Nếu đã dịch rồi → bỏ qua, trả về true ngay lập tức.
    /// Sau khi dịch → lưu vào database để dùng offline lần sau.
    /// </summary>
    /// <param name="place">Địa điểm cần dịch</param>
    /// <param name="targetLang">Mã ngôn ngữ đích (en/ja/ko/zh)</param>
    /// <returns>true nếu thành công hoặc đã có bản dịch; false nếu lỗi</returns>
    public async Task<bool> TranslatePlaceAsync(TouristPlace place, string targetLang)
    {
        // Kiểm tra đã có bản dịch chưa → tránh gọi API thừa
        if (AlreadyTranslated(place, targetLang)) return true;

        try
        {
            // Dịch tên và mô tả song song (hoặc tuần tự nếu lo rate limit)
            var translatedName = await TranslateTextAsync(place.NameVi, targetLang);
            var translatedDesc = await TranslateTextAsync(place.DescVi, targetLang);

            // Nếu một trong hai thất bại → không lưu bản dịch không hoàn chỉnh
            if (translatedName == null || translatedDesc == null) return false;

            // Gán bản dịch vào đúng property của model
            ApplyTranslation(place, targetLang, translatedName, translatedDesc);

            // Lưu xuống SQLite để dùng lần sau không cần gọi API
            await _db.UpdatePlaceAsync(place);

            System.Diagnostics.Debug.WriteLine(
                $"✅ Dịch xong [{targetLang}]: {place.NameVi} → {translatedName}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ Lỗi dịch: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Dịch toàn bộ danh sách địa điểm sang một ngôn ngữ.
    /// Có progress callback để cập nhật UI loading bar.
    /// Delay 500ms giữa mỗi địa điểm để tránh vượt rate limit MyMemory.
    /// </summary>
    /// <param name="places">Danh sách địa điểm cần dịch</param>
    /// <param name="targetLang">Mã ngôn ngữ đích</param>
    /// <param name="progress">Callback báo tiến độ (current, total)</param>
    public async Task TranslateAllAsync(
        List<TouristPlace> places,
        string targetLang,
        IProgress<(int current, int total)>? progress = null)
    {
        for (int i = 0; i < places.Count; i++)
        {
            await TranslatePlaceAsync(places[i], targetLang);

            // Báo tiến độ cho UI (vd: ProgressBar, label "3/10")
            progress?.Report((i + 1, places.Count));

            // Nghỉ 500ms giữa mỗi địa điểm (~1 req/giây, tránh bị block)
            await Task.Delay(500);
        }
    }

    // ─── Private: Gọi MyMemory API ─────────────────────────────────────────

    /// <summary>
    /// Gọi MyMemory API để dịch một đoạn văn bản.
    /// ✅ FIX: Thêm guard null/empty để tránh ArgumentNullException
    ///         khi UrlEncode nhận null (xảy ra nếu NameVi hoặc DescVi chưa có dữ liệu).
    /// </summary>
    /// <param name="text">Văn bản tiếng Việt cần dịch</param>
    /// <param name="targetLang">Mã ngôn ngữ đích</param>
    /// <returns>Chuỗi đã dịch, hoặc null nếu thất bại</returns>
    private async Task<string?> TranslateTextAsync(string? text, string targetLang)
    {
        // ✅ FIX: Trả về ngay nếu text null/rỗng — tránh crash UrlEncode(null)
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Ngôn ngữ không nằm trong danh sách hỗ trợ → trả về nguyên bản
        if (!LangPair.TryGetValue(targetLang, out var langpair)) return text;

        try
        {
            // Encode text để an toàn khi đưa vào URL query string
            var encoded = HttpUtility.UrlEncode(text);
            var url = $"{ApiUrl}?q={encoded}&langpair={langpair}";

            System.Diagnostics.Debug.WriteLine(
                $"[MyMemory] → [{targetLang}]: {text.Substring(0, Math.Min(30, text.Length))}...");

            var response = await _http.GetAsync(url);

            // Kiểm tra HTTP status (200-299 = thành công)
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MyMemory] HTTP Error: {(int)response.StatusCode}");
                return null;
            }

            // Parse JSON response
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // MyMemory dùng responseStatus 200 = thành công (không phải HTTP status)
            var status = root.GetProperty("responseStatus").GetInt32();
            if (status != 200)
            {
                System.Diagnostics.Debug.WriteLine($"[MyMemory] API Error status: {status}");
                return null;
            }

            // Lấy kết quả dịch từ responseData.translatedText
            var translated = root
                .GetProperty("responseData")
                .GetProperty("translatedText")
                .GetString();

            System.Diagnostics.Debug.WriteLine(
                $"[MyMemory] ✅ {translated?.Substring(0, Math.Min(40, translated?.Length ?? 0))}");

            return translated;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MyMemory] Exception: {ex.Message}");
            return null;
        }
    }

    // ─── Private: Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Kiểm tra địa điểm đã có bản dịch sang ngôn ngữ này chưa.
    /// Với tiếng Anh: cần kiểm tra thêm không trùng với bản gốc tiếng Việt
    /// (tránh trường hợp NameEn được gán bằng NameVi khi chưa dịch).
    /// </summary>
    private static bool AlreadyTranslated(TouristPlace place, string lang) =>
        lang switch
        {
            "ja" => !string.IsNullOrWhiteSpace(place.NameJa) && !string.IsNullOrWhiteSpace(place.DescJa),
            "ko" => !string.IsNullOrWhiteSpace(place.NameKo) && !string.IsNullOrWhiteSpace(place.DescKo),
            "zh" => !string.IsNullOrWhiteSpace(place.NameZh) && !string.IsNullOrWhiteSpace(place.DescZh),
            "en" => !string.IsNullOrWhiteSpace(place.NameEn) && place.NameEn != place.NameVi
                    && !string.IsNullOrWhiteSpace(place.DescEn),
            _ => true // Ngôn ngữ không hỗ trợ → coi như đã xử lý, bỏ qua
        };

    /// <summary>
    /// Gán kết quả dịch vào đúng property ngôn ngữ của model TouristPlace.
    /// Với tiếng Anh: dùng null-coalescing để giữ giá trị cũ nếu bản dịch mới là null.
    /// </summary>
    private static void ApplyTranslation(
        TouristPlace place, string lang, string? name, string? desc)
    {
        switch (lang)
        {
            case "ja":
                place.NameJa = name;
                place.DescJa = desc;
                break;
            case "ko":
                place.NameKo = name;
                place.DescKo = desc;
                break;
            case "zh":
                place.NameZh = name;
                place.DescZh = desc;
                break;
            case "en":
                // Giữ giá trị cũ nếu bản dịch mới là null (an toàn hơn gán thẳng)
                place.NameEn = name ?? place.NameEn;
                place.DescEn = desc ?? place.DescEn;
                break;
        }
    }
}