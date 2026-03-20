using System.Globalization;
using LocalizationResourceManager.Maui;

namespace TravelGuide;

public partial class MainPage : ContentPage
{
    private readonly DatabaseService _dbService;
    private readonly TranslationService _translationService;

    // ✅ Chỉ 5 ngôn ngữ cố định — không lấy từ hệ thống
    private readonly List<string> _supportedLanguages = new()
    {
        "Tiếng Việt (vi)",
        "English (en)",
        "日本語 (ja)",
        "한국어 (ko)",
        "中文 (zh)"
    };

    private readonly List<string> _fullCurrencyList = new();

    public MainPage(DatabaseService dbService, TranslationService translationService)
    {
        InitializeComponent();
        _dbService = dbService;
        _translationService = translationService;

        LoadCurrencies();
        LoadSavedSettings();

        if (LanguageListFrame != null) LanguageListFrame.IsVisible = false;
        if (CurrencyListFrame != null) CurrencyListFrame.IsVisible = false;
    }

    // ── Load currencies từ hệ thống ──────────────────────────────────────
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

    // ── Load settings đã lưu ─────────────────────────────────────────────
    private void LoadSavedSettings()
    {
        // Lấy ngôn ngữ đã lưu → tìm trong danh sách 5 ngôn ngữ
        var savedCode = Preferences.Get("app_language", "vi");
        var savedLang = _supportedLanguages
            .FirstOrDefault(l => l.Contains($"({savedCode})"))
            ?? "Tiếng Việt (vi)";

        LanguageEntry.Text = savedLang;
        CurrencyEntry.Text = Preferences.Get("currency", "VND - Vietnamese Dong");
    }

    // ── Search ngôn ngữ — chỉ trong 5 ngôn ngữ ──────────────────────────
    private void OnLanguageSearch(object sender, TextChangedEventArgs e)
    {
        var keyword = (e.NewTextValue ?? "").ToLower();

        if (string.IsNullOrWhiteSpace(keyword))
        {
            // Hiện tất cả 5 ngôn ngữ khi ô trống
            if (LanguageCollection != null)
                LanguageCollection.ItemsSource = _supportedLanguages;
            if (LanguageListFrame != null)
                LanguageListFrame.IsVisible = true;
            return;
        }

        var result = _supportedLanguages
            .Where(l => l.ToLower().Contains(keyword))
            .ToList();

        if (LanguageCollection != null)
            LanguageCollection.ItemsSource = result;
        if (LanguageListFrame != null)
            LanguageListFrame.IsVisible = result.Any();
    }

    // ── Chọn ngôn ngữ → set + dịch ───────────────────────────────────────
    private void OnLanguageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not string selected) return;

        LanguageEntry.Text = selected;
        if (LanguageListFrame != null) LanguageListFrame.IsVisible = false;

        // Extract code từ "Tiếng Việt (vi)" → "vi"
        var code = ExtractLangCode(selected);
        Preferences.Set("app_language", code);

        AppLanguage.SetLanguage(code);

        // ✅ Trigger dịch nếu cần
        if (code is "en" or "ja" or "ko" or "zh")
            _ = TranslateIfNeededAsync(code);

        // Sync LocalizationResourceManager
        SyncLocalization(code);

        if (sender is CollectionView cv) cv.SelectedItem = null;
    }

    // ── Search currency ───────────────────────────────────────────────────
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

    // ── Chọn currency ────────────────────────────────────────────────────
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

    // ── Nút Continue ─────────────────────────────────────────────────────
    private async void GoHome(object sender, EventArgs e)
    {
        // Đảm bảo ngôn ngữ đang chọn đã được set
        var code = ExtractLangCode(LanguageEntry.Text ?? "vi");
        AppLanguage.SetLanguage(code);

        // Nếu cần dịch mà chưa dịch → dịch trước khi vào HomePage
        if (code is "en" or "ja" or "ko" or "zh")
        {
            bool done = await _dbService.IsTranslatedAsync(code);
            if (!done)
            {
                // Hiện loading
                await ShowTranslatingAsync(code);
            }
        }

        if (Handler?.MauiContext == null) return;
        var homePage = Handler.MauiContext.Services
            .GetRequiredService<HomePage>();
        await Navigation.PushAsync(homePage);
    }

    // ── Dịch với loading indicator ────────────────────────────────────────
    private async Task ShowTranslatingAsync(string lang)
    {
        // Đổi text nút thành loading
        var continueBtn = this.FindByName<Button>("ContinueBtn");

        try
        {
            if (continueBtn != null)
            {
                continueBtn.Text = "⏳ Đang dịch...";
                continueBtn.IsEnabled = false;
                continueBtn.BackgroundColor = Color.FromArgb("#9CA3AF");
            }

            var places = await _dbService.GetPlacesAsync();
            var progress = new Progress<(int current, int total)>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (continueBtn != null)
                        continueBtn.Text = $"⏳ Đang dịch {p.current}/{p.total}...";
                });
            });

            await _translationService.TranslateAllAsync(places, lang, progress);
        }
        finally
        {
            if (continueBtn != null)
            {
                continueBtn.Text = "Continue / Tiếp tục";
                continueBtn.IsEnabled = true;
                continueBtn.BackgroundColor = Color.FromArgb("#1E88E5");
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
            System.Diagnostics.Debug.WriteLine($"[MainPage] Dịch xong [{lang}]");
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

    // ── Helper: extract "vi" từ "Tiếng Việt (vi)" ────────────────────────
    private static string ExtractLangCode(string displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return "vi";
        int start = displayName.LastIndexOf('(') + 1;
        int end = displayName.LastIndexOf(')');
        if (start > 0 && end > start)
            return displayName.Substring(start, end - start).Trim().ToLower();
        return "vi";
    }
}