using SQLite;
using TravelGuide.Models;
using System.Text.Json;

namespace TravelGuide
{
    public class DatabaseService
    {
        private SQLiteAsyncConnection? _db;

        // ── 1. Khởi tạo Database ────────────────────────────────────────
        async Task Init()
        {
            if (_db is not null) return;
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "TravelGuide.db3");
            _db = new SQLiteAsyncConnection(dbPath);
            await _db.CreateTableAsync<TouristPlace>();
        }

        // ── 2. Seed dữ liệu từ JSON ─────────────────────────────────────
        public async Task SeedDataAsync()
        {
            await Init();

            var count = await _db!.Table<TouristPlace>().CountAsync();
            if (count > 0) return; // ← Giữ lại data + bản dịch đã có

            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("extra_places.json");
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var places = JsonSerializer.Deserialize<List<TouristPlace>>(json, options);

                if (places != null && places.Count > 0)
                {
                    foreach (var p in places)
                    {
                        // NameEn giữ trống — IsTranslatedAsync sẽ phát hiện chưa dịch
                        if (string.IsNullOrEmpty(p.NameEn)) p.NameEn = "";
                        if (string.IsNullOrEmpty(p.DescEn)) p.DescEn = "";
                    }

                    await _db!.InsertAllAsync(places);
                    System.Diagnostics.Debug.WriteLine($"✅ Đã nạp {places.Count} địa điểm.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Lỗi nạp dữ liệu: {ex.Message}");
            }
        }

        // ── 3. Lấy tất cả địa điểm ─────────────────────────────────────
        public async Task<List<TouristPlace>> GetPlacesAsync()
        {
            await Init();
            return await _db!.Table<TouristPlace>().ToListAsync();
        }

        // ── 4. Lấy địa điểm gần người dùng ─────────────────────────────
        public async Task<List<TouristPlace>> GetNearbyPlacesAsync(
            double userLat, double userLon, double radiusMeters = 1000)
        {
            var all = await GetPlacesAsync();
            return all
                .Where(p =>
                {
                    double dist = Location.CalculateDistance(
                        userLat, userLon,
                        p.Latitude, p.Longitude,
                        DistanceUnits.Kilometers) * 1000;
                    return dist <= radiusMeters;
                })
                .OrderBy(p => Location.CalculateDistance(
                    userLat, userLon,
                    p.Latitude, p.Longitude,
                    DistanceUnits.Kilometers))
                .ToList();
        }

        // ── 5. Cập nhật 1 địa điểm (sau khi dịch xong) ─────────────────
        public async Task UpdatePlaceAsync(TouristPlace place)
        {
            await Init();
            await _db!.UpdateAsync(place);
        }

        // ── 6. Kiểm tra đã dịch ngôn ngữ nào chưa ──────────────────────
        // FIX: Kiểm tra TẤT CẢ địa điểm, không chỉ place đầu tiên
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
                _ => true // "vi" luôn có sẵn
            };
        }
    }
}