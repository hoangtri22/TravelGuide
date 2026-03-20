namespace TravelGuide;

/// <summary>
/// Quản lý ngôn ngữ hiện tại toàn app.
/// Các page subscribe event OnLanguageChanged để tự refresh UI.
/// </summary>
public static class AppLanguage
{
    private static string _current = "vi";

    /// <summary>Ngôn ngữ hiện tại: "vi" | "en" | "ja" | "ko" | "zh"</summary>
    public static string Current => _current;

    /// <summary>Fired khi user đổi ngôn ngữ</summary>
    public static event Action<string>? OnLanguageChanged;

    public static readonly Dictionary<string, string> SupportedLanguages = new()
    {
        { "vi", "🇻🇳 Tiếng Việt" },
        { "en", "🇬🇧 English" },
        { "ja", "🇯🇵 日本語" },
        { "ko", "🇰🇷 한국어" },
        { "zh", "🇨🇳 中文" },
    };

    public static void SetLanguage(string langCode)
    {
        if (_current == langCode) return;
        _current = langCode;

        // Lưu Preferences để nhớ giữa các lần mở app
        Preferences.Set("app_language", langCode);
        OnLanguageChanged?.Invoke(langCode);
    }

    /// <summary>Load ngôn ngữ đã lưu từ lần trước</summary>
    public static void LoadSaved()
    {
        _current = Preferences.Get("app_language", "vi");
    }

    public static string GetLanguageName(string code) =>
        SupportedLanguages.TryGetValue(code, out var name) ? name : code;
}