using System.Globalization;
using LocalizationResourceManager.Maui;

namespace TravelGuide;

public partial class MainPage : ContentPage
{
    private readonly DatabaseService _dbService;

    private string _selectedLang = "vi";

    private readonly List<string> _fullCurrencyList = new();

    private readonly Dictionary<string, string> _langNames = new()
    {
        { "vi", "🇻🇳 Tiếng Việt" },
        { "en", "🇬🇧 English" },
        { "ja", "🇯🇵 日本語" },
        { "ko", "🇰🇷 한국어" },
        { "zh", "🇨🇳 中文" },
    };

    public MainPage(DatabaseService dbService)
    {
        InitializeComponent();
        _dbService = dbService;

        LoadCurrencies();
        LoadSavedSettings();
        UpdateCustomLocalizedText();

        if (CurrencyListFrame != null) CurrencyListFrame.IsVisible = false;
    }

    private void LoadCurrencies()
    {
        try
        {
            var currencies = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                .Select(c => { try { return new RegionInfo(c.Name); } catch { return null; } })
                .Where(r => r != null)
                .Select(r => $"{r!.ISOCurrencySymbol} - {r.CurrencyEnglishName}")
                .Distinct().OrderBy(c => c).ToList();
            _fullCurrencyList.AddRange(currencies);
        }
        catch
        {
            _fullCurrencyList.AddRange(new[]
                { "VND - Vietnamese Dong", "USD - US Dollar",
                  "JPY - Japanese Yen", "KRW - South Korean Won",
                  "CNY - Chinese Yuan" });
        }
    }

    private void LoadSavedSettings()
    {
        _selectedLang = Preferences.Get("app_language", "vi");
        CurrencyEntry.Text = Preferences.Get("currency", "VND - Vietnamese Dong");
        HighlightSelectedLang(_selectedLang);
    }

    private void HighlightSelectedLang(string code)
    {
        var allCodes = new[] { "vi", "en", "ja", "ko", "zh" };
        foreach (var c in allCodes)
        {
            var border = this.FindByName<Border>($"BtnLang_{c}");
            if (border == null) continue;

            bool isSelected = c == code;
            border.BackgroundColor = isSelected
                ? Color.FromArgb("#1E88E5")
                : Color.FromArgb("#F1F5F9");

            var stack = border.Content as VerticalStackLayout;
            if (stack?.Children.Count > 1 && stack.Children[1] is Label lbl)
                lbl.TextColor = isSelected ? Colors.White : Color.FromArgb("#555555");
        }

        if (LblSelectedLang != null && _langNames.TryGetValue(code, out var name))
            LblSelectedLang.Text = name;
    }

    private void OnLanguageTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not string code) return;
        if (_selectedLang == code) return;

        _selectedLang = code;
        HighlightSelectedLang(code);

        Preferences.Set("app_language", code);
        AppLanguage.SetLanguage(code);
        SyncLocalization(code);
        UpdateCustomLocalizedText();

    }

    private void OnCurrencySearch(object sender, TextChangedEventArgs e)
    {
        var keyword = (e.NewTextValue ?? "").ToLower();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            if (CurrencyListFrame != null) CurrencyListFrame.IsVisible = false;
            return;
        }
        var result = _fullCurrencyList
            .Where(x => x.ToLower().Contains(keyword))
            .Take(20).ToList();
        if (CurrencyCollection != null) CurrencyCollection.ItemsSource = result;
        if (CurrencyListFrame != null) CurrencyListFrame.IsVisible = result.Any();
    }

    private void OnCurrencySelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is string selected)
        {
            CurrencyEntry.Text = selected;
            if (CurrencyListFrame != null) CurrencyListFrame.IsVisible = false;
            Preferences.Set("currency", selected);
        }
        if (sender is CollectionView cv) cv.SelectedItem = null;
    }

    private async void GoHome(object sender, EventArgs e)
    {
        AppLanguage.SetLanguage(_selectedLang);
        _dbService.ClearCache();
        await _dbService.GetPlacesAsync();

        if (Handler?.MauiContext == null) return;
        var homePage = Handler.MauiContext.Services.GetRequiredService<HomePage>();
        await Navigation.PushAsync(homePage);
    }

    private void SyncLocalization(string code)
    {
        try
        {
            var locMgr = Handler?.MauiContext?.Services
                .GetService<ILocalizationResourceManager>();
            if (locMgr != null)
                locMgr.CurrentCulture = new CultureInfo(code);
        }
        catch { }
    }

    private void UpdateCustomLocalizedText()
    {
        LblSyncHint.Text = AppLanguage.Current switch
        {
            "en" => "The app automatically syncs POI, audio and translations from the admin web.",
            "ja" => "アプリは管理WebからPOI・音声・翻訳データを自動同期します。",
            "ko" => "앱은 관리자 웹에서 POI, 오디오, 번역 데이터를 자동 동기화합니다.",
            "zh" => "应用会自动从管理后台同步POI、音频和翻译数据。",
            _ => "App tự đồng bộ dữ liệu POI/AUDIO/Bản dịch từ web admin."
        };
    }
}