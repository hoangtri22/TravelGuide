using TravelGuide.Models;

namespace TravelGuide;

public partial class PlaceDetailPage : ContentPage
{
    private TouristPlace? _currentPlace;
    private readonly NarrationEngine _narrationEngine;

    public PlaceDetailPage(NarrationEngine narrationEngine)
    {
        InitializeComponent();
        _narrationEngine = narrationEngine;
    }

    public void LoadPlace(TouristPlace place)
    {
        _currentPlace = place;
        ImgPlace.Source = place.ImageSource;
        LblName.Text = place.Name;
        LblDescription.Text = place.Description;
        Title = place.Name;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        MiniPlayer.Attach(_narrationEngine); // ✅
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Không stop — để MiniPlayer tiếp tục khi navigate
    }

    private async void OnSpeakClicked(object sender, EventArgs e)
    {
        if (_currentPlace != null)
            await _narrationEngine.SpeakAsync(_currentPlace);
    }
}