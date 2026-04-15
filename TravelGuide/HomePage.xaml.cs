using LocalizationResourceManager.Maui;
using TravelGuide.Models;
using System.Globalization;
using Microsoft.Maui.Dispatching;

namespace TravelGuide;

/// <summary>
/// Trang chủ: danh sách địa điểm, tìm kiếm, lối tới bản đồ / âm thanh / gần đây.
/// </summary>
public partial class HomePage : ContentPage
{
    private const int AuthUiRefreshCooldownSeconds = 10;
    private readonly DatabaseService _dbService;
    private readonly NarrationEngine _narrationEngine;
    private readonly TouristAuthService _touristAuthService;
    private List<TouristPlace> _allPlaces = new();
    private string _selectedCategory = "all";
    private string _currentKeyword = "";
    private DateTime _lastAuthUiRefreshUtc = DateTime.MinValue;
    private IDispatcherTimer? _authUiSyncTimer;
    private bool _isAuthUiSyncRunning;
    private readonly List<LangOption> _languageOptions =
    [
        new("vi", "🇻🇳 Tiếng Việt"),
        new("en", "🇬🇧 English"),
        new("ja", "🇯🇵 日本語"),
        new("ko", "🇰🇷 한국어"),
        new("zh", "🇨🇳 中文")
    ];

