using TravelGuide.Models;

namespace TravelGuide;

public partial class HomePage : ContentPage
{
    public HomePage()
    {
        InitializeComponent();
        // Để đổ dữ liệu ngay khi khởi tạo
        LoadData();
    }

    private void LoadData()
    {
        // Lấy dữ liệu tập trung từ DataService giúp đồng bộ
        PlacesCollection.ItemsSource = DataService.GetPlaces();
    }

    // Mở trang Bản đồ
    private async void OpenMap(object sender, EventArgs e)
    {
        // Mở bản đồ chung
        await Navigation.PushAsync(new MapPage());
    }

    // Chuyển sang trang chi tiết
    private async void OnPlaceSelected(object sender, SelectionChangedEventArgs e)
    {
        // Tránh lỗi khi CurrentSelection rỗng
        var selectedPlace = e.CurrentSelection.FirstOrDefault() as TouristPlace;

        if (selectedPlace != null)
        {
            // Điều hướng sang trang chi tiết và truyền dữ liệu
            await Navigation.PushAsync(new PlaceDetailPage(selectedPlace));

            // Quan trọng: Reset lại để có thể chọn lại chính địa danh đó ngay lập tức
            if (sender is CollectionView collectionView)
            {
                collectionView.SelectedItem = null;
            }
        }
    }
}