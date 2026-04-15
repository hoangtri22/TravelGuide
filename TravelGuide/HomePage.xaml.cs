using LocalizationResourceManager.Maui;
using TravelGuide.Models;
using System.Globalization;

namespace TravelGuide;

/// <summary>
/// Trang chủ: danh sách địa điểm, tìm kiếm, lối tới bản đồ / âm thanh / gần đây.
/// </summary>
public partial class HomePage : ContentPage
{
    private readonly DatabaseService _dbService;
    private readonly NarrationEngine _narrationEngine;
    private List<TouristPlace> _allPlaces = new();
    private readonly List<LangOption> _languageOptions =
    [
        new("vi", "🇻🇳 Tiếng Việt"),
        new("en", "🇬🇧 English"),
        new("ja", "🇯🇵 日本語"),
        new("ko", "🇰🇷 한국어"),
        new("zh", "🇨🇳 中文")
    ];

    /// <summary>Đăng ký lắng nghe đổi ngôn ngữ để reload danh sách và chrome.</summary>
    public HomePage(DatabaseService dbService, NarrationEngine narrationEngine)
    {
        InitializeComponent();
        _dbService = dbService;
        _narrationEngine = narrationEngine;
        UpdateLanguageButton(AppLanguage.Current);

        AppLanguage.OnLanguageChanged += _ =>
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                UpdateLanguageButton(AppLanguage.Current);
                UpdateLocalizedChrome();
                await LoadPlacesAsync();
            });
    }

    /// <summary>Gắn mini player và tải lại danh sách POI.</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        MiniPlayer.Attach(_narrationEngine);
        SyncLocalization(AppLanguage.Current);
        UpdateLanguageButton(AppLanguage.Current);
        UpdateLocalizedChrome();
        _dbService.ClearCache();
        await LoadPlacesAsync();
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
        LblTotalPoi.Text = FormatPlacesCount(_allPlaces.Count);
        PlacesCollection.ItemsSource = null;  // ← force refresh
        PlacesCollection.ItemsSource = _allPlaces;
    }

    /// <summary>Cập nhật nhãn số địa điểm khi đổi ngôn ngữ (tiêu đề hero dùng ResX trong XAML).</summary>
    private void UpdateLocalizedChrome()
    {
        LblTotalPoi.Text = FormatPlacesCount(_allPlaces.Count);
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

    /// <summary>Lọc danh sách theo từ khóa (tên/mô tả đa ngôn ngữ).</summary>
    private void OnSearchBarTextChanged(object sender, TextChangedEventArgs e)
    {
        var keyword = (e.NewTextValue ?? "").Trim().ToLower();
        PlacesCollection.ItemsSource = null;
        PlacesCollection.ItemsSource = string.IsNullOrEmpty(keyword)
            ? _allPlaces
            : _allPlaces.Where(p =>
                p.NameVi.ToLower().Contains(keyword) ||
                p.NameEn.ToLower().Contains(keyword) ||
                p.DescVi.ToLower().Contains(keyword) ||
                (p.NameJa != null && p.NameJa.ToLower().Contains(keyword)) ||
                (p.NameKo != null && p.NameKo.ToLower().Contains(keyword)) ||
                (p.NameZh != null && p.NameZh.ToLower().Contains(keyword)))
              .ToList();
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
        await MainScroll.ScrollToAsync(0, 0, true);
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
}