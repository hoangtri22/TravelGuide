using SQLite;
using TravelGuide.Models;
using System.Text.Json;
using Microsoft.Maui.Devices.Sensors;

namespace TravelGuide
{
    public class DatabaseService
    {
        private SQLiteAsyncConnection? _db;

        // Lock để tránh nhiều thread cùng init database
        private readonly SemaphoreSlim _initLock = new(1, 1);

        // Cache dữ liệu để giảm số lần truy vấn database
        private List<TouristPlace>? _cachedPlaces;

        // =========================================================
        // Khởi tạo database (thread-safe)
        // =========================================================
        private async Task Init()
        {
            if (_db is not null) return;

            await _initLock.WaitAsync();
            try
            {
                if (_db is null)
                {
                    var dbPath = Path.Combine(FileSystem.AppDataDirectory, "TravelGuide.db3");
                    _db = new SQLiteAsyncConnection(dbPath);

                    await _db.CreateTableAsync<TouristPlace>();

                    // Tạo index để tăng tốc truy vấn theo vị trí
                    await _db.ExecuteAsync(
                        "CREATE INDEX IF NOT EXISTS idx_lat_lon ON TouristPlace(Latitude, Longitude)");
                }
            }
            finally
            {
                _initLock.Release();
            }
        }

        // =========================================================
        // Nạp dữ liệu từ file JSON vào database (chỉ chạy 1 lần)
        // =========================================================
        public async Task SeedDataAsync()
        {
            await Init();

            var count = await _db!.Table<TouristPlace>().CountAsync();
            if (count > 0) return;

            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("extra_places.json");
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var places = JsonSerializer.Deserialize<List<TouristPlace>>(json, options);

                if (places?.Count > 0)
                {
                    foreach (var p in places)
                    {
                        // Đảm bảo các field không null để tránh lỗi khi xử lý
                        p.NameEn ??= "";
                        p.DescEn ??= "";
                        p.NameJa ??= "";
                        p.DescJa ??= "";
                        p.NameKo ??= "";
                        p.DescKo ??= "";
                        p.NameZh ??= "";
                        p.DescZh ??= "";
                    }

                    await _db.InsertAllAsync(places);

                    // Lưu cache sau khi seed
                    _cachedPlaces = places;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Seed error: {ex.Message}");
            }
        }

        // =========================================================
        // Lấy toàn bộ danh sách địa điểm (có cache)
        // =========================================================
        public async Task<List<TouristPlace>> GetPlacesAsync()
        {
            await Init();

            if (_cachedPlaces != null)
                return _cachedPlaces;

            _cachedPlaces = await _db!.Table<TouristPlace>().ToListAsync();
            return _cachedPlaces;
        }

        // =========================================================
        // Lấy các địa điểm gần vị trí người dùng
        // Có tối ưu bằng bounding box trước khi tính khoảng cách
        // =========================================================
        public async Task<List<TouristPlace>> GetNearbyPlacesAsync(
            double userLat,
            double userLon,
            double radiusMeters = 1000)
        {
            var places = await GetPlacesAsync();

            // Tính khoảng giới hạn lat/lon
            double latRange = radiusMeters / 111000d;
            double lonRange = radiusMeters / (111000d * Math.Cos(userLat * Math.PI / 180));

            var filtered = places.Where(p =>
                p.Latitude >= userLat - latRange &&
                p.Latitude <= userLat + latRange &&
                p.Longitude >= userLon - lonRange &&
                p.Longitude <= userLon + lonRange
            );

            return filtered
                .Select(p => new
                {
                    Place = p,
                    Distance = Location.CalculateDistance(
                        userLat, userLon,
                        p.Latitude, p.Longitude,
                        DistanceUnits.Kilometers)
                })
                .Where(x => x.Distance * 1000 <= radiusMeters)
                .OrderBy(x => x.Distance)
                .Select(x => x.Place)
                .ToList();
        }

        // =========================================================
        // Cập nhật dữ liệu một địa điểm
        // Đồng thời update cache nếu có
        // =========================================================
        public async Task UpdatePlaceAsync(TouristPlace place)
        {
            await Init();
            await _db!.UpdateAsync(place);

            if (_cachedPlaces != null)
            {
                var index = _cachedPlaces.FindIndex(p => p.Id == place.Id);
                if (index >= 0)
                    _cachedPlaces[index] = place;
            }
        }

        // =========================================================
        // Kiểm tra đã dịch xong toàn bộ dữ liệu cho một ngôn ngữ chưa
        // =========================================================
        public async Task<bool> IsTranslatedAsync(string langCode)
        {
            var places = await GetPlacesAsync();
            if (places.Count == 0) return false;

            return langCode switch
            {
                "en" => places.All(p => !string.IsNullOrEmpty(p.NameEn) && p.NameEn != p.NameVi),
                "ja" => places.All(p => !string.IsNullOrEmpty(p.NameJa) && p.NameJa != p.NameVi),
                "ko" => places.All(p => !string.IsNullOrEmpty(p.NameKo) && p.NameKo != p.NameVi),
                "zh" => places.All(p => !string.IsNullOrEmpty(p.NameZh) && p.NameZh != p.NameVi),
                _ => true
            };
        }

        // =========================================================
        // Xóa cache để reload dữ liệu từ database khi cần
        // =========================================================
        public void ClearCache()
        {
            _cachedPlaces = null;
        }
    }
}