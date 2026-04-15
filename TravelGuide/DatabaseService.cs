using TravelGuide.Models;
using System.Text.Json;
using Microsoft.Maui.Devices.Sensors;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using SQLite;

namespace TravelGuide;

/// <summary>
/// Tải và cache danh sách <see cref="TouristPlace"/>: ưu tiên API admin (<c>/api/public/pois</c>), fallback <c>extra_places.json</c>,
/// bổ sung dịch MyMemory khi thiếu field theo ngôn ngữ. Xóa cache khi sự kiện đổi ngôn ngữ của <see cref="AppLanguage"/>.
/// </summary>
public class DatabaseService
{
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _syncLock = new(1, 1);
        private readonly string _sqlitePath;
        private const string PublicApiBaseLoopback = "http://127.0.0.1:5096";
        private const string PublicApiBaseAndroid = "http://10.0.2.2:5096";
        private List<TouristPlace>? _cachedPlaces;
        private SQLiteAsyncConnection? _localDb;
        private DateTime _lastSyncUtc = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

        private static readonly JsonSerializerOptions ApiJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>Tiêm <see cref="HttpClient"/> và đăng ký xóa cache khi đổi ngôn ngữ.</summary>
        public DatabaseService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _sqlitePath = Path.Combine(FileSystem.AppDataDirectory, "travelguide-local.db3");
            AppLanguage.OnLanguageChanged += _ => ClearCache();
        }

        /// <summary>Buộc làm mới dữ liệu POI (gọi khi khởi động app qua <see cref="App"/>).</summary>
        public async Task SeedDataAsync()
        {
            await RefreshFromApiOrFallbackAsync(force: true);
        }

        /// <summary>Danh sách POI đã cache (~30s); đồng bộ API/fallback khi hết hạn.</summary>
        public async Task<List<TouristPlace>> GetPlacesAsync()
        {
            if (_cachedPlaces != null && DateTime.UtcNow - _lastSyncUtc < CacheDuration)
                return _cachedPlaces;

            await RefreshFromApiOrFallbackAsync(force: false);
            return _cachedPlaces ?? new List<TouristPlace>();
        }

        /// <summary>Lọc POI trong <paramref name="radiusMeters"/> quanh (<paramref name="userLat"/>, <paramref name="userLon"/>), có bounding box.</summary>
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
                .OrderByDescending(x => x.Place.Priority)
                .ThenBy(x => x.Distance)
                .Select(x => x.Place)
                .ToList();
        }

        /// <summary>Cập nhật một phần tử trong cache nếu tồn tại (không ghi file/SQLite cục bộ).</summary>
        public async Task UpdatePlaceAsync(TouristPlace place)
        {
            if (_cachedPlaces != null)
            {
                var index = _cachedPlaces.FindIndex(p => p.Id == place.Id);
                if (index >= 0)
                    _cachedPlaces[index] = place;
            }
            var db = await GetLocalDbAsync();
            await db.InsertOrReplaceAsync(place);
        }

        /// <summary>Thêm đánh giá cục bộ cho một POI theo username đã đăng nhập.</summary>
        public async Task AddPlaceReviewAsync(int poiId, string username, int rating, string content)
        {
            if (poiId <= 0) return;
            var user = (username ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(user)) return;
            var text = (content ?? string.Empty).Trim();
            if (text.Length == 0) return;

            var review = new TouristPlaceReview
            {
                PoiId = poiId,
                Username = user,
                Rating = Math.Max(1, Math.Min(5, rating)),
                Content = text,
                CreatedAtUtc = DateTime.UtcNow
            };

            var db = await GetLocalDbAsync();
            await db.InsertAsync(review);
        }

        /// <summary>Đọc đánh giá local của một POI (mới nhất trước).</summary>
        public async Task<List<TouristPlaceReview>> GetPlaceReviewsAsync(int poiId, int take = 100)
        {
            if (poiId <= 0) return new List<TouristPlaceReview>();
            var db = await GetLocalDbAsync();
            return await db.Table<TouristPlaceReview>()
                .Where(r => r.PoiId == poiId)
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(Math.Max(1, take))
                .ToListAsync();
        }

        /// <summary>Đánh dấu đã đến 1 POI và lưu xuống SQLite local.</summary>
        public async Task<bool> MarkPlaceVisitedAsync(int placeId)
        {
            var places = await GetPlacesAsync();
            var place = places.FirstOrDefault(p => p.Id == placeId);
            if (place == null || place.IsVisited) return false;

            place.IsVisited = true;
            await UpdatePlaceAsync(place);
            return true;
        }

        /// <summary>True nếu mọi POI có tên+mô tả đủ cho <paramref name="langCode"/> (en/ja/ko/zh).</summary>
        public async Task<bool> IsTranslatedAsync(string langCode)
        {
            var places = await GetPlacesAsync();
            if (places.Count == 0) return false;

            return langCode switch
            {
                "en" => places.All(p => !string.IsNullOrWhiteSpace(p.NameEn) && p.NameEn != p.NameVi
                    && !string.IsNullOrWhiteSpace(p.DescEn)),
                "ja" => places.All(p => !string.IsNullOrWhiteSpace(p.NameJa) && !string.IsNullOrWhiteSpace(p.DescJa)),
                "ko" => places.All(p => !string.IsNullOrWhiteSpace(p.NameKo) && !string.IsNullOrWhiteSpace(p.DescKo)),
                "zh" => places.All(p => !string.IsNullOrWhiteSpace(p.NameZh) && !string.IsNullOrWhiteSpace(p.DescZh)),
                _ => true
            };
        }

        /// <summary>Xóa cache trong RAM để lần gọi sau tải lại từ API/fallback.</summary>
        public void ClearCache()
        {
            _cachedPlaces = null;
            _lastSyncUtc = DateTime.MinValue;
        }

        /// <summary>
        /// URL gốc TravelGuide.API (HTTP) — <c>Preferences api_base_url</c> hoặc mặc định.
        /// Android emulator: <c>10.0.2.2</c> = máy host; thiết bị thật: IP LAN máy chạy API (vd <c>http://192.168.1.10:5096</c>).
        /// Chỉ POI <c>published</c> mới có trong <c>/api/public/pois</c>.
        /// </summary>
        public string GetCurrentApiBaseUrl()
        {
            var defaultUrl = DeviceInfo.Platform == DevicePlatform.Android
                ? PublicApiBaseAndroid
                : PublicApiBaseLoopback;

            var configured = Preferences.Get("api_base_url", defaultUrl)?.Trim();
            if (string.IsNullOrWhiteSpace(configured))
            {
                Preferences.Set("api_base_url", defaultUrl);
                return defaultUrl;
            }

            if (ShouldForcePublicApiBase(configured))
            {
                Preferences.Set("api_base_url", defaultUrl);
                System.Diagnostics.Debug.WriteLine($"[API] Reset api_base_url '{configured}' -> '{defaultUrl}'");
                return defaultUrl;
            }

            return configured;
        }

        /// <summary>Đồng bộ cache: API trước, nếu rỗng và chưa có cache thì đọc JSON nhúng.</summary>
        private async Task RefreshFromApiOrFallbackAsync(bool force)
        {
            await _syncLock.WaitAsync();
            try
            {
                if (!force && _cachedPlaces != null && DateTime.UtcNow - _lastSyncUtc < CacheDuration)
                    return;

                var (httpOk, fromApi) = await TryGetFromApiAsync();
                if (httpOk)
                {
                    await EnsureClientTranslationsForCurrentLanguageAsync(fromApi);
                    await ReplaceLocalDbAsync(fromApi);
                    _cachedPlaces = fromApi;
                    _lastSyncUtc = DateTime.UtcNow;
                    return;
                }

                var fromLocalDb = await LoadFromLocalDbAsync();
                if (fromLocalDb.Count > 0)
                {
                    await EnsureClientTranslationsForCurrentLanguageAsync(fromLocalDb);
                    await ReplaceLocalDbAsync(fromLocalDb);
                    _cachedPlaces = fromLocalDb;
                    _lastSyncUtc = DateTime.UtcNow;
                    return;
                }

                var localFallback = await LoadLocalFallbackAsync();
                await EnsureClientTranslationsForCurrentLanguageAsync(localFallback);
                await ReplaceLocalDbAsync(localFallback);
                _cachedPlaces = localFallback;
                _lastSyncUtc = DateTime.UtcNow;
            }
            finally
            {
                _syncLock.Release();
            }
        }

        /// <summary>Khởi tạo SQLite local và tạo bảng POI nếu chưa có.</summary>
        private async Task<SQLiteAsyncConnection> GetLocalDbAsync()
        {
            if (_localDb != null) return _localDb;

            _localDb = new SQLiteAsyncConnection(_sqlitePath);
            await _localDb.CreateTableAsync<TouristPlace>();
            await _localDb.CreateTableAsync<TouristPlaceReview>();
            return _localDb;
        }

        /// <summary>Đọc toàn bộ POI từ SQLite local.</summary>
        private async Task<List<TouristPlace>> LoadFromLocalDbAsync()
        {
            try
            {
                var db = await GetLocalDbAsync();
                return await db.Table<TouristPlace>()
                    .OrderByDescending(p => p.Priority)
                    .ToListAsync();
            }
            catch
            {
                return new List<TouristPlace>();
            }
        }

        /// <summary>Ghi đè danh sách POI local theo snapshot mới nhất từ API/fallback.</summary>
        private async Task ReplaceLocalDbAsync(List<TouristPlace> places)
        {
            try
            {
                var db = await GetLocalDbAsync();
                await db.RunInTransactionAsync(connection =>
                {
                    var visitedById = connection.Table<TouristPlace>()
                        .Where(p => p.IsVisited)
                        .Select(p => p.Id)
                        .ToHashSet();

                    foreach (var place in places)
                    {
                        if (visitedById.Contains(place.Id))
                            place.IsVisited = true;
                    }

                    connection.DeleteAll<TouristPlace>();
                    connection.InsertAll(places);
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SQLite] Save POI failed: {ex.Message}");
            }
        }

        /// <summary>
        /// GET <c>/api/public/pois?lang=</c>. Trả về <c>(true, list)</c> khi HTTP thành công (kể cả danh sách rỗng);
        /// <c>(false, rỗng)</c> khi không kết nối được server — khi đó mới dùng SQLite / JSON nhúng.
        /// </summary>
        private async Task<(bool HttpOk, List<TouristPlace> Places)> TryGetFromApiAsync()
        {
            try
            {
                var baseUrl = GetCurrentApiBaseUrl().TrimEnd('/');
                var lang = string.IsNullOrWhiteSpace(AppLanguage.Current) ? "vi" : AppLanguage.Current;
                var url = $"{baseUrl}/api/public/pois?lang={Uri.EscapeDataString(lang)}";
                System.Diagnostics.Debug.WriteLine($"[API] Fetch POI from {url}");
                using var resp = await _httpClient.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[API] GET {url} → HTTP {(int)resp.StatusCode}");
                    return (false, new List<TouristPlace>());
                }

                var apiPois = await resp.Content.ReadFromJsonAsync<List<ApiPoi>>(ApiJsonOptions);
                if (apiPois == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[API] JSON null từ {url}");
                    return (false, new List<TouristPlace>());
                }

                var list = apiPois.Select(MapFromApi).ToList();
                return (true, list);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Load POI failed: {ex.Message}");
                return (false, new List<TouristPlace>());
            }
        }

        private static bool ShouldForcePublicApiBase(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return true;

            // POI public đã chuyển qua TravelGuide.API; tránh trỏ nhầm sang AdminWeb.
            return uri.Port is 5280 or 7145;
        }

        /// <summary>
        /// Khi UI không phải tiếng Việt mà API/file chưa có Name*/Desc* đầy đủ → dịch qua MyMemory trên máy.
        /// </summary>
        private async Task EnsureClientTranslationsForCurrentLanguageAsync(List<TouristPlace> places)
        {
            var lang = string.IsNullOrWhiteSpace(AppLanguage.Current) ? "vi" : AppLanguage.Current;
            if (lang is not ("en" or "ja" or "ko" or "zh") || places.Count == 0)
                return;

            for (var i = 0; i < places.Count; i++)
            {
                var p = places[i];
                if (string.IsNullOrWhiteSpace(p.NameVi) || string.IsNullOrWhiteSpace(p.DescVi))
                    continue;

                var needName = string.IsNullOrWhiteSpace(GetNameForLang(p, lang))
                               || (lang == "en" && string.Equals(p.NameEn?.Trim(), p.NameVi.Trim(), StringComparison.Ordinal));
                var needDesc = string.IsNullOrWhiteSpace(GetDescForLang(p, lang))
                               || (lang == "en" && string.Equals(p.DescEn?.Trim(), p.DescVi.Trim(), StringComparison.Ordinal));

                if (!needName && !needDesc)
                    continue;

                try
                {
                    if (needName)
                    {
                        var t = await MyMemoryTranslateAsync(_httpClient, p.NameVi, lang);
                        if (!string.IsNullOrWhiteSpace(t))
                            SetNameForLang(p, lang, t);
                    }

                    if (needDesc)
                    {
                        var t = await MyMemoryTranslateAsync(_httpClient, p.DescVi, lang);
                        if (!string.IsNullOrWhiteSpace(t))
                            SetDescForLang(p, lang, t);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Translate] POI {p.Id} ({lang}): {ex.Message}");
                }

                if (i < places.Count - 1)
                    await Task.Delay(450);
            }
        }

        /// <summary>Lấy tên theo mã ngôn ngữ (field tương ứng trên POI).</summary>
        private static string? GetNameForLang(TouristPlace p, string lang) => lang switch
        {
            "en" => p.NameEn,
            "ja" => p.NameJa,
            "ko" => p.NameKo,
            "zh" => p.NameZh,
            _ => null
        };

        /// <summary>Lấy mô tả theo mã ngôn ngữ.</summary>
        private static string? GetDescForLang(TouristPlace p, string lang) => lang switch
        {
            "en" => p.DescEn,
            "ja" => p.DescJa,
            "ko" => p.DescKo,
            "zh" => p.DescZh,
            _ => null
        };

        /// <summary>Gán tên đã dịch vào field phù hợp.</summary>
        private static void SetNameForLang(TouristPlace p, string lang, string value)
        {
            switch (lang)
            {
                case "en": p.NameEn = value; break;
                case "ja": p.NameJa = value; break;
                case "ko": p.NameKo = value; break;
                case "zh": p.NameZh = value; break;
            }
        }

        /// <summary>Gán mô tả đã dịch vào field phù hợp.</summary>
        private static void SetDescForLang(TouristPlace p, string lang, string value)
        {
            switch (lang)
            {
                case "en": p.DescEn = value; break;
                case "ja": p.DescJa = value; break;
                case "ko": p.DescKo = value; break;
                case "zh": p.DescZh = value; break;
            }
        }

        /// <summary>Gọi API MyMemory (vi→target) cho một chuỗi.</summary>
        private static async Task<string?> MyMemoryTranslateAsync(HttpClient http, string sourceText, string targetLang)
        {
            if (string.IsNullOrWhiteSpace(sourceText)) return sourceText;
            if (targetLang is not ("en" or "ja" or "ko" or "zh")) return sourceText;

            var url =
                $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(sourceText)}&langpair=vi|{Uri.EscapeDataString(targetLang)}";
            try
            {
                var json = await http.GetStringAsync(url);
                var node = JsonNode.Parse(json);
                if (node?["responseStatus"]?.GetValue<int>() != 200)
                    return null;
                return node["responseData"]?["translatedText"]?.GetValue<string>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MyMemory] {ex.Message}");
                return null;
            }
        }

        /// <summary>Ánh xạ DTO JSON API → entity app.</summary>
        private static TouristPlace MapFromApi(ApiPoi p)
        {
            return new TouristPlace
            {
                Id = p.Id,
                NameVi = p.NameVi ?? "",
                NameEn = p.NameEn ?? "",
                NameJa = p.NameJa ?? "",
                NameKo = p.NameKo ?? "",
                NameZh = p.NameZh ?? "",
                DescVi = p.DescVi ?? "",
                DescEn = p.DescEn ?? "",
                DescJa = p.DescJa ?? "",
                DescKo = p.DescKo ?? "",
                DescZh = p.DescZh ?? "",
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                Radius = p.Radius <= 0 ? 50 : p.Radius,
                ImagePath = p.ImagePath ?? "",
                AudioUrl = string.IsNullOrWhiteSpace(p.AudioUrl) ? null : p.AudioUrl.Trim(),
                Priority = p.Priority,
                MapLink = string.IsNullOrWhiteSpace(p.MapLink) ? null : p.MapLink.Trim(),
                QrImagePath = string.IsNullOrWhiteSpace(p.QrImagePath) ? null : p.QrImagePath.Trim(),
                Price = p.Price < 0 ? 0 : p.Price,
                Tag = string.IsNullOrWhiteSpace(p.Tag) ? "dia diem du lich" : p.Tag.Trim()
            };
        }

        /// <summary>Đọc <c>extra_places.json</c> từ package nếu API không có dữ liệu.</summary>
        private static async Task<List<TouristPlace>> LoadLocalFallbackAsync()
        {
            try
            {
                using var stream = await FileSystem.OpenAppPackageFileAsync("extra_places.json");
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var places = JsonSerializer.Deserialize<List<TouristPlace>>(json, options) ?? new List<TouristPlace>();
                foreach (var p in places)
                {
                    p.NameEn ??= "";
                    p.DescEn ??= "";
                    p.NameJa ??= "";
                    p.DescJa ??= "";
                    p.NameKo ??= "";
                    p.DescKo ??= "";
                    p.NameZh ??= "";
                    p.DescZh ??= "";
                    p.QrImagePath = string.IsNullOrWhiteSpace(p.QrImagePath) ? "" : p.QrImagePath.Trim();
                    p.Tag = string.IsNullOrWhiteSpace(p.Tag) ? "dia diem du lich" : p.Tag.Trim();
                    if (p.Price < 0) p.Price = 0;
                }
                return places;
            }
            catch
            {
                return new List<TouristPlace>();
            }
        }
}

/// <summary>Dạng JSON một POI từ API public (khớp tên thuộc tính server).</summary>
internal sealed class ApiPoi
{
    public int Id { get; set; }
    public string? NameVi { get; set; }
    public string? NameEn { get; set; }
    public string? NameJa { get; set; }
    public string? NameKo { get; set; }
    public string? NameZh { get; set; }
    public string? DescVi { get; set; }
    public string? DescEn { get; set; }
    public string? DescJa { get; set; }
    public string? DescKo { get; set; }
    public string? DescZh { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Radius { get; set; }
    public string? ImagePath { get; set; }
    public string? AudioUrl { get; set; }
    public int Priority { get; set; }
    public string? MapLink { get; set; }
    public string? QrImagePath { get; set; }
    public decimal Price { get; set; }
    public string? Tag { get; set; }
}