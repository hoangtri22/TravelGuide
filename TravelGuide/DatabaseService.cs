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

            // Tạo bảng dựa trên Model TouristPlace bạn đã định nghĩa
            await _db.CreateTableAsync<TouristPlace>();
        }

        // 2. Hàm nạp dữ liệu từ file JSON vào SQLite
        public async Task SeedDataAsync()
        {
            await Init();

            // Chỉ nạp nếu database đang trống để tránh trùng lặp
            var count = await _db.Table<TouristPlace>().CountAsync();
            if (count > 0) return;

            try
            {
                // Mở file extra_places.json từ thư mục Resources/Raw
                using var stream = await FileSystem.OpenAppPackageFileAsync("extra_places.json");
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();

                // Chuyển dữ liệu JSON thành danh sách C#
                var places = JsonSerializer.Deserialize<List<TouristPlace>>(json);
                if (places != null)
                {
                    await _db.InsertAllAsync(places);
                }
            }
            catch (Exception ex)
            {
                // Ghi log lỗi nếu không tìm thấy file hoặc định dạng JSON sai
                System.Diagnostics.Debug.WriteLine($"Lỗi nạp dữ liệu: {ex.Message}");
            }
        }

        // 3. Hàm lấy danh sách địa điểm để hiển thị lên UI
        public async Task<List<TouristPlace>> GetPlacesAsync()
        {
            await Init();
            return await _db.Table<TouristPlace>().ToListAsync();
        }
    }
}