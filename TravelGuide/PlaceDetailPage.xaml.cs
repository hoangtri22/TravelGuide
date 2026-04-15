using TravelGuide.Models;
using Microsoft.Maui.ApplicationModel;
using System.Globalization;

namespace TravelGuide;
/// <summary>Chi tiết một POI: ảnh, tên/mô tả theo ngôn ngữ, nút phát TTS, mini player.</summary>
public partial class PlaceDetailPage : ContentPage
{
    private TouristPlace? _currentPlace;
    private readonly NarrationEngine _narrationEngine;
    private readonly DatabaseService _dbService;
    private readonly TouristAuthService _authService;
    private string _activeTab = "overview";
    private bool _isSubmittingReview;
    private sealed record LocalReviewVm(string Author, string RatingText, string TimeText, string Content);

    public PlaceDetailPage(NarrationEngine narrationEngine, DatabaseService dbService, TouristAuthService authService)
    {
        InitializeComponent();
        _narrationEngine = narrationEngine;
        _dbService = dbService;
        _authService = authService;

        // ← Reload khi đổi ngôn ngữ
        AppLanguage.OnLanguageChanged += _lang =>
            MainThread.BeginInvokeOnMainThread(() => { _ = RefreshUIAsync(); });
    }

    /// <summary>Gán POI hiện tại và làm mới binding.</summary>
    public void LoadPlace(TouristPlace place)
    {
        _currentPlace = place;
        _ = RefreshUIAsync();
    }

