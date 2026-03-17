using TravelGuide.Models;

namespace TravelGuide;

public partial class HomePage : ContentPage
{
    private readonly DatabaseService _dbService;
    private List<TouristPlace> _allPlaces = new();

    // Sử dụng Dependency Injection để lấy DatabaseService đã đăng ký
    public HomePage(DatabaseService dbService)
    {
        InitializeComponent();
        _dbService = dbService;
    }

    // Mỗi khi quay lại trang chủ, cập nhật lại dữ liệu mới nhất
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadData();
    }

    private async Task LoadData()
    {
        // Lấy toàn bộ 30+ địa điểm từ SQLite
        _allPlaces = await _dbService.GetPlacesAsync();

        // 1. Lấy 5 địa điểm ngẫu nhiên để hiển thị mặc định
        var random5 = _allPlaces.OrderBy(x => Guid.NewGuid()).Take(5).ToList();

        PlacesCollection.ItemsSource = random5;
    }

    // 2. Xử lý tìm kiếm (Search)
    private void OnSearchBarTextChanged(object sender, TextChangedEventArgs e)
    {
        var searchTerm = e.NewTextValue?.ToLower() ?? "";

        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            // Nếu xóa thanh tìm kiếm, quay lại hiển thị 5 cái ngẫu nhiên ban đầu
            var random5 = _allPlaces.OrderBy(x => Guid.NewGuid()).Take(5).ToList();
            PlacesCollection.ItemsSource = random5;
        }
        else
        {
            // Tìm kiếm dựa trên tên địa danh (không phân biệt hoa thường)
            var results = _allPlaces
                .Where(p => p.Name.ToLower().Contains(searchTerm))
                .ToList();

            PlacesCollection.ItemsSource = results;
        }
    }

    // Mở bản đồ
    // Mở bản đồ
    private async void OpenMap(object sender, EventArgs e)
    {
        // Lấy MapPage từ hệ thống Service (đã có sẵn dbService bên trong)
        var mapPage = Handler.MauiContext.Services.GetService<MapPage>();
        await Navigation.PushAsync(mapPage);
    }

    // Chuyển sang trang chi tiết
    private async void OnPlaceSelected(object sender, SelectionChangedEventArgs e)
    {
        var selectedPlace = e.CurrentSelection.FirstOrDefault() as TouristPlace;

        if (selectedPlace != null)
        {
            await Navigation.PushAsync(new PlaceDetailPage(selectedPlace));

            if (sender is CollectionView collectionView)
            {
                collectionView.SelectedItem = null;
            }
        }
    }
}