using TravelGuide.Models;

namespace TravelGuide;

public partial class PlaceDetailPage : ContentPage
{
    TouristPlace _currentPlace;

    public PlaceDetailPage(TouristPlace place)
    {
        InitializeComponent();
        _currentPlace = place;
        ImgPlace.Source = place.ImageUrl;
        LblName.Text = place.Name;
        LblDescription.Text = place.Description;
        Title = place.Name;
    }

    private async void OnSpeakClicked(object sender, EventArgs e)
    {
        if (_currentPlace != null)
        {
            // Dùng TextToSpeech mặc định của hệ thống
            await TextToSpeech.Default.SpeakAsync($"{_currentPlace.Name}. {_currentPlace.Description}");
        }
    }

    // Tự động ngừng đọc khi người dùng thoát trang để tiết kiệm RAM
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        TextToSpeech.Default.SpeakAsync(""); // Truyền chuỗi rỗng để stop
    }
}