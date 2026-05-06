using System.Text.Json;
using Microsoft.Maui.Devices;

namespace TravelGuide;

internal static class EndpointResolver
{
    private const string ApiBaseLoopback = "http://127.0.0.1:5096";
    private const string ApiBaseAndroidEmulator = "http://10.0.2.2:5096";
    private const string AdminBaseLoopback = "http://127.0.0.1:5280";
    private const string AdminBaseAndroidEmulator = "http://10.0.2.2:5280";
    private const string DeviceEndpointsConfigAsset = "device_endpoints.json";
    private static bool _androidPhysicalConfigLoaded;
    private static string? _androidPhysicalApiBaseUrl;
    private static string? _androidPhysicalAdminWebBaseUrl;

    /// <summary>
    /// URL TravelGuide.API mặc định khi chưa có URL trong Preferences (màn đăng nhập ghi đè khi đổi mạng).
    /// Thứ tự ưu tiên thực tế khi dùng app: Preferences từ đăng nhập → <c>device_endpoints.json</c> (máy thật; tên file hợp lệ với Android Raw)
    /// → mặc định: Android emulator <c>10.0.2.2:5096</c>, nền tảng khác <c>127.0.0.1:5096</c>.
    /// </summary>
    internal static string GetDefaultApiBaseUrl()
    {
        if (DeviceInfo.Platform != DevicePlatform.Android)
            return ApiBaseLoopback;

        if (DeviceInfo.DeviceType == DeviceType.Virtual)
            return ApiBaseAndroidEmulator;

        if (TryNormalizeHttpUrl(GetAndroidPhysicalApiBaseUrlFromConfig(), out var cfg))
            return cfg!;
        return ApiBaseLoopback;
    }

    /// <summary>Tên cũ — gọi <see cref="GetDefaultApiBaseUrl"/>.</summary>
    internal static string GetDefaultApiBaseUrlForCurrentPlatform() => GetDefaultApiBaseUrl();

    /// <summary>Điện thoại thật không thể dùng host emulator <c>10.0.2.2</c>; bỏ qua pref cũ để dùng env / file nhúng / mặc định.</summary>
    private static bool IsAndroidPhysicalWithEmulatorHost(Uri uri) =>
        DeviceInfo.Platform == DevicePlatform.Android
        && DeviceInfo.DeviceType == DeviceType.Physical
        && string.Equals(uri.Host, "10.0.2.2", StringComparison.OrdinalIgnoreCase);

    /// <summary>Chuỗi URL đã lưu là <c>10.0.2.2</c> trên máy thật → không dùng.</summary>
    internal static bool ShouldDiscardAndroidPhysicalEmulatorApiPref(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri) && IsAndroidPhysicalWithEmulatorHost(uri);
    }

    /// <summary>Ưu tiên <c>tourist_api_base_url</c> / <c>api_base_url</c> (màn đăng nhập), sau đó mặc định theo nền tảng / env / file nhúng.</summary>
    internal static string ResolveApiBaseUrl()
    {
        if (DeviceInfo.Platform == DevicePlatform.Android
            && DeviceInfo.DeviceType == DeviceType.Physical)
        {
            var candidates = new[]
            {
                Preferences.Get("tourist_api_base_url", ""),
                Preferences.Get("api_base_url", ""),
                GetAndroidPhysicalApiBaseUrlFromConfig()
            };
            var normalized = candidates
                .Select(TryNormalizeHttpUrl)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToList();
            if (normalized.Count > 0)
                return normalized[0];
            return GetDefaultApiBaseUrl();
        }

        var tourist = Preferences.Get("tourist_api_base_url", "")?.Trim();
        if (Uri.TryCreate(tourist, UriKind.Absolute, out var touristUri) && !IsAndroidPhysicalWithEmulatorHost(touristUri))
            return touristUri.ToString().TrimEnd('/');

        var api = Preferences.Get("api_base_url", "")?.Trim();
        if (Uri.TryCreate(api, UriKind.Absolute, out var apiUri) && !IsAndroidPhysicalWithEmulatorHost(apiUri))
            return apiUri.ToString().TrimEnd('/');

        return GetDefaultApiBaseUrl();
    }

    internal static (string Primary, string Secondary) ResolveAdminWebBaseUrls()
    {
        if (DeviceInfo.Platform == DevicePlatform.Android
            && DeviceInfo.DeviceType == DeviceType.Physical)
        {
            var candidates = new[]
            {
                Preferences.Get("admin_web_base_url", ""),
                GetAndroidPhysicalAdminBaseUrlFromConfig()
            };
            var normalized = candidates
                .Select(TryNormalizeHttpUrl)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToList();
            var chosen = normalized.Count > 0 ? normalized[0] : null;
            if (!string.IsNullOrWhiteSpace(chosen))
                return (chosen!, chosen!);
        }

        var configured = Preferences.Get("admin_web_base_url", "")?.Trim();
        if (Uri.TryCreate(configured, UriKind.Absolute, out var cfgUri))
        {
            var url = NormalizeAdminHostForAndroid(cfgUri.ToString().TrimEnd('/'));
            return (url, url);
        }

        var apiBase = ResolveApiBaseUrl();
        if (Uri.TryCreate(apiBase, UriKind.Absolute, out var apiUri))
        {
            var host = MapLoopbackToAndroidEmulatorHost(apiUri.Host);
            var scheme = apiUri.Scheme;
            return
            (
                $"{scheme}://{host}:5280",
                $"{scheme}://{host}:5280"
            );
        }

        var fallback = DeviceInfo.Platform == DevicePlatform.Android
            ? AdminBaseAndroidEmulator
            : AdminBaseLoopback;
        return (fallback, fallback);
    }

    /// <summary>Trên emulator Android, 127.0.0.1/localhost là chính emulator — không trỏ tới máy host.</summary>
    private static string MapLoopbackToAndroidEmulatorHost(string host)
    {
        if (DeviceInfo.Platform != DevicePlatform.Android)
            return host;
        if (string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return "10.0.2.2";
        return host;
    }

    private static string NormalizeAdminHostForAndroid(string url)
    {
        if (DeviceInfo.Platform != DevicePlatform.Android || !Uri.TryCreate(url, UriKind.Absolute, out var u))
            return url;
        var builder = new UriBuilder(u) { Host = MapLoopbackToAndroidEmulatorHost(u.Host) };
        return builder.Uri.ToString().TrimEnd('/');
    }

    private static void EnsureAndroidPhysicalEndpointsLoaded()
    {
        if (_androidPhysicalConfigLoaded)
            return;
        _androidPhysicalConfigLoaded = true;

        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync(DeviceEndpointsConfigAsset).GetAwaiter().GetResult();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("androidPhysicalApiBaseUrl", out var apiEl))
            {
                var raw = apiEl.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(raw)
                    && Uri.TryCreate(raw, UriKind.Absolute, out var apiUri)
                    && (string.Equals(apiUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(apiUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                    _androidPhysicalApiBaseUrl = apiUri.ToString().TrimEnd('/');
            }

            if (doc.RootElement.TryGetProperty("androidPhysicalAdminWebBaseUrl", out var admEl))
            {
                var rawAdm = admEl.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(rawAdm)
                    && Uri.TryCreate(rawAdm, UriKind.Absolute, out var admUri)
                    && (string.Equals(admUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(admUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
                    _androidPhysicalAdminWebBaseUrl = admUri.ToString().TrimEnd('/');
            }
        }
        catch
        {
            // giữ null
        }
    }

    private static string? GetAndroidPhysicalApiBaseUrlFromConfig()
    {
        EnsureAndroidPhysicalEndpointsLoaded();
        return _androidPhysicalApiBaseUrl;
    }

    private static string? GetAndroidPhysicalAdminBaseUrlFromConfig()
    {
        EnsureAndroidPhysicalEndpointsLoaded();
        return _androidPhysicalAdminWebBaseUrl;
    }

    private static bool TryNormalizeHttpUrl(string? raw, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri))
            return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;
        if (IsAndroidPhysicalWithEmulatorHost(uri))
            return false;
        normalized = uri.ToString().TrimEnd('/');
        return true;
    }

    private static string? TryNormalizeHttpUrl(string? raw) =>
        TryNormalizeHttpUrl(raw, out var normalized) ? normalized : null;

}