    /// <summary>Đổ ảnh, tên, mô tả và <c>Title</c> từ <see cref="_currentPlace"/>.</summary>
    private async Task RefreshUIAsync()
    {
        if (_currentPlace == null) return;
        ImgPlace.Source = _currentPlace.ImageSource;
        LblName.Text = _currentPlace.Name;
        LblDescription.Text = _currentPlace.Description;
        Title = _currentPlace.Name;

        var defaultRating = CalculateLocalRating(_currentPlace.Id, _currentPlace.Priority);
        LblRating.Text = $"{defaultRating:F1} ★★★★★";
        LblSubTitle.Text = BuildSubTitle(_currentPlace);

        var map = (_currentPlace.MapLink ?? "").Trim();
        BtnMapLink.IsVisible = map.Length > 0 && Uri.TryCreate(map, UriKind.Absolute, out var u) &&
                               (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
        var qr = (_currentPlace.QrImagePath ?? "").Trim();
        if (qr.Length > 0)
        {
            ImgQr.Source = qr;
            QrSection.IsVisible = true;
        }
        else
        {
            ImgQr.Source = null;
            QrSection.IsVisible = false;
        }

        LblAboutTag.Text = $"Danh mục: {GetTagLabel(_currentPlace.Tag)}";
        LblAboutPrice.Text = $"Mức giá tham khảo: {Math.Max(0, _currentPlace.Price):N0} VND";
        LblAboutCoords.Text = $"Tọa độ: {_currentPlace.Latitude:F6}, {_currentPlace.Longitude:F6}";
        LblAboutMapLink.Text = string.IsNullOrWhiteSpace(map) ? "Map link: chưa có" : $"Map link: {map}";

        var keywords = BuildKeywords(_currentPlace.Tag);
        LblReviewKeywords.Text = string.Join(" · ", keywords);
        await RefreshReviewsAsync(defaultRating);

        RefreshTabState();
    }

    /// <summary>Gắn mini player và refresh nội dung.</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        MiniPlayer.Attach(_narrationEngine);
        _ = RefreshUIAsync(); // ← Refresh lại khi quay lại trang
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

    private void OnTabClicked(object sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        var tab = (btn.CommandParameter as string)?.Trim().ToLowerInvariant();
        if (tab is not ("overview" or "reviews" or "about")) return;
        _activeTab = tab;
        RefreshTabState();
    }

    private void RefreshTabState()
    {
        SectionOverview.IsVisible = _activeTab == "overview";
        SectionReviews.IsVisible = _activeTab == "reviews";
        SectionAbout.IsVisible = _activeTab == "about";

        SetTabButtonState(TabOverview, _activeTab == "overview");
        SetTabButtonState(TabReviews, _activeTab == "reviews");
        SetTabButtonState(TabAbout, _activeTab == "about");
    }

    private static void SetTabButtonState(Button btn, bool active)
    {
        btn.TextColor = active ? Color.FromArgb("#0B74B2") : Color.FromArgb("#6B7280");
        btn.FontAttributes = active ? FontAttributes.Bold : FontAttributes.None;
    }

    private static double CalculateLocalRating(int id, int priority)
    {
        var score = 4.1 + ((id % 7) * 0.1) + Math.Min(priority, 5) * 0.03;
        return Math.Min(4.9, Math.Round(score, 1));
    }

    private static string BuildSubTitle(TouristPlace place)
    {
        var tag = GetTagLabel(place.Tag);
        return $"{tag} · Local guide";
    }

    private static string GetTagLabel(string? tag)
    {
        var t = (tag ?? "").Trim().ToLowerInvariant();
        return t switch
        {
            "quan an" => "Ẩm thực",
            "quan nuoc" => "Đồ uống",
            "di tich lich su" => "Di tích",
            _ => "Điểm tham quan"
        };
    }

    private static IReadOnlyList<string> BuildKeywords(string? tag)
    {
        var t = (tag ?? "").Trim().ToLowerInvariant();
        return t switch
        {
            "quan an" => ["món ngon", "phục vụ nhanh", "giá hợp lý", "hải sản tươi"],
            "quan nuoc" => ["không gian đẹp", "menu đa dạng", "phục vụ thân thiện", "view chill"],
            "di tich lich su" => ["giàu lịch sử", "kiến trúc đẹp", "đáng tham quan", "nhiều thông tin"],
            _ => ["đáng ghé", "khung cảnh đẹp", "thuận tiện", "nhiều trải nghiệm"]
        };
    }

    private async Task RefreshReviewsAsync(double defaultRating)
    {
        if (_currentPlace == null) return;
        var rows = await _dbService.GetPlaceReviewsAsync(_currentPlace.Id);
        var reviewCount = rows.Count;
        var avgRating = reviewCount == 0
            ? defaultRating
            : Math.Round(rows.Average(r => Math.Max(1, Math.Min(5, r.Rating))), 1);

        LblRating.Text = $"{avgRating:F1} ★★★★★";
        LblReviewCount.Text = $"({reviewCount:N0})";
        LblReviewSummary.Text = reviewCount == 0
            ? $"Chưa có đánh giá cho {GetTagLabel(_currentPlace.Tag)}"
            : $"Đánh giá từ khách đã đăng nhập ({reviewCount:N0})";

        ReviewsCollection.ItemsSource = rows
            .Select(r => new LocalReviewVm(
                r.Username,
                $"{new string('★', Math.Max(1, Math.Min(5, r.Rating)))}",
                FormatRelativeTime(r.CreatedAtUtc),
                r.Content))
            .ToList();
    }

    private async void OnSubmitReviewClicked(object sender, EventArgs e)
    {
        if (_isSubmittingReview || _currentPlace == null) return;
        var content = (ReviewContentEditor.Text ?? string.Empty).Trim();
        if (content.Length < 4)
        {
            await DisplayAlert("Thiếu nội dung", "Vui lòng nhập ít nhất 4 ký tự cho đánh giá.", "OK");
            return;
        }

        var (ok, username, _) = await _authService.GetMeAsync();
        if (!ok || string.IsNullOrWhiteSpace(username))
        {
            await DisplayAlert("Cần đăng nhập", "Bạn cần đăng nhập tài khoản để gửi đánh giá.", "OK");
            return;
        }

        _isSubmittingReview = true;
        if (sender is Button submitBtn) submitBtn.IsEnabled = false;
        try
        {
            var rating = ParseRatingFromPicker();
            await _dbService.AddPlaceReviewAsync(_currentPlace.Id, username, rating, content);
            ReviewContentEditor.Text = string.Empty;
            ReviewRatingPicker.SelectedIndex = 0;
            await RefreshReviewsAsync(CalculateLocalRating(_currentPlace.Id, _currentPlace.Priority));
            await DisplayAlert("Thành công", "Đã lưu đánh giá của bạn.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Lỗi", $"Không thể lưu đánh giá: {ex.Message}", "OK");
        }
        finally
        {
            _isSubmittingReview = false;
            if (sender is Button submitButton) submitButton.IsEnabled = true;
        }
    }

    private int ParseRatingFromPicker()
    {
        var raw = ReviewRatingPicker.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(raw)) return 5;
        var numberPart = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(numberPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? Math.Max(1, Math.Min(5, value))
            : 5;
    }

    private static string FormatRelativeTime(DateTime createdAtUtc)
    {
        var ts = DateTime.UtcNow - createdAtUtc;
        if (ts.TotalMinutes < 1) return "vừa xong";
        if (ts.TotalMinutes < 60) return $"{(int)ts.TotalMinutes} phút trước";
        if (ts.TotalHours < 24) return $"{(int)ts.TotalHours} giờ trước";
        if (ts.TotalDays < 30) return $"{(int)ts.TotalDays} ngày trước";
        var months = Math.Max(1, (int)(ts.TotalDays / 30));
        return $"{months} tháng trước";
    }
}