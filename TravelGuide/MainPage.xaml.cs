using System.Globalization;
using LocalizationResourceManager.Maui;

namespace TravelGuide;

public partial class MainPage : ContentPage
{
    private readonly DatabaseService _dbService;
    private readonly TranslationService _translationService;

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

    public MainPage(DatabaseService dbService, TranslationService translationService)
    {
        InitializeComponent();
        _dbService = dbService;
        _translationService = translationService;

        LoadCurrencies();
        LoadSavedSettings();

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

        if (code is "en" or "ja" or "ko" or "zh")
            _ = TranslateIfNeededAsync(code);
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

        // Dịch theo ngôn ngữ đang chọn (bao gồm cả EN)
        if (_selectedLang is "en" or "ja" or "ko" or "zh")
        {
            bool done = await _dbService.IsTranslatedAsync(_selectedLang);
            if (!done)
                await ShowTranslatingAsync(_selectedLang);
        }

        if (Handler?.MauiContext == null) return;
        var homePage = Handler.MauiContext.Services.GetRequiredService<HomePage>();
        await Navigation.PushAsync(homePage);
    }

    private async Task ShowTranslatingAsync(string lang)
    {
        try
        {
            if (TranslatingBar != null) TranslatingBar.IsVisible = true;
            if (ContinueBtn != null)
            {
                ContinueBtn.IsEnabled = false;
                ContinueBtn.Opacity = 0.6;
            }

            var places = await _dbService.GetPlacesAsync();
            var progress = new Progress<(int current, int total)>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (LblTranslating != null)
                        LblTranslating.Text = $"{p.current}/{p.total}...";
                });
            });

            await _translationService.TranslateAllAsync(places, lang, progress);
        }
        finally
        {
            if (TranslatingBar != null) TranslatingBar.IsVisible = false;
            if (ContinueBtn != null)
            {
                ContinueBtn.IsEnabled = true;
                ContinueBtn.Opacity = 1;
            }
        }
    }

    private async Task TranslateIfNeededAsync(string lang)
    {
        try
        {
            bool done = await _dbService.IsTranslatedAsync(lang);
            if (done) return;
            var places = await _dbService.GetPlacesAsync();
            await _translationService.TranslateAllAsync(places, lang);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MainPage] TranslateIfNeeded error: {ex.Message}");
        }
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
}