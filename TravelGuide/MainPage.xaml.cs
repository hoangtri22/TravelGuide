using System.Globalization;

namespace TravelGuide;

public partial class MainPage : ContentPage
{
    List<string> fullCurrencyList = new();
    List<string> fullLanguageList = new();

    public MainPage()
    {
        InitializeComponent();
        LoadData();
        LoadSavedSettings();

        if (LanguageListFrame != null) LanguageListFrame.IsVisible = false;
        if (CurrencyListFrame != null) CurrencyListFrame.IsVisible = false;
    }

    void LoadData()
    {
        try
        {
            fullCurrencyList = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                .Select(c => { try { return new RegionInfo(c.Name); } catch { return null; } })
                .Where(r => r != null)
                .Select(r => $"{r?.ISOCurrencySymbol} - {r?.CurrencyEnglishName}")
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            fullLanguageList = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
                .Select(c => $"{c.EnglishName} ({c.TwoLetterISOLanguageName})")
                .Where(l => !string.IsNullOrEmpty(l))
                .Distinct()
                .OrderBy(l => l)
                .ToList();
        }
        catch
        {
            fullCurrencyList = new() { "VND - Vietnamese Dong", "USD - US Dollar" };
            fullLanguageList = new() { "Vietnamese (vi)", "English (en)" };
        }
    }

    void LoadSavedSettings()
    {
        LanguageEntry.Text = Preferences.Get("language", GetDeviceLanguage());
        CurrencyEntry.Text = Preferences.Get("currency", "VND - Vietnamese Dong");
    }

    string GetDeviceLanguage()
    {
        var deviceLang = CultureInfo.CurrentCulture.EnglishName;
        return fullLanguageList.FirstOrDefault(l => l.Contains(deviceLang)) ?? "English (en)";
    }

    void OnLanguageSearch(object sender, TextChangedEventArgs e)
    {
        var keyword = (e.NewTextValue ?? "").ToLower();

        if (string.IsNullOrWhiteSpace(keyword))
        {
            if (LanguageListFrame != null) LanguageListFrame.IsVisible = false;
            return;
        }

        var result = fullLanguageList
            .Where(x => x.ToLower().Contains(keyword))
            .Take(20)
            .ToList();

        if (LanguageCollection != null) LanguageCollection.ItemsSource = result;
        if (LanguageListFrame != null) LanguageListFrame.IsVisible = result.Any();
    }

    void OnLanguageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is string selected)
        {
            LanguageEntry.Text = selected;
            if (LanguageListFrame != null) LanguageListFrame.IsVisible = false;
            Preferences.Set("language", selected);
        }
        if (sender is CollectionView cv) cv.SelectedItem = null;
    }

    void OnCurrencySearch(object sender, TextChangedEventArgs e)
    {
        var keyword = (e.NewTextValue ?? "").ToLower();

        if (string.IsNullOrWhiteSpace(keyword))
        {
            if (CurrencyListFrame != null) CurrencyListFrame.IsVisible = false;
            return;
        }

        var result = fullCurrencyList
            .Where(x => x.ToLower().Contains(keyword))
            .Take(20)
            .ToList();

        if (CurrencyCollection != null) CurrencyCollection.ItemsSource = result;
        if (CurrencyListFrame != null) CurrencyListFrame.IsVisible = result.Any();
    }

    void OnCurrencySelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is string selected)
        {
            CurrencyEntry.Text = selected;
            if (CurrencyListFrame != null) CurrencyListFrame.IsVisible = false;
            Preferences.Set("currency", selected);
        }
        if (sender is CollectionView cv) cv.SelectedItem = null;
    }

    async void GoHome(object sender, EventArgs e)
    {
        // Lấy HomePage từ hệ thống Service đã được tiêm sẵn DatabaseService
        var homePage = Handler.MauiContext.Services.GetService<HomePage>();

        if (homePage != null)
        {
            await Navigation.PushAsync(homePage);
        }
    }
}