    /// <summary>Đăng ký lắng nghe đổi ngôn ngữ để reload danh sách và chrome.</summary>
    public HomePage(DatabaseService dbService, NarrationEngine narrationEngine, TouristAuthService touristAuthService)
    {
        InitializeComponent();
        _dbService = dbService;
        _narrationEngine = narrationEngine;
        _touristAuthService = touristAuthService;
        UpdateLanguageButton(AppLanguage.Current);
        UpdateCategoryChipTexts();

        AppLanguage.OnLanguageChanged += _ =>
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                UpdateLanguageButton(AppLanguage.Current);
                UpdateCategoryChipTexts();
                UpdateLocalizedChrome();
                await LoadPlacesAsync();
            });

        UpdateCategoryChipState();
    }

    /// <summary>Gắn mini player và tải lại danh sách POI.</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        EnsureAuthUiSyncTimer();
        _authUiSyncTimer?.Start();
        MiniPlayer.Attach(_narrationEngine);
        SyncLocalization(AppLanguage.Current);
        UpdateLanguageButton(AppLanguage.Current);
        UpdateLocalizedChrome();
        await RefreshTouristAuthUiAsync(force: true);
        _dbService.ClearCache();
        await LoadPlacesAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _authUiSyncTimer?.Stop();
    }

    private async void OnLanguageButtonClicked(object sender, EventArgs e)
    {
        var selectedLabel = await DisplayActionSheet(
            AppResources.LanguageLabel,
            "Cancel",
            null,
            _languageOptions.Select(x => x.Label).ToArray());
        if (string.IsNullOrWhiteSpace(selectedLabel) || selectedLabel == "Cancel")
            return;
        var option = _languageOptions.FirstOrDefault(x => x.Label == selectedLabel);
        if (option is null) return;
        var selectedCode = option.Code;
        if (selectedCode == AppLanguage.Current) return;

        Preferences.Set("app_language", selectedCode);
        AppLanguage.SetLanguage(selectedCode);
        SyncLocalization(selectedCode);
        _dbService.ClearCache();
        await LoadPlacesAsync();
    }

    private void UpdateLanguageButton(string? code)
    {
        var selected = _languageOptions.FirstOrDefault(x => x.Code == (code ?? "vi")) ?? _languageOptions[0];
        LanguageButton.Text = selected.Label;
    }

    /// <summary>Nạp POI từ <see cref="DatabaseService"/> và gán <c>ItemsSource</c>.</summary>
    private async Task LoadPlacesAsync()
    {
        _allPlaces = await _dbService.GetPlacesAsync();
        ApplyFilters();
    }

    /// <summary>Cập nhật nhãn số địa điểm khi đổi ngôn ngữ (tiêu đề hero dùng ResX trong XAML).</summary>
    private void UpdateLocalizedChrome()
    {
        UpdateCategoryChipTexts();
        UpdateNearbySectionTexts();
        ApplyFilters();
    }

    private void SyncLocalization(string code)
    {
        try
        {
            var locMgr = Handler?.MauiContext?.Services.GetService<ILocalizationResourceManager>();
            if (locMgr != null)
                locMgr.CurrentCulture = new CultureInfo(code);
        }
        catch
        {
            // ignore localization sync errors
        }
    }

    /// <summary>Chuỗi ngắn cho badge (không emoji — hiển thị gọn cạnh tagline).</summary>
    private static string FormatPlacesCount(int n) => AppLanguage.Current switch
    {
        "en" => n == 1 ? $"{n} place" : $"{n} places",
        "ja" => $"{n}か所",
        "ko" => $"{n}곳",
        "zh" => $"{n} 个景点",
        _ => $"{n} địa điểm"
    };

    private async Task RefreshPremiumUiAsync()
    {
        if (PremiumCard == null || BtnUpgradePremiumHome == null) return;

        var me = await _touristAuthService.GetMeAsync();
        if (!me.Ok)
        {
            PremiumCard.IsVisible = false;
            return;
        }

        var isPremium = string.Equals(me.AccountTier, "premium", StringComparison.OrdinalIgnoreCase);
        PremiumCard.IsVisible = !isPremium;
        BtnUpgradePremiumHome.IsVisible = !isPremium;
    }

    private async Task RefreshTouristAuthUiAsync(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && (now - _lastAuthUiRefreshUtc).TotalSeconds < AuthUiRefreshCooldownSeconds)
            return;
        if (_isAuthUiSyncRunning) return;

        _isAuthUiSyncRunning = true;
        try
        {
            _lastAuthUiRefreshUtc = now;
            await RefreshPremiumUiAsync();
        }
        finally
        {
            _isAuthUiSyncRunning = false;
        }
    }

    /// <summary>Lọc danh sách theo từ khóa (tên/mô tả đa ngôn ngữ).</summary>
    private void OnSearchBarTextChanged(object sender, TextChangedEventArgs e)
    {
        _currentKeyword = (e.NewTextValue ?? "").Trim().ToLowerInvariant();
        ApplyFilters();
    }

    private void OnCategoryChipClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        var category = (btn.CommandParameter as string)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(category)) return;
        _selectedCategory = category;
        UpdateCategoryChipState();
        ApplyFilters();
    }

    /// <summary>Mở <see cref="PlaceDetailPage"/> với POI được chọn.</summary>
    private async void OnPlaceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not TouristPlace place) return;
        try
        {
            if (Handler?.MauiContext == null) return;
            var detailPage = Handler.MauiContext.Services
                .GetRequiredService<PlaceDetailPage>();
            detailPage.LoadPlace(place);
            await Navigation.PushAsync(detailPage);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Navigation error", $"Cannot open place detail: {ex.Message}", "OK");
        }
        finally
        {
            if (sender is CollectionView cv) cv.SelectedItem = null;
        }
    }

    private async void OnBottomNavHome(object? sender, EventArgs e)
    {
        await RefreshTouristAuthUiAsync(force: true);
        await MainScroll.ScrollToAsync(0, 0, true);
    }

    private void EnsureAuthUiSyncTimer()
    {
        if (_authUiSyncTimer is not null) return;
        _authUiSyncTimer = Dispatcher.CreateTimer();
        _authUiSyncTimer.Interval = TimeSpan.FromSeconds(5);
        _authUiSyncTimer.Tick += async (_, _) => await RefreshTouristAuthUiAsync(force: true);
    }

    private void OnBottomNavSaved(object? sender, EventArgs e)
    {
        // Chưa có màn Đã lưu — giữ tab để đồng bộ UI / đa ngôn ngữ
    }

    private async void OpenQrScanner(object? sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync(nameof(QrScannerPage));
        }
        catch (Exception ex)
        {
            await DisplayAlert("QR error", $"Cannot open QR scanner: {ex.Message}", "OK");
        }
    }

    private async void OpenQrScanHistory(object? sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync(nameof(QrScanHistoryPage));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Navigation error", $"Cannot open scan history: {ex.Message}", "OK");
        }
    }

    private async void OnUpgradePremiumClicked(object sender, EventArgs e)
    {
        var fee = TouristPricing.PremiumActivationVnd;
        var goScan = await DisplayAlert(
            "Nâng cấp Premium",
            $"Phí kích hoạt (mô phỏng): {fee:N0} VND.\n\nLuồng hiện tại kích hoạt Premium bằng cách quét mã QR claim.",
            "Mở quét QR",
            "Đóng");
        if (!goScan) return;

        await Shell.Current.GoToAsync(nameof(QrScannerPage));
    }

    private void ApplyFilters()
    {
        IEnumerable<TouristPlace> filtered = _allPlaces;

        if (_selectedCategory != "all")
        {
            filtered = filtered.Where(p =>
                string.Equals(NormalizeTagKey((p.Tag ?? "").Trim()), _selectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_currentKeyword))
        {
            var keyword = _currentKeyword;
            filtered = filtered.Where(p =>
                p.NameVi.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                p.NameEn.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                p.DescVi.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (p.NameJa ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (p.NameKo ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (p.NameZh ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();
        LblTotalPoi.Text = FormatPlacesCount(list.Count);

        PlacesCollection.ItemsSource = null;
        PlacesCollection.ItemsSource = list;

        NearbyCollection.ItemsSource = null;
        NearbyCollection.ItemsSource = list.Take(4).ToList();
    }

    private void UpdateCategoryChipState()
    {
        var chips = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase)
        {
            ["all"] = ChipAll,
            ["quán ăn"] = ChipFood,
            ["quán nước"] = ChipDrink,
            ["di tích lịch sử"] = ChipSight,
            ["địa điểm du lịch"] = ChipOther
        };

        foreach (var kv in chips)
        {
            var isActive = string.Equals(kv.Key, _selectedCategory, StringComparison.OrdinalIgnoreCase);
            kv.Value.BackgroundColor = isActive ? Color.FromArgb("#1A7FD4") : Colors.White;
            kv.Value.TextColor = isActive ? Colors.White : Color.FromArgb("#445566");
        }
    }

    /// <summary>Điều hướng tới <see cref="MapPage"/>.</summary>
    private async void OpenMap(object sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync(nameof(MapPage));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Map error", $"Cannot open map page: {ex.Message}", "OK");
        }
    }

    /// <summary>Điều hướng tới <see cref="AudioPage"/>.</summary>
    private async void OpenAudio(object sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync(nameof(AudioPage));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Navigation error", $"Cannot open audio page: {ex.Message}", "OK");
        }
    }

    /// <summary>Lấy vị trí hiện tại và hiển thị POI trong bán kính 1km (hoặc toàn bộ nếu rỗng).</summary>
    private async void OpenNearby(object sender, EventArgs e)
    {
        try
        {
            var location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium,
                    TimeSpan.FromSeconds(5)));
            if (location == null) return;
            var nearby = await _dbService.GetNearbyPlacesAsync(
                location.Latitude, location.Longitude, radiusMeters: 1000);
            PlacesCollection.ItemsSource = null;
            PlacesCollection.ItemsSource = nearby.Any() ? nearby : _allPlaces;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HomeNearby] {ex.Message}");
        }
    }

    private sealed record LangOption(string Code, string Label);

    private void UpdateCategoryChipTexts()
    {
        switch (AppLanguage.Current)
        {
            case "en":
                ChipAll.Text = "🔥 Featured";
                ChipFood.Text = "🍜 Food";
                ChipDrink.Text = "☕ Drinks";
                ChipSight.Text = "🏛️ Heritage";
                ChipOther.Text = "📍 Others";
                break;
            case "ja":
                ChipAll.Text = "🔥 注目";
                ChipFood.Text = "🍜 グルメ";
                ChipDrink.Text = "☕ ドリンク";
                ChipSight.Text = "🏛️ 史跡";
                ChipOther.Text = "📍 その他";
                break;
            case "ko":
                ChipAll.Text = "🔥 추천";
                ChipFood.Text = "🍜 맛집";
                ChipDrink.Text = "☕ 음료";
                ChipSight.Text = "🏛️ 유적";
                ChipOther.Text = "📍 기타";
                break;
            case "zh":
                ChipAll.Text = "🔥 热门";
                ChipFood.Text = "🍜 美食";
                ChipDrink.Text = "☕ 饮品";
                ChipSight.Text = "🏛️ 古迹";
                ChipOther.Text = "📍 其他";
                break;
            default:
                ChipAll.Text = "🔥 Nổi bật";
                ChipFood.Text = "🍜 Ẩm thực";
                ChipDrink.Text = "☕ Cà phê";
                ChipSight.Text = "🏛️ Di tích";
                ChipOther.Text = "📍 Khác";
                break;
        }
    }

    private void UpdateNearbySectionTexts()
    {
        switch (AppLanguage.Current)
        {
            case "en":
                LblNearbyTitle.Text = "📍 Near you";
                LblNearbyViewAll.Text = "View all";
                break;
            case "ja":
                LblNearbyTitle.Text = "📍 近くのスポット";
                LblNearbyViewAll.Text = "すべて表示";
                break;
            case "ko":
                LblNearbyTitle.Text = "📍 내 주변";
                LblNearbyViewAll.Text = "전체 보기";
                break;
            case "zh":
                LblNearbyTitle.Text = "📍 附近地点";
                LblNearbyViewAll.Text = "查看全部";
                break;
            default:
                LblNearbyTitle.Text = "📍 Gần bạn";
                LblNearbyViewAll.Text = "Xem tất cả";
                break;
        }
    }

    private static string NormalizeTagKey(string rawTag)
    {
        var t = (rawTag ?? "").Trim().ToLowerInvariant();
        return t switch
        {
            "quan an" => "quán ăn",
            "quan nuoc" => "quán nước",
            "di tich lich su" => "di tích lịch sử",
            "dia diem du lich" => "địa điểm du lịch",
            _ => t
        };
    }
}