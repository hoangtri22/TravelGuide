using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Maui.Devices;
using TravelGuide.Models;

namespace TravelGuide;

public sealed class TouristAuthService
{
    private readonly HttpClient _httpClient;
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(15);
    private const string TokenKey = "tourist_token";
    private const string UsernameKey = "tourist_username";
    private const string TierKey = "tourist_account_tier";
    private const string DeviceInstallIdKey = "tg_device_install_id";
    private const string LastAppOpenReportMsKey = "tourist_last_app_open_report_ms";
    private const int AppOpenReportThrottleMs = 3 * 60 * 1000;

    public TouristAuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string GetCurrentApiBaseUrl()
    {
        var resolved = EndpointResolver.ResolveApiBaseUrl().TrimEnd('/');
        if (!ShouldForceAdminBase(resolved) && !ShouldResetStaleLanBase(resolved))
            return resolved;

        var defaultUrl = EndpointResolver.GetDefaultApiBaseUrl();
        Preferences.Set("tourist_api_base_url", defaultUrl);
        Preferences.Set("api_base_url", defaultUrl);
        System.Diagnostics.Debug.WriteLine($"[AuthAPI] Reset invalid API URL '{resolved}' -> '{defaultUrl}'");
        return defaultUrl;
    }

    public bool TrySetApiBaseUrl(string rawUrl, out string normalizedUrl, out string error)
    {
        normalizedUrl = string.Empty;
        error = string.Empty;

        var input = NormalizeApiBaseUrlInput((rawUrl ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Vui lòng nhập API URL.";
            return false;
        }

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            error = "API URL không hợp lệ. Ví dụ: http://192.168.1.16:5096";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "Chỉ hỗ trợ http/https.";
            return false;
        }

        if (ShouldForceAdminBase(uri.ToString()))
        {
            error = "URL này đang là cổng AdminWeb, vui lòng dùng API port (ví dụ :5096).";
            return false;
        }

        normalizedUrl = uri.ToString().TrimEnd('/');
        Preferences.Set("tourist_api_base_url", normalizedUrl);
        Preferences.Set("api_base_url", normalizedUrl);
        return true;
    }

