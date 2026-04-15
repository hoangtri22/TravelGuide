using System.Globalization;
using LocalizationResourceManager.Maui;

namespace TravelGuide;

/// <summary>
/// Màn hình cài đặt ban đầu: chọn ngôn ngữ, tiền tệ, đồng bộ culture với ResX và chuyển tới <see cref="HomePage"/>.
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly DatabaseService _dbService;
    private readonly TouristAuthService _touristAuthService;
    private bool _isTouristLoggedIn;

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

    /// <summary>Khởi tạo UI, nạp danh sách tiền tệ và cài đặt đã lưu.</summary>
    public MainPage(DatabaseService dbService, TouristAuthService touristAuthService)
    {
        InitializeComponent();
        _dbService = dbService;
        _touristAuthService = touristAuthService;

        LoadCurrencies();
        LoadSavedSettings();

        if (CurrencyListFrame != null) CurrencyListFrame.IsVisible = false;
        _ = RefreshTouristAuthStatusAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshTouristAuthStatusAsync();
    }

    /// <summary>Lấy danh sách mã tiền tệ từ <see cref="CultureInfo"/> (fallback cố định nếu lỗi).</summary>
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

    /// <summary>Đọc ngôn ngữ và chuỗi tiền tệ từ Preferences; cập nhật highlight nút ngôn ngữ.</summary>
    private void LoadSavedSettings()
    {
        _selectedLang = Preferences.Get("app_language", "vi");
        CurrencyEntry.Text = Preferences.Get("currency", "VND - Vietnamese Dong");
        HighlightSelectedLang(_selectedLang);
    }

    /// <summary>Đổi màu border/label các nút ngôn ngữ theo mã đang chọn.</summary>
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

    /// <summary>Tap cờ ngôn ngữ: lưu Preferences, gọi <see cref="AppLanguage.SetLanguage"/>, sync ResX.</summary>
    private void OnLanguageTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is not string code) return;
        if (_selectedLang == code) return;

        _selectedLang = code;
        HighlightSelectedLang(code);

        Preferences.Set("app_language", code);
        AppLanguage.SetLanguage(code);
        SyncLocalization(code);

    }

    /// <summary>Lọc danh sách tiền tệ theo từ khóa (tối đa 20 dòng).</summary>
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

    /// <summary>Chọn một dòng tiền tệ từ gợi ý và lưu Preferences.</summary>
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

    /// <summary>Áp ngôn ngữ, xóa cache POI, điều hướng tới dashboard <see cref="HomePage"/>.</summary>
    private async void GoHome(object sender, EventArgs e)
    {
        if (!_isTouristLoggedIn)
        {
            await DisplayAlert("Thông báo", "Vui lòng đăng nhập du khách trước khi tiếp tục.", "OK");
            return;
        }

        AppLanguage.SetLanguage(_selectedLang);
        _dbService.ClearCache();
        await _dbService.GetPlacesAsync();

        if (Handler?.MauiContext == null) return;
        var homePage = Handler.MauiContext.Services.GetRequiredService<HomePage>();
        await Navigation.PushAsync(homePage);
    }

    /// <summary>Cập nhật <see cref="ILocalizationResourceManager.CurrentCulture"/> theo mã ngôn ngữ.</summary>
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

    private async Task RefreshTouristAuthStatusAsync()
    {
        var me = await _touristAuthService.GetMeAsync();
        if (LblTouristAuthStatus == null) return;
        _isTouristLoggedIn = me.Ok;
        LblTouristAuthStatus.Text = me.Ok
            ? $"Đã đăng nhập: {me.Username} ({me.AccountTier})"
            : "Chưa đăng nhập";
        if (ContinueBtn != null)
            ContinueBtn.IsEnabled = _isTouristLoggedIn;
    }

    private async void OnTouristLoginClicked(object sender, EventArgs e)
    {
        if (Handler?.MauiContext == null) return;
        var page = Handler.MauiContext.Services.GetRequiredService<TouristLoginPage>();
        await Navigation.PushAsync(page);
        await RefreshTouristAuthStatusAsync();
        if (_isTouristLoggedIn)
            GoHome(this, EventArgs.Empty);
    }

    private async void OnTouristLogoutClicked(object sender, EventArgs e)
    {
        _touristAuthService.Logout();
        await RefreshTouristAuthStatusAsync();
    }
}