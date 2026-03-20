using System.Globalization;
using LocalizationResourceManager.Maui;

namespace TravelGuide;

public partial class MainPage : ContentPage
{
    private List<string> _fullCurrencyList = new();
    private List<string> _fullLanguageList = new();

    // FIX: Inject HomePage qua DI thay vì GetService trong GoHome
    private readonly HomePage _homePage;

    public MainPage(HomePage homePage)
    {
        InitializeComponent();
        _homePage = homePage;

        LoadData();
        LoadSavedSettings();

        if (LanguageListFrame != null) LanguageListFrame.IsVisible = false;
        if (CurrencyListFrame != null) CurrencyListFrame.IsVisible = false;
    }

    private void LoadData()
    {
        try
        {
            _fullCurrencyList = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                .Select(c => { try { return new RegionInfo(c.Name); } catch { return null; } })
                .Where(r => r != null)
                .Select(r => $"{r!.ISOCurrencySymbol} - {r.CurrencyEnglishName}")
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            _fullLanguageList = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
                .Select(c => $"{c.EnglishName} ({c.TwoLetterISOLanguageName})")
                .Where(l => !string.IsNullOrEmpty(l))
                .Distinct()
                .OrderBy(l => l)
                .ToList();
        }
        catch
        {
            _fullCurrencyList = new() { "VND - Vietnamese Dong", "USD - US Dollar" };
            _fullLanguageList = new() { "Vietnamese (vi)", "English (en)" };
        }
    }

    private void LoadSavedSettings()
    {
        string savedLang = Preferences.Get("language", GetDeviceLanguage());
        LanguageEntry.Text = savedLang;
        CurrencyEntry.Text = Preferences.Get("currency", "VND - Vietnamese Dong");
        UpdateAppLanguage(savedLang);
    }

    private string GetDeviceLanguage()
    {
        var deviceLang = CultureInfo.CurrentCulture.EnglishName;
        return _fullLanguageList.FirstOrDefault(l => l.Contains(deviceLang)) ?? "English (en)";
    }

    private void OnLanguageSearch(object sender, TextChangedEventArgs e)
    {
        var keyword = (e.NewTextValue ?? "").ToLower();

        if (string.IsNullOrWhiteSpace(keyword))
        {
            if (LanguageListFrame != null) LanguageListFrame.IsVisible = false;
            return;
        }

        var result = _fullLanguageList
            .Where(x => x.ToLower().Contains(keyword))
            .Take(20)
            .ToList();

        if (LanguageCollection != null) LanguageCollection.ItemsSource = result;
        if (LanguageListFrame != null) LanguageListFrame.IsVisible = result.Any();
    }

    private void OnLanguageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is string selected)
        {
            LanguageEntry.Text = selected;
            if (LanguageListFrame != null) LanguageListFrame.IsVisible = false;

            Preferences.Set("language", selected);
            UpdateAppLanguage(selected);
        }
        if (sender is CollectionView cv) cv.SelectedItem = null;
    }

    /// <summary>
    /// Đồng bộ cả ILocalizationResourceManager lẫn AppLanguage.
    /// FIX: AppLanguage.SetLanguage() chưa được gọi trong bản cũ.
    /// </summary>
    private void UpdateAppLanguage(string selectedLanguage)
    {
        try
        {
            int startIndex = selectedLanguage.LastIndexOf('(') + 1;
            if (startIndex <= 0 || startIndex + 2 > selectedLanguage.Length) return;

            string langCode = selectedLanguage.Substring(startIndex, 2).ToLower();

            // FIX: Sync AppLanguage để TouristPlace.Name/Description trả đúng ngôn ngữ
            // Map full locale code → app language code (chỉ 5 ngôn ngữ app hỗ trợ)
            var supportedCode = langCode switch
            {
                "vi" => "vi",
                "en" => "en",
                "ja" => "ja",
                "ko" => "ko",
                "zh" => "zh",
                _ => "en" // fallback về English nếu locale không hỗ trợ
            };
            AppLanguage.SetLanguage(supportedCode);

            // Sync LocalizationResourceManager cho .resx translations
            var localizationManager = Handler?.MauiContext?.Services
                .GetService<ILocalizationResourceManager>();
            if (localizationManager != null)
                localizationManager.CurrentCulture = new CultureInfo(langCode);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] UpdateAppLanguage error: {ex.Message}");
        }
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
            .Take(20)
            .ToList();

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

    // FIX: Dùng _homePage đã inject — không cần GetService tại runtime
    private async void GoHome(object sender, EventArgs e)
    {
        await Navigation.PushAsync(_homePage);
    }
}