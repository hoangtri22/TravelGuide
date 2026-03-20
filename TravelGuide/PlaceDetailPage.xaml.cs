using TravelGuide.Models;

namespace TravelGuide;

public partial class PlaceDetailPage : ContentPage
{
    private TouristPlace? _currentPlace;

    // FIX: Inject NarrationEngine để dùng đúng locale + queue
    private readonly NarrationEngine _narrationEngine;

    // Constructor đầy đủ — dùng khi navigate bằng DI
    public PlaceDetailPage(NarrationEngine narrationEngine)
    {
        InitializeComponent();
        _narrationEngine = narrationEngine;
    }

    /// <summary>
    /// Gán dữ liệu POI sau khi navigate tới trang.
    /// Gọi: await Shell.Current.GoToAsync(nameof(PlaceDetailPage));
    ///       sau đó set page.LoadPlace(place) hoặc dùng QueryProperty.
    /// </summary>
    public void LoadPlace(TouristPlace place)
    {
        _currentPlace = place;
        ImgPlace.Source = place.ImageSource;
        LblName.Text = place.Name;
        LblDescription.Text = place.Description;
        Title = place.Name;
    }

    private async void OnSpeakClicked(object sender, EventArgs e)
    {
        if (_currentPlace == null) return;

        // FIX: Dùng NarrationEngine → đúng locale, câu intro tự nhiên, có queue
        await _narrationEngine.SpeakAsync(_currentPlace);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // FIX: StopAsync() clear queue luôn, không có CS4014 warning
        _ = _narrationEngine.StopAsync();
    }
}