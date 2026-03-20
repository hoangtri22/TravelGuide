using TravelGuide.Models;

namespace TravelGuide;

public partial class HomePage : ContentPage
{
    private readonly DatabaseService _dbService;
    private readonly NarrationEngine _narrationEngine;
    private List<TouristPlace> _allPlaces = new();

    public HomePage(DatabaseService dbService, NarrationEngine narrationEngine)
    {
        InitializeComponent();
        _dbService = dbService;
        _narrationEngine = narrationEngine;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadPlacesAsync();
    }

    private async Task LoadPlacesAsync()
    {
        _allPlaces = await _dbService.GetPlacesAsync();
        PlacesCollection.ItemsSource = _allPlaces;
    }

    // ── Search ────────────────────────────────────────────────────────────
    private void OnSearchBarTextChanged(object sender, TextChangedEventArgs e)
    {
        var keyword = (e.NewTextValue ?? "").Trim().ToLower();

        PlacesCollection.ItemsSource = string.IsNullOrEmpty(keyword)
            ? _allPlaces
            : _allPlaces.Where(p =>
                p.Name.ToLower().Contains(keyword) ||
                p.Description.ToLower().Contains(keyword)).ToList();
    }

    // ── Chọn địa điểm → navigate sang PlaceDetailPage ────────────────────
    private async void OnPlaceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not TouristPlace place) return;

        // FIX: Dùng factory pattern — lấy page từ DI rồi gọi LoadPlace()
        var detailPage = Handler.MauiContext!.Services.GetRequiredService<PlaceDetailPage>();
        detailPage.LoadPlace(place);
        await Navigation.PushAsync(detailPage);

        // Reset selection để tap lại vẫn trigger
        if (sender is CollectionView cv) cv.SelectedItem = null;
    }

    // ── Nút Bản đồ ────────────────────────────────────────────────────────
    private async void OpenMap(object sender, EventArgs e)
    {
        var mapPage = Handler.MauiContext!.Services.GetRequiredService<MapPage>();
        await Navigation.PushAsync(mapPage);
    }

    // ── Nút Gần đây ───────────────────────────────────────────────────────
    private async void OpenNearby(object sender, EventArgs e)
    {
        try
        {
            var location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)));

            if (location == null) return;

            var nearby = await _dbService.GetNearbyPlacesAsync(
                location.Latitude, location.Longitude, radiusMeters: 1000);

            PlacesCollection.ItemsSource = nearby;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HomeNearby] {ex.Message}");
        }
    }
}