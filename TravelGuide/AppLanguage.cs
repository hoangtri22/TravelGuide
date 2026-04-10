using System.Globalization;

namespace TravelGuide;

/// <summary>
/// Quản lý ngôn ngữ toàn app
/// Cung cấp cơ chế đổi ngôn ngữ + notify UI + lưu Preferences
/// </summary>
public static class AppLanguage
{
    // Ngôn ngữ mặc định
    private static string _current = "vi";

    /// <summary>
    /// Ngôn ngữ hiện tại: vi, en, ja, ko, zh
    /// </summary>
    public static string Current => _current;

    /// <summary>
    /// Event được gọi khi ngôn ngữ thay đổi
    /// UI có thể subscribe để refresh
    /// </summary>
    public static event Action<string>? OnLanguageChanged;

    /// <summary>
    /// Danh sách ngôn ngữ hỗ trợ
    /// </summary>
    public static readonly Dictionary<string, string> SupportedLanguages = new()
    {
        { "vi", "Tiếng Việt" },
        { "en", "English" },
        { "ja", "日本語" },
        { "ko", "한국어" },
        { "zh", "中文" },
    };

    /// <summary>Đặt ngôn ngữ UI, lưu Preferences, áp culture thread và bắn <see cref="OnLanguageChanged"/>.</summary>
    public static void SetLanguage(string langCode)
    {
        // Validate ngôn ngữ
        if (!SupportedLanguages.ContainsKey(langCode))
            return;

        // Không đổi nếu giống nhau
        if (_current == langCode)
            return;

        _current = langCode;

        // Lưu lại để dùng cho lần mở app sau
        Preferences.Set("app_language", langCode);

        // Áp dụng culture cho resx
        ApplyCulture(langCode);

        // Notify UI
        OnLanguageChanged?.Invoke(langCode);
    }

    /// <summary>Đọc mã ngôn ngữ đã lưu khi mở app; fallback <c>vi</c> nếu không hợp lệ.</summary>
    public static void LoadSaved()
    {
        var saved = Preferences.Get("app_language", "vi");

        if (!SupportedLanguages.ContainsKey(saved))
            saved = "vi";

        _current = saved;

        ApplyCulture(saved);
        OnLanguageChanged?.Invoke(saved);
    }

    /// <summary>Gán <see cref="CultureInfo.DefaultThreadCurrentCulture"/> / UICulture cho thread hiện tại.</summary>
    private static void ApplyCulture(string langCode)
    {
        try
        {
            var culture = new CultureInfo(langCode);

            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch
        {
            // fallback nếu culture không hợp lệ
            var culture = new CultureInfo("vi");

            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
    }

    /// <summary>Trả về nhãn hiển thị (vd. <c>en</c> → <c>English</c>) từ <see cref="SupportedLanguages"/>.</summary>
    public static string GetLanguageName(string code)
    {
        return SupportedLanguages.TryGetValue(code, out var name)
            ? name
            : code;
    }
}