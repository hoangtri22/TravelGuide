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

        AppLanguage.OnLanguageChanged += _ =>
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                UpdateLocalizedChrome();
                await LoadPlacesAsync();
            });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        MiniPlayer.Attach(_narrationEngine);
        UpdateLocalizedChrome();
        await LoadPlacesAsync();
    }

    private async Task LoadPlacesAsync()
    {
        _allPlaces = await _dbService.GetPlacesAsync();
        LblTotalPoi.Text = $"{_allPlaces.Count} POI";
        PlacesCollection.ItemsSource = null;  // ← force refresh
        PlacesCollection.ItemsSource = _allPlaces;
    }

    private void UpdateLocalizedChrome()
    {
        LblDashboardTitle.Text = AppLanguage.Current switch
        {
            "en" => "Dashboard",
            "ja" => "ダッシュボード",
            "ko" => "대시보드",
            "zh" => "仪表盘",
            _ => "Dashboard"
        };
    }

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

    private async void OnPlaceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not TouristPlace place) return;
        if (Handler?.MauiContext == null) return;
        var detailPage = Handler.MauiContext.Services
            .GetRequiredService<PlaceDetailPage>();
        detailPage.LoadPlace(place);
        await Navigation.PushAsync(detailPage);
        if (sender is CollectionView cv) cv.SelectedItem = null;
    }

    private async void OpenMap(object sender, EventArgs e)
    {
        if (Handler?.MauiContext == null) return;
        var mapPage = Handler.MauiContext.Services.GetRequiredService<MapPage>();
        await Navigation.PushAsync(mapPage);
    }

    private async void OpenAudio(object sender, EventArgs e)
    {
        if (Handler?.MauiContext == null) return;
        var audioPage = Handler.MauiContext.Services.GetRequiredService<AudioPage>();
        await Navigation.PushAsync(audioPage);
    }

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
}