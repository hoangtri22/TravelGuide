using System.Text.Json;
using System.Web;
using TravelGuide.Models;

namespace TravelGuide;

public class TranslationService
{
    private readonly HttpClient _http;
    private readonly DatabaseService _db;

    // MyMemory API — miễn phí, không cần key, 1000 request/ngày
    private const string ApiUrl = "https://api.mymemory.translated.net/get";

    // Mapping ngôn ngữ: AppLanguage code → MyMemory language pair
    private static readonly Dictionary<string, string> LangPair = new()
    {
        { "en", "vi|en" },
        { "ja", "vi|ja" },
        { "ko", "vi|ko" },
        { "zh", "vi|zh" },
    };

    public TranslationService(HttpClient http, DatabaseService db)
    {
        _http = http;
        _db = db;
    }

    /// <summary>Dịch 1 địa điểm sang ngôn ngữ target nếu chưa có bản dịch.</summary>
    public async Task<bool> TranslatePlaceAsync(TouristPlace place, string targetLang)
    {
        if (AlreadyTranslated(place, targetLang)) return true;

        try
        {
            var translatedName = await TranslateTextAsync(place.NameVi, targetLang);
            var translatedDesc = await TranslateTextAsync(place.DescVi, targetLang);

            if (translatedName == null || translatedDesc == null) return false;

            ApplyTranslation(place, targetLang, translatedName, translatedDesc);
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

    /// <summary>Dịch toàn bộ danh sách địa điểm sang 1 ngôn ngữ</summary>
    public async Task TranslateAllAsync(
        List<TouristPlace> places,
        string targetLang,
        IProgress<(int current, int total)>? progress = null)
    {
        for (int i = 0; i < places.Count; i++)
        {
            await TranslatePlaceAsync(places[i], targetLang);
            progress?.Report((i + 1, places.Count));

            // Delay tránh rate limit MyMemory (~1 req/giây)
            await Task.Delay(500);
        }
    }

    // ─── Private helpers ───────────────────────────────────────────────────

    private async Task<string?> TranslateTextAsync(string text, string targetLang)
    {
        if (!LangPair.TryGetValue(targetLang, out var langpair)) return text;

        try
        {
            var encoded = HttpUtility.UrlEncode(text);
            var url = $"{ApiUrl}?q={encoded}&langpair={langpair}";

            System.Diagnostics.Debug.WriteLine(
                $"[MyMemory] → [{targetLang}]: {text.Substring(0, Math.Min(30, text.Length))}...");

            var response = await _http.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[MyMemory] HTTP Error: {(int)response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // responseStatus 200 = thành công
            var status = root.GetProperty("responseStatus").GetInt32();
            if (status != 200)
            {
                System.Diagnostics.Debug.WriteLine($"[MyMemory] API Error status: {status}");
                return null;
            }

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

    private static bool AlreadyTranslated(TouristPlace place, string lang) =>
        lang switch
        {
            "ja" => !string.IsNullOrEmpty(place.NameJa),
            "ko" => !string.IsNullOrEmpty(place.NameKo),
            "zh" => !string.IsNullOrEmpty(place.NameZh),
            "en" => !string.IsNullOrEmpty(place.NameEn) && place.NameEn != place.NameVi,
            _ => true
        };

    private static void ApplyTranslation(
        TouristPlace place, string lang, string? name, string? desc)
    {
        switch (lang)
        {
            case "ja": place.NameJa = name; place.DescJa = desc; break;
            case "ko": place.NameKo = name; place.DescKo = desc; break;
            case "zh": place.NameZh = name; place.DescZh = desc; break;
            case "en":
                place.NameEn = name ?? place.NameEn;
                place.DescEn = desc ?? place.DescEn;
                break;
        }
    }
}