    /// <summary>
    /// Trên Android emulator, <c>127.0.0.1</c>/<c>localhost</c> là chính emulator — không tới máy host.
    /// Tự đổi sang <c>10.0.2.2</c> (alias host) để đăng nhập/API không bị treo.
    /// </summary>
    public void EnsureValidApiUrlForCurrentPlatform()
    {
        if (DeviceInfo.Platform != DevicePlatform.Android)
            return;

        var resolved = EndpointResolver.ResolveApiBaseUrl();
        if (!Uri.TryCreate(resolved, UriKind.Absolute, out var uri))
            return;

        var host = uri.Host;
        if (!string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return;

        var port = uri.IsDefaultPort ? 5096 : uri.Port;
        var scheme = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? Uri.UriSchemeHttps
            : Uri.UriSchemeHttp;
        TrySetApiBaseUrl($"{scheme}://10.0.2.2:{port}", out _, out _);
    }

    private static string GetOrCreateDeviceInstallId()
    {
        var existing = Preferences.Get(DeviceInstallIdKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;

        var generated = Guid.NewGuid().ToString("N");
        Preferences.Set(DeviceInstallIdKey, generated);
        return generated;
    }

    private static string GetDeviceDisplayName()
    {
        var name = (DeviceInfo.Current.Name ?? string.Empty).Trim();
        if (name.Length > 0) return name;

        var model = (DeviceInfo.Current.Model ?? string.Empty).Trim();
        if (model.Length > 0) return model;

        return $"{DeviceInfo.Current.Platform}-{DeviceInfo.Current.Idiom}";
    }

    private async Task<(bool Ok, string Token, string Username, string AccountTier, string Message)> LoginByDeviceAsync()
    {
        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/auth/device-login";
            using var cts = new CancellationTokenSource(ApiTimeout);
            var payload = new
            {
                deviceId = GetOrCreateDeviceInstallId(),
                deviceName = GetDeviceDisplayName(),
                appPlatform = DeviceInfo.Current.Platform.ToString()
            };
            using var response = await _httpClient.PostAsJsonAsync(url, payload, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var error = (await response.Content.ReadAsStringAsync()).Trim();
                RecordHttpError($"POST {url} -> {(int)response.StatusCode} {response.StatusCode}. {error}");
                return (false, string.Empty, string.Empty, string.Empty, string.IsNullOrWhiteSpace(error) ? "Không thể khởi tạo phiên thiết bị." : error);
            }

            var dto = await response.Content.ReadFromJsonAsync<DeviceLoginResponse>();
            if (dto is null || string.IsNullOrWhiteSpace(dto.Token))
                return (false, string.Empty, string.Empty, string.Empty, "Phản hồi phiên thiết bị không hợp lệ.");

            var username = string.IsNullOrWhiteSpace(dto.Username) ? GetDeviceDisplayName() : dto.Username.Trim();
            var tier = string.IsNullOrWhiteSpace(dto.AccountTier) ? "free" : dto.AccountTier.Trim();

            await SecureStorage.Default.SetAsync(TokenKey, dto.Token.Trim());
            Preferences.Set(UsernameKey, username);
            Preferences.Set(TierKey, tier);
            return (true, dto.Token.Trim(), username, tier, string.Empty);
        }
        catch (OperationCanceledException)
        {
            var baseUrl = GetCurrentApiBaseUrl().TrimEnd('/');
            RecordHttpError($"POST {baseUrl}/api/tourist/auth/device-login -> timeout");
            return (false, string.Empty, string.Empty, string.Empty, $"Request timeout when calling API ({baseUrl}).");
        }
        catch (Exception ex)
        {
            var baseUrl = GetCurrentApiBaseUrl().TrimEnd('/');
            RecordHttpError($"POST {baseUrl}/api/tourist/auth/device-login -> {ex.Message}");
            return (false, string.Empty, string.Empty, string.Empty, $"Cannot connect to API ({baseUrl}). Error: {ex.Message}");
        }
    }

    private async Task<(bool Ok, string Token, string Username, string AccountTier, string Message)> EnsureTokenAsync()
    {
        var token = await SecureStorage.Default.GetAsync(TokenKey);
        if (!string.IsNullOrWhiteSpace(token))
        {
            var username = Preferences.Get(UsernameKey, GetDeviceDisplayName());
            var tier = Preferences.Get(TierKey, "free");
            return (true, token.Trim(), username, tier, string.Empty);
        }

        return await LoginByDeviceAsync();
    }

    public async Task<bool> IsLoggedInAsync()
    {
        var me = await GetMeAsync();
        return me.Ok;
    }

    public async Task<(bool Ok, string Username, string AccountTier)> GetMeAsync()
    {
        var ensured = await EnsureTokenAsync();
        if (!ensured.Ok)
            return (false, "", "");

        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/auth/me";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ensured.Token);
            using var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode && response.StatusCode == HttpStatusCode.Unauthorized)
            {
                SecureStorage.Default.Remove(TokenKey);
                var relogin = await LoginByDeviceAsync();
                if (!relogin.Ok) return (false, "", "");

                using var retryReq = new HttpRequestMessage(HttpMethod.Get, url);
                retryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", relogin.Token);
                using var retryResponse = await _httpClient.SendAsync(retryReq);
                if (!retryResponse.IsSuccessStatusCode)
                    return (false, "", "");

                var retryDto = await retryResponse.Content.ReadFromJsonAsync<TouristMeResponse>();
                if (retryDto is null) return (false, "", "");
                var retryUsername = retryDto.Username ?? relogin.Username;
                var retryTier = retryDto.AccountTier ?? relogin.AccountTier;
                Preferences.Set(UsernameKey, retryUsername);
                Preferences.Set(TierKey, retryTier);
                return (true, retryUsername, retryTier);
            }

            if (!response.IsSuccessStatusCode)
            {
                RecordHttpError($"GET {url} -> {(int)response.StatusCode} {response.StatusCode}");
                return (false, "", "");
            }

            var dto = await response.Content.ReadFromJsonAsync<TouristMeResponse>();
            if (dto is null) return (false, "", "");

            var username = dto.Username ?? ensured.Username;
            var tier = dto.AccountTier ?? ensured.AccountTier;
            Preferences.Set(UsernameKey, username);
            Preferences.Set(TierKey, tier);
            return (true, username, tier);
        }
        catch
        {
            RecordHttpError($"GET /api/tourist/auth/me -> exception");
            return (false, "", "");
        }
    }

    /// <summary>Đăng xuất: thu hồi phiên trên API (RefreshToken) rồi xóa token cục bộ.</summary>
    public async Task LogoutAsync()
    {
        string? bearer = null;
        try
        {
            bearer = await SecureStorage.Default.GetAsync(TokenKey);
        }
        catch
        {
            // ignore
        }

        if (!string.IsNullOrWhiteSpace(bearer))
        {
            try
            {
                using var cts = new CancellationTokenSource(ApiTimeout);
                var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/auth/logout";
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer.Trim());
                await _httpClient.SendAsync(req, cts.Token);
            }
            catch
            {
                // vẫn xóa local
            }
        }

        try
        {
            SecureStorage.Default.Remove(TokenKey);
        }
        catch { /* ignore */ }

        try
        {
            Preferences.Remove(UsernameKey);
            Preferences.Remove(TierKey);
        }
        catch { /* ignore */ }
    }

    /// <summary>Kích hoạt Premium bằng mã trong QR (sau khi người dùng xác nhận thanh toán mô phỏng trên app).</summary>
    public async Task<(bool Ok, string Message)> RedeemPremiumClaimAsync(string claimCode, decimal amountVnd)
    {
        var auth = await EnsureTokenAsync();
        if (!auth.Ok)
            return (false, auth.Message);

        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/premium/redeem";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new { claimCode, amountVnd })
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.Token);
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
        var auth = await EnsureTokenAsync();
        if (!auth.Ok)
            return (false, false, true, 0, auth.Message);

        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/pois/{poiId}/access";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.Token);
            using var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode)
            {
                RecordHttpError($"GET {url} -> {(int)response.StatusCode} {response.StatusCode}");
                return (false, false, true, 0, "Không kiểm tra được quyền truy cập.");
            }

            var dto = await response.Content.ReadFromJsonAsync<PoiAccessResponse>();
            if (dto is null) return (false, false, true, 0, "Phản hồi không hợp lệ.");
            return (true, dto.HasAccess, dto.RequiresPurchase, dto.PriceVnd, "");
        }
        catch (Exception ex)
        {
            RecordHttpError($"GET /api/tourist/pois/{poiId}/access -> {ex.Message}");
            return (false, false, true, 0, ex.Message);
        }
    }

    public async Task<(bool Ok, string Message)> ConfirmPoiPurchaseAsync(int poiId, decimal amountVnd)
    {
        var auth = await EnsureTokenAsync();
        if (!auth.Ok)
            return (false, auth.Message);

        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/pois/{poiId}/purchase-confirm";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new { amountVnd })
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.Token);
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

    /// <summary>Gửi tối đa một lần / ~3 phút để admin đếm lượt mở app (không chặn UX).</summary>
    public void ReportAppOpenIfNeeded()
    {
        _ = ReportAppOpenIfNeededCoreAsync();
    }

    private async Task ReportAppOpenIfNeededCoreAsync()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var lastMs = Preferences.Get(LastAppOpenReportMsKey, 0L);
        if (nowMs - lastMs < AppOpenReportThrottleMs)
            return;

        var auth = await EnsureTokenAsync();
        if (!auth.Ok)
            return;

        try
        {
            using var cts = new CancellationTokenSource(ApiTimeout);
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/analytics/app-open";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
            using var response = await _httpClient.SendAsync(req, cts.Token);
            if (response.IsSuccessStatusCode)
                Preferences.Set(LastAppOpenReportMsKey, nowMs);
        }
        catch
        {
            // ignore — thống kê không chặn app
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
        var auth = await EnsureTokenAsync();
        if (!auth.Ok) return;

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
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.Token);
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
        var auth = await EnsureTokenAsync();
        if (!auth.Ok)
            return (false, Array.Empty<MyScanHistoryRow>(), auth.Message);

        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/pois/my-scan-history";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.Token);
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

    /// <summary>Lấy dữ liệu heatmap GPS-only theo POI (eventType: poi_gps_inside).</summary>
    public async Task<(bool Ok, IReadOnlyList<GpsHeatmapRow> Items, string Message)> GetGpsHeatmapAsync(int minutes = 90)
    {
        var auth = await EnsureTokenAsync();
        if (!auth.Ok)
            return (false, Array.Empty<GpsHeatmapRow>(), auth.Message);

        try
        {
            var clamped = Math.Clamp(minutes, 1, 1440);
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/pois/gps-heatmap?minutes={clamped}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
            using var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode)
            {
                var text = (await response.Content.ReadAsStringAsync()).Trim();
                return (false, Array.Empty<GpsHeatmapRow>(), string.IsNullOrWhiteSpace(text) ? "Không tải được heatmap GPS." : text);
            }

            var items = await response.Content.ReadFromJsonAsync<List<GpsHeatmapRow>>();
            return (true, items ?? new List<GpsHeatmapRow>(), "");
        }
        catch (Exception ex)
        {
            return (false, Array.Empty<GpsHeatmapRow>(), ex.Message);
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

    public sealed class GpsHeatmapRow
    {
        [JsonPropertyName("poiId")] public int PoiId { get; set; }
        [JsonPropertyName("poiNameVi")] public string? PoiNameVi { get; set; }
        [JsonPropertyName("gpsHits")] public int GpsHits { get; set; }
    }

    public async Task<(bool Ok, string Message)> SubmitPlaceReviewAsync(int poiId, string? poiNameVi, int rating, string content)
    {
        var auth = await EnsureTokenAsync();
        if (!auth.Ok)
            return (false, auth.Message);

        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/comments";
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new
                {
                    poiId,
                    poiNameVi,
                    rating,
                    content
                })
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.Token);
            using var response = await _httpClient.SendAsync(req);
            var text = (await response.Content.ReadAsStringAsync()).Trim();
            if (!response.IsSuccessStatusCode)
                return (false, string.IsNullOrWhiteSpace(text) ? "Gửi đánh giá thất bại." : text);
            return (true, "Đã gửi đánh giá.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<(bool Ok, IReadOnlyList<TouristPlaceReview> Items, string Message)> GetPlaceReviewsAsync(int poiId, int take = 100)
    {
        var token = await SecureStorage.Default.GetAsync(TokenKey);
        var clamped = Math.Clamp(take, 1, 500);
        string? apiErr = null;
        List<ApiTouristComment>? apiRows = null;

        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/comments/{poiId}?take={clamped}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(req);
            if (response.IsSuccessStatusCode)
                apiRows = await response.Content.ReadFromJsonAsync<List<ApiTouristComment>>();
            else
                apiErr = (await response.Content.ReadAsStringAsync()).Trim();
        }
        catch (Exception ex)
        {
            apiErr = ex.Message;
        }

        var adminRows = await TryFetchCommentsFromAdminPublicAsync(poiId, clamped);
        var merged = MergeCommentRows(apiRows, adminRows);
        var items = merged.Select(x => new TouristPlaceReview
        {
            Id = x.Id > int.MaxValue ? int.MaxValue : (int)Math.Max(0, x.Id),
            PoiId = x.PoiId,
            Username = x.Username ?? "",
            Rating = Math.Clamp(x.Rating, 1, 5),
            Content = x.Content ?? "",
            CreatedAtUtc = x.CreatedAtUtc
        }).ToList();

        if (items.Count == 0 && !string.IsNullOrEmpty(apiErr) && adminRows is null)
            return (false, Array.Empty<TouristPlaceReview>(), string.IsNullOrWhiteSpace(apiErr) ? "Không tải được bình luận." : apiErr);

        return (true, items, "");
    }

    public async Task<(bool Ok, IReadOnlyList<PlaceReviewWithAdminReply> Items, string Message)> GetPlaceReviewsWithAdminReplyAsync(int poiId, int take = 100)
    {
        var token = await SecureStorage.Default.GetAsync(TokenKey);
        var clamped = Math.Clamp(take, 1, 500);
        string? apiErr = null;
        List<ApiTouristComment>? apiRows = null;

        try
        {
            var url = $"{GetCurrentApiBaseUrl().TrimEnd('/')}/api/tourist/comments/{poiId}?take={clamped}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(req);
            if (response.IsSuccessStatusCode)
                apiRows = await response.Content.ReadFromJsonAsync<List<ApiTouristComment>>();
            else
                apiErr = (await response.Content.ReadAsStringAsync()).Trim();
        }
        catch (Exception ex)
        {
            apiErr = ex.Message;
        }

        var adminRows = await TryFetchCommentsFromAdminPublicAsync(poiId, clamped);
        var merged = MergeCommentRows(apiRows, adminRows);
        var items = merged.Select(x => new PlaceReviewWithAdminReply
        {
            Id = x.Id,
            PoiId = x.PoiId,
            Username = x.Username ?? "",
            Rating = Math.Clamp(x.Rating, 1, 5),
            Content = x.Content ?? "",
            AdminReply = x.AdminReply ?? "",
            Status = x.Status ?? "",
            CreatedAtUtc = x.CreatedAtUtc
        }).ToList();

        if (items.Count == 0 && !string.IsNullOrEmpty(apiErr) && adminRows is null)
            return (false, Array.Empty<PlaceReviewWithAdminReply>(), string.IsNullOrWhiteSpace(apiErr) ? "Không tải được bình luận." : apiErr);

        return (true, items, "");
    }

    private async Task<List<ApiTouristComment>?> TryFetchCommentsFromAdminPublicAsync(int poiId, int take)
    {
        try
        {
            var adminBase = EndpointResolver.ResolveAdminWebBaseUrls().Primary.TrimEnd('/');
            var url = $"{adminBase}/api/public/tourist-comments/{poiId}?take={take}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _httpClient.SendAsync(req);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<List<ApiTouristComment>>();
        }
        catch
        {
            return null;
        }
    }

    private static List<ApiTouristComment> MergeCommentRows(
        IReadOnlyList<ApiTouristComment>? api,
        IReadOnlyList<ApiTouristComment>? admin)
    {
        var dict = new Dictionary<string, ApiTouristComment>(StringComparer.Ordinal);
        void AddRange(IReadOnlyList<ApiTouristComment>? src)
        {
            if (src is null) return;
            foreach (var x in src)
            {
                var k = MergeDedupeKey(x);
                if (!dict.ContainsKey(k))
                    dict[k] = x;
            }
        }

        AddRange(api);
        AddRange(admin);
        return dict.Values.OrderByDescending(x => x.CreatedAtUtc).ToList();
    }

    private static string MergeDedupeKey(ApiTouristComment x)
    {
        var u = (x.Username ?? "").Trim();
        var c = (x.Content ?? "").Trim();
        return $"{u}\u001f{c}\u001f{x.CreatedAtUtc:yyyy-MM-ddTHH:mm:ss.fff}Z";
    }

    private static bool ShouldForceAdminBase(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return true;

        // Login endpoint hiện nằm ở TravelGuide.API (5096/7149), không nằm ở AdminWeb (5280/7145).
        return uri.Port is 5280 or 7145;
    }

    /// <summary>
    /// Khi bỏ UI nhập API, pref cũ (vd 192.168.1.115) có thể tồn tại dù đã đổi mạng/IP.
    /// Nếu URL hiện tại là private LAN và khác default hiện tại, reset về default để tránh kẹt kết nối cũ.
    /// </summary>
    private static bool ShouldResetStaleLanBase(string resolved)
    {
        if (!Uri.TryCreate(resolved, UriKind.Absolute, out var cur))
            return true;

        if (!IsPrivateLanHost(cur.Host))
            return false;

        var def = EndpointResolver.GetDefaultApiBaseUrl().TrimEnd('/');
        if (!Uri.TryCreate(def, UriKind.Absolute, out var defUri))
            return false;

        return !string.Equals(cur.Host, defUri.Host, StringComparison.OrdinalIgnoreCase)
               || cur.Port != defUri.Port
               || !string.Equals(cur.Scheme, defUri.Scheme, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPrivateLanHost(string host)
    {
        if (!IPAddress.TryParse(host, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;
        var b = ip.GetAddressBytes();
        if (b[0] == 10) return true;
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
        if (b[0] == 192 && b[1] == 168) return true;
        return false;
    }

    private static string NormalizeApiBaseUrlInput(string input)
    {
        return input;
    }

    private static void RecordHttpError(string message) => DemoDiagnostics.RecordHttpError(message);

    private sealed class DeviceLoginResponse
    {
        [JsonPropertyName("token")] public string? Token { get; set; }
        [JsonPropertyName("username")] public string? Username { get; set; }
        [JsonPropertyName("accountTier")] public string? AccountTier { get; set; }
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

    private sealed class ApiTouristComment
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("poiId")] public int PoiId { get; set; }
        [JsonPropertyName("username")] public string? Username { get; set; }
        [JsonPropertyName("rating")] public int Rating { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("adminReply")] public string? AdminReply { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("createdAtUtc")] public DateTime CreatedAtUtc { get; set; }
    }

    public sealed class PlaceReviewWithAdminReply
    {
        public long Id { get; set; }
        public int PoiId { get; set; }
        public string Username { get; set; } = "";
        public int Rating { get; set; }
        public string Content { get; set; } = "";
        public string AdminReply { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CreatedAtUtc { get; set; }
    }
}
