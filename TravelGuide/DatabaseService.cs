using SQLite;
using TravelGuide.Models;
using System.Text.Json;

namespace TravelGuide
{
    public class DatabaseService
    {
        private SQLiteAsyncConnection _db;

        // 1. Khởi tạo Database
        async Task Init()
        {
            if (_db is not null) return;

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "TravelGuide.db3");
            _db = new SQLiteAsyncConnection(dbPath);

            // --- QUAN TRỌNG: CẬP NHẬT CẤU TRÚC BẢNG ---
            // Nếu bạn đang trong quá trình phát triển và thay đổi Model liên tục, 
            // hãy bỏ comment dòng dưới đây để mỗi lần chạy nó sẽ xóa bảng cũ tạo bảng mới:
            // await _db.DropTableAsync<TouristPlace>(); 

            await _db.CreateTableAsync<TouristPlace>();
        }

        // 2. Hàm nạp dữ liệu từ file JSON vào SQLite
        public async Task SeedDataAsync()
        {
            await Init();

            // Kiểm tra xem đã có dữ liệu chưa
            var count = await _db.Table<TouristPlace>().CountAsync();

            // Nếu đã có dữ liệu, mình xóa đi nạp lại để đảm bảo cập nhật đúng bản JSON mới nhất
            if (count > 0)
            {
                await _db.DeleteAllAsync<TouristPlace>();
            }

            try
            {
                // Mở file extra_places.json từ Resources/Raw
                using var stream = await FileSystem.OpenAppPackageFileAsync("extra_places.json");
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();

                // Cấu hình để không phân biệt chữ hoa chữ thường khi đọc JSON
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                var places = JsonSerializer.Deserialize<List<TouristPlace>>(json, options);

                if (places != null && places.Count > 0)
                {
                    await _db.InsertAllAsync(places);
                    System.Diagnostics.Debug.WriteLine($"Đã nạp thành công {places.Count} địa điểm vào DB.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Lỗi nạp dữ liệu: {ex.Message}");
            }
        }

        // 3. Hàm lấy danh sách địa điểm
        public async Task<List<TouristPlace>> GetPlacesAsync()
        {
            await Init();
            return await _db.Table<TouristPlace>().ToListAsync();
        }
    }
}