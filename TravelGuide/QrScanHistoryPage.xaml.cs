using System.Globalization;
using LocalizationResourceManager.Maui;
using TravelGuide.Models;

namespace TravelGuide;

/// <summary>Danh sách POI đã quét — chạm để mở chi tiết, không cần quét lại.</summary>
public partial class QrScanHistoryPage : ContentPage
{
    private readonly DatabaseService _dbService;
    private readonly TouristAuthService _authService;
    private List<TouristAuthService.MyScanHistoryRow> _rows = new();

    public QrScanHistoryPage(DatabaseService dbService, TouristAuthService authService)
    {
        InitializeComponent();
        _dbService = dbService;
        _authService = authService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var loggedIn = await _authService.IsLoggedInAsync();
        HintLabel.IsVisible = !loggedIn;
        if (!loggedIn)
        {
            HistoryList.ItemsSource = Array.Empty<ScanHistoryVm>();
            return;
        }

        var (ok, items, _) = await _authService.GetMyScanHistoryAsync();
        _rows = ok ? items.ToList() : new List<TouristAuthService.MyScanHistoryRow>();
        var culture = GetUiCulture();
        HistoryList.ItemsSource = _rows
            .Select(r => new ScanHistoryVm(
                r.PoiId,
                string.IsNullOrWhiteSpace(r.PoiNameVi) ? $"POI #{r.PoiId}" : r.PoiNameVi!,
                FormatSubtitle(r, culture)))
            .ToList();
    }

    private static CultureInfo GetUiCulture()
    {
        try
        {
            var loc = Application.Current?.Handler?.MauiContext?.Services.GetService<ILocalizationResourceManager>();
            if (loc?.CurrentCulture != null)
                return loc.CurrentCulture;
        }
        catch
        {
            // ignore
        }

        return CultureInfo.CurrentUICulture;
    }

    private static string FormatSubtitle(TouristAuthService.MyScanHistoryRow r, CultureInfo culture)
    {
        var local = r.LastScannedAtUtc.ToLocalTime();
        var when = local.ToString("g", culture);
        var evt = (r.EventType ?? "").Trim();
        if (evt.Length == 0) return when;
        return $"{when} · {evt}";
    }

    private async void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ScanHistoryVm vm) return;
        try
        {
            var places = await _dbService.GetPlacesAsync();
            var place = places.FirstOrDefault(p => p.Id == vm.PoiId);
            if (place is null)
            {
                var culture = GetUiCulture();
                var title = AppResources.ResourceManager.GetString("AppTitle", culture) ?? "TravelGuide";
                var msg = AppResources.ResourceManager.GetString("ScanHistoryPlaceMissing", culture)
                          ?? "Địa điểm không còn trong danh sách công khai.";
                await DisplayAlert(title, msg, "OK");
                return;
            }

            if (Handler?.MauiContext == null) return;
            var detailPage = Handler.MauiContext.Services.GetRequiredService<PlaceDetailPage>();
            detailPage.LoadPlace(place);
            await Navigation.PushAsync(detailPage);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Navigation error", ex.Message, "OK");
        }
        finally
        {
            if (sender is CollectionView cv) cv.SelectedItem = null;
        }
    }

    private sealed record ScanHistoryVm(int PoiId, string TitleText, string SubtitleText);
}
