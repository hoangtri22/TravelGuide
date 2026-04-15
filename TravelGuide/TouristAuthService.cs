using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TravelGuide;

public sealed class TouristAuthService
{
    private readonly HttpClient _httpClient;
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(15);
    private const string AuthBaseLoopback = "http://127.0.0.1:5096";
    private const string AuthBaseAndroid = "http://10.0.2.2:5096";
    private const string TokenKey = "tourist_token";
    private const string UsernameKey = "tourist_username";
    private const string TierKey = "tourist_account_tier";

    public TouristAuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string GetCurrentApiBaseUrl()
    {
        var defaultUrl = DeviceInfo.Platform == DevicePlatform.Android
            ? AuthBaseAndroid
            : AuthBaseLoopback;

        var configured = Preferences.Get("tourist_api_base_url", defaultUrl)?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
        {
            Preferences.Set("tourist_api_base_url", defaultUrl);
            return defaultUrl;
        }

        if (ShouldForceAdminBase(configured))
        {
            Preferences.Set("tourist_api_base_url", defaultUrl);
            System.Diagnostics.Debug.WriteLine($"[AuthAPI] Reset tourist_api_base_url '{configured}' -> '{defaultUrl}'");
            return defaultUrl;
        }

        return configured;
    }

    public async Task<(bool Ok, string Message)> RegisterAsync(string username, string password, string displayName, string accountTier)
    {
        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/auth/register";
            using var cts = new CancellationTokenSource(ApiTimeout);
            var response = await _httpClient.PostAsJsonAsync(url, new
            {
                username,
                password,
                displayName,
                accountTier
            }, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var error = (await response.Content.ReadAsStringAsync()).Trim();
                return (false, string.IsNullOrWhiteSpace(error) ? "Đăng ký thất bại." : error);
            }
            return (true, "Đăng ký thành công.");
        }
        catch (OperationCanceledException)
        {
            var baseUrl = GetCurrentApiBaseUrl().TrimEnd('/');
            return (false, $"Request timeout when calling API ({baseUrl}).");
        }
        catch (Exception ex)
        {
            var baseUrl = GetCurrentApiBaseUrl().TrimEnd('/');
            return (false, $"Cannot connect to API ({baseUrl}). Error: {ex.Message}");
        }
    }

    public async Task<(bool Ok, string Message)> LoginAsync(string username, string password)
    {
        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/auth/login";
            using var cts = new CancellationTokenSource(ApiTimeout);
            var response = await _httpClient.PostAsJsonAsync(url, new { username, password }, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var error = (await response.Content.ReadAsStringAsync()).Trim();
                return (false, string.IsNullOrWhiteSpace(error) ? "Đăng nhập thất bại." : error);
            }

            var dto = await response.Content.ReadFromJsonAsync<TouristLoginResponse>();
            if (dto is null || string.IsNullOrWhiteSpace(dto.Token))
                return (false, "Phản hồi đăng nhập không hợp lệ.");

            await SecureStorage.Default.SetAsync(TokenKey, dto.Token);
            Preferences.Set(UsernameKey, dto.Username ?? username);
            Preferences.Set(TierKey, string.IsNullOrWhiteSpace(dto.AccountTier) ? "free" : dto.AccountTier);
            return (true, "Đăng nhập thành công.");
        }
        catch (OperationCanceledException)
        {
            var baseUrl = GetCurrentApiBaseUrl().TrimEnd('/');
            return (false, $"Request timeout when calling API ({baseUrl}).");
        }
        catch (Exception ex)
        {
            var baseUrl = GetCurrentApiBaseUrl().TrimEnd('/');
            return (false, $"Cannot connect to API ({baseUrl}). Error: {ex.Message}");
        }
    }

    public async Task<bool> IsLoggedInAsync()
    {
        var token = await SecureStorage.Default.GetAsync(TokenKey);
        return !string.IsNullOrWhiteSpace(token);
    }

    public async Task<(bool Ok, string Username, string AccountTier)> GetMeAsync()
    {
        var token = await SecureStorage.Default.GetAsync(TokenKey);
        if (string.IsNullOrWhiteSpace(token))
            return (false, "", "");

        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/auth/me";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode)
                return (false, "", "");

            var dto = await response.Content.ReadFromJsonAsync<TouristMeResponse>();
            if (dto is null) return (false, "", "");

            var username = dto.Username ?? Preferences.Get(UsernameKey, "");
            var tier = dto.AccountTier ?? Preferences.Get(TierKey, "free");
            Preferences.Set(UsernameKey, username);
            Preferences.Set(TierKey, tier);
            return (true, username, tier);
        }
        catch
        {
            return (false, "", "");
        }
    }

    public void Logout()
    {
        SecureStorage.Default.Remove(TokenKey);
        Preferences.Remove(UsernameKey);
        Preferences.Remove(TierKey);
    }

    /// <summary>Kích hoạt Premium bằng mã trong QR (sau khi người dùng xác nhận thanh toán mô phỏng trên app).</summary>
    public async Task<(bool Ok, string Message)> RedeemPremiumClaimAsync(string claimCode, decimal amountVnd)
    {
        var token = await SecureStorage.Default.GetAsync(TokenKey);
        if (string.IsNullOrWhiteSpace(token))
            return (false, "Vui lòng đăng nhập du khách trước.");

        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/premium/redeem";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new { claimCode, amountVnd })
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(req);
            var text = (await response.Content.ReadAsStringAsync()).Trim();
            if (!response.IsSuccessStatusCode)
                return (false, string.IsNullOrWhiteSpace(text) ? "Kích hoạt thất bại." : text);

            Preferences.Set(TierKey, "premium");
            return (true, string.IsNullOrWhiteSpace(text) ? "Đã kích hoạt Premium." : text);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Ok, bool HasAccess, bool RequiresPurchase, decimal PriceVnd, string Message)> GetPoiAccessAsync(int poiId)
    {
        var token = await SecureStorage.Default.GetAsync(TokenKey);
        if (string.IsNullOrWhiteSpace(token))
            return (false, false, true, 0, "Chưa đăng nhập.");

        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/pois/{poiId}/access";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode)
                return (false, false, true, 0, "Không kiểm tra được quyền truy cập.");

            var dto = await response.Content.ReadFromJsonAsync<PoiAccessResponse>();
            if (dto is null) return (false, false, true, 0, "Phản hồi không hợp lệ.");
            return (true, dto.HasAccess, dto.RequiresPurchase, dto.PriceVnd, "");
        }
        catch (Exception ex)
        {
            return (false, false, true, 0, ex.Message);
        }
    }

    public async Task<(bool Ok, string Message)> ConfirmPoiPurchaseAsync(int poiId, decimal amountVnd)
    {
        var token = await SecureStorage.Default.GetAsync(TokenKey);
        if (string.IsNullOrWhiteSpace(token))
            return (false, "Vui lòng đăng nhập.");

        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/pois/{poiId}/purchase-confirm";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new { amountVnd })
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(req);
            var text = (await response.Content.ReadAsStringAsync()).Trim();
            if (!response.IsSuccessStatusCode)
                return (false, string.IsNullOrWhiteSpace(text) ? "Xác nhận thất bại." : text);
            return (true, string.IsNullOrWhiteSpace(text) ? "Đã ghi nhận." : text);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Ghi log quét QR mở POI (lịch sử + doanh thu trên Admin Web).</summary>
    public async Task LogPoiQrScanAsync(
        int poiId,
        string? poiNameVi,
        string eventType,
        decimal amountVnd,
        string? deviceId,
        string? deviceModel,
        string? appPlatform)
    {
        var token = await SecureStorage.Default.GetAsync(TokenKey);
        if (string.IsNullOrWhiteSpace(token)) return;

        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/pois/scan-log";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new
                {
                    poiId,
                    poiNameVi,
                    eventType,
                    amountVnd,
                    deviceId,
                    deviceModel,
                    appPlatform
                })
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(req);
            _ = await response.Content.ReadAsStringAsync();
        }
        catch
        {
            // Không chặn UX nếu log lỗi.
        }
    }

    /// <summary>Lấy lịch quét (POI đã mở qua QR) để mở lại không cần quét.</summary>
    public async Task<(bool Ok, IReadOnlyList<MyScanHistoryRow> Items, string Message)> GetMyScanHistoryAsync()
    {
        var token = await SecureStorage.Default.GetAsync(TokenKey);
        if (string.IsNullOrWhiteSpace(token))
            return (false, Array.Empty<MyScanHistoryRow>(), "Chưa đăng nhập.");

        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/pois/my-scan-history";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode)
            {
                var text = (await response.Content.ReadAsStringAsync()).Trim();
                return (false, Array.Empty<MyScanHistoryRow>(), string.IsNullOrWhiteSpace(text) ? "Không tải được lịch quét." : text);
            }

            var items = await response.Content.ReadFromJsonAsync<List<MyScanHistoryRow>>();
            return (true, items ?? new List<MyScanHistoryRow>(), "");
        }
        catch (Exception ex)
        {
            return (false, Array.Empty<MyScanHistoryRow>(), ex.Message);
        }
    }

    public sealed class MyScanHistoryRow
    {
        [JsonPropertyName("poiId")] public int PoiId { get; set; }
        [JsonPropertyName("poiNameVi")] public string? PoiNameVi { get; set; }
        [JsonPropertyName("eventType")] public string? EventType { get; set; }
        [JsonPropertyName("amountVnd")] public decimal AmountVnd { get; set; }
        [JsonPropertyName("lastScannedAtUtc")] public DateTime LastScannedAtUtc { get; set; }
    }

    private static bool ShouldForceAdminBase(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return true;

        // Login endpoint hiện nằm ở TravelGuide.API (5096/7149), không nằm ở AdminWeb (5280/7145).
        return uri.Port is 5280 or 7145;
    }

    private sealed class TouristLoginResponse
    {
        public string? Token { get; set; }
        public string? Username { get; set; }
        public string? AccountTier { get; set; }
    }

    private sealed class TouristMeResponse
    {
        public string? Username { get; set; }
        public string? AccountTier { get; set; }
    }

    private sealed class PoiAccessResponse
    {
        [JsonPropertyName("hasAccess")]
        public bool HasAccess { get; set; }

        [JsonPropertyName("requiresPurchase")]
        public bool RequiresPurchase { get; set; }

        [JsonPropertyName("priceVnd")]
        public decimal PriceVnd { get; set; }
    }
}
