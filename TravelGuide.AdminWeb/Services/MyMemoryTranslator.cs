using System.Text.Json;

namespace TravelGuide.AdminWeb.Services;

/// <summary>Gọi API MyMemory (cặp vi→target) để bổ sung field dịch thiếu trên server.</summary>
public static class MyMemoryTranslator
{
    /// <summary>Dịch một chuỗi; trả về chuỗi rỗng nếu lỗi hoặc responseStatus ≠ 200.</summary>
    public static async Task<string> TranslateAsync(HttpClient httpClient, string sourceText, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return sourceText;
        var pair = $"vi|{targetLang}";
        var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(sourceText)}&langpair={pair}";
        try
        {
            var json = await httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("responseStatus").GetInt32() != 200) return "";
            return root.GetProperty("responseData").GetProperty("translatedText").GetString() ?? "";
        }
        catch
        {
            return "";
        }
    }
}
