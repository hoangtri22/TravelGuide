using TravelGuide.Models;
using Microsoft.Maui.ApplicationModel;

namespace TravelGuide;
/// <summary>Chi tiết một POI: ảnh, tên/mô tả theo ngôn ngữ, nút phát TTS, mini player.</summary>
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

    /// <summary>Gán POI hiện tại và làm mới binding.</summary>
    public void LoadPlace(TouristPlace place)
    {
        _currentPlace = place;
        RefreshUI();
    }

    /// <summary>Đổ ảnh, tên, mô tả và <c>Title</c> từ <see cref="_currentPlace"/>.</summary>
    private void RefreshUI()
    {
        if (_currentPlace == null) return;
        ImgPlace.Source = _currentPlace.ImageSource;
        LblName.Text = _currentPlace.Name;         // Name tự lấy đúng ngôn ngữ
        LblDescription.Text = _currentPlace.Description;
        Title = _currentPlace.Name;
        var map = (_currentPlace.MapLink ?? "").Trim();
        BtnMapLink.IsVisible = map.Length > 0 && Uri.TryCreate(map, UriKind.Absolute, out var u) &&
                               (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>Gắn mini player và refresh nội dung.</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        MiniPlayer.Attach(_narrationEngine);
        RefreshUI(); // ← Refresh lại khi quay lại trang
    }

    /// <summary>Lifecycle rời trang (có thể bổ sung gỡ đăng ký <see cref="AppLanguage.OnLanguageChanged"/> nếu cần).</summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
    }

    /// <summary>Đưa POI hiện tại vào <see cref="NarrationEngine.SpeakAsync"/>.</summary>
    private async void OnSpeakClicked(object sender, EventArgs e)
    {
        if (_currentPlace != null)
            await _narrationEngine.SpeakAsync(_currentPlace);
    }

    /// <summary>Mở <see cref="TouristPlace.MapLink"/> trong trình duyệt / app bản đồ.</summary>
    private async void OnMapLinkClicked(object sender, EventArgs e)
    {
        var url = _currentPlace?.MapLink?.Trim();
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            await Launcher.Default.OpenAsync(new Uri(url, UriKind.Absolute));
        }
        catch
        {
            await DisplayAlert("Lỗi", "Không mở được liên kết bản đồ.", "OK");
        }
    }
}