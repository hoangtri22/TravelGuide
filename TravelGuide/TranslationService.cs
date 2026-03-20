using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TravelGuide.Models;

namespace TravelGuide;

public class TranslationService
{
    private readonly HttpClient _http;
    private readonly DatabaseService _db;

    // ⚠️ Thay bằng API key thật của bạn
    private const string ApiKey = "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";
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string Model = "claude-haiku-4-5-20251001"; // Haiku: nhanh + rẻ cho task dịch

    public TranslationService(HttpClient http, DatabaseService db)
    {
        _http = http;
        _db = db;
    }

    /// <summary>
    /// Dịch 1 địa điểm sang ngôn ngữ target nếu chưa có bản dịch.
    /// Trả về true nếu dịch thành công.
    /// </summary>
    public async Task<bool> TranslatePlaceAsync(TouristPlace place, string targetLang)
    {
        // Kiểm tra đã có bản dịch chưa
        if (AlreadyTranslated(place, targetLang)) return true;

        try
        {
            var langName = GetLanguageFullName(targetLang);

            // Gọi Claude dịch cả Name lẫn Description trong 1 request
            // FIX CS9006: Tách JSON template ra biến riêng để tránh {{ }} conflict với string interpolation
            const string jsonTemplate = "{\n  \"name\": \"...\",\n  \"description\": \"...\"\n}";
            var prompt =
                $"Dịch các đoạn văn bản sau sang {langName}.\n" +
                $"Chỉ trả về JSON thuần, không giải thích, không markdown.\n" +
                $"Format JSON:\n{jsonTemplate}\n\n" +
                $"Văn bản cần dịch:\n" +
                $"- name: {place.NameVi}\n" +
                $"- description: {place.DescVi}";

            var result = await CallClaudeAsync(prompt);
            if (result == null) return false;

            // Parse JSON từ Claude
            var json = JsonSerializer.Deserialize<TranslationResult>(result,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (json == null) return false;

            // Gán vào đúng field theo ngôn ngữ
            ApplyTranslation(place, targetLang, json.Name, json.Description);

            // Lưu lại vào SQLite
            await _db.UpdatePlaceAsync(place);

            System.Diagnostics.Debug.WriteLine(
                $"✅ Dịch xong [{targetLang}]: {place.NameVi} → {json.Name}");
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

            // Delay nhỏ để tránh rate limit
            await Task.Delay(300);
        }
    }

    // ─── Private helpers ───────────────────────────────────────────────────

    private async Task<string?> CallClaudeAsync(string prompt)
    {
        var requestBody = new
        {
            model = Model,
            max_tokens = 300,
            messages = new[]
            {
                new { role = "user", content = prompt }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Add("x-api-key", ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine($"Claude API error: {err}");
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);

        // Lấy text từ response Claude
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();

        // Làm sạch markdown nếu Claude tự ý thêm ```json
        return text?
            .Replace("```json", "")
            .Replace("```", "")
            .Trim();
    }

    private static bool AlreadyTranslated(TouristPlace place, string lang) =>
        lang switch
        {
            "ja" => !string.IsNullOrEmpty(place.NameJa),
            "ko" => !string.IsNullOrEmpty(place.NameKo),
            "zh" => !string.IsNullOrEmpty(place.NameZh),
            "en" => !string.IsNullOrEmpty(place.NameEn),
            _ => true // "vi" luôn có sẵn
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
                place.DescEn = desc ?? place.DescEn; break;
        }
    }

    private static string GetLanguageFullName(string code) =>
        code switch
        {
            "ja" => "tiếng Nhật",
            "ko" => "tiếng Hàn",
            "zh" => "tiếng Trung (Giản thể)",
            "en" => "tiếng Anh",
            _ => "tiếng Anh"
        };

    private class TranslationResult
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
    }
}