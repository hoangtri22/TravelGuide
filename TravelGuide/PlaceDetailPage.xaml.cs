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

        // ← Reload khi đổi ngôn ngữ
        AppLanguage.OnLanguageChanged += _ =>
            MainThread.BeginInvokeOnMainThread(() => RefreshUI());
    }

    public void LoadPlace(TouristPlace place)
    {
        _currentPlace = place;
        RefreshUI();
    }

    private void RefreshUI()
    {
        if (_currentPlace == null) return;
        ImgPlace.Source = _currentPlace.ImageSource;
        LblName.Text = _currentPlace.Name;         // Name tự lấy đúng ngôn ngữ
        LblDescription.Text = _currentPlace.Description;
        Title = _currentPlace.Name;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        MiniPlayer.Attach(_narrationEngine);
        RefreshUI(); // ← Refresh lại khi quay lại trang
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
    }

    private async void OnSpeakClicked(object sender, EventArgs e)
    {
        if (_currentPlace != null)
            await _narrationEngine.SpeakAsync(_currentPlace);
    }
}