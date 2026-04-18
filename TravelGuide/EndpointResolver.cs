using Microsoft.Maui.Devices;

namespace TravelGuide;

internal static class EndpointResolver
{
    private const string ApiBaseLoopback = "http://127.0.0.1:5096";
    private const string ApiBaseAndroidEmulator = "http://10.0.2.2:5096";
    private const string AdminBaseLoopback = "http://127.0.0.1:5280";
    private const string AdminBaseAndroidEmulator = "http://10.0.2.2:5280";

    /// <summary>
    /// URL API mặc định khi chưa có Preferences — không đọc <c>api_base_url</c>.
    /// Emulator: <c>10.0.2.2</c>; thiết bị thật: biến môi trường <c>TRAVELGUIDE_API_BASE_URL</c> (vd <c>http://192.168.1.10:5096</c>), nếu không có vẫn dùng 10.0.2.2 (sai trên máy thật → cần set env hoặc Preferences).
    /// </summary>
    internal static string GetDefaultApiBaseUrlForCurrentPlatform()
    {
        if (DeviceInfo.Platform != DevicePlatform.Android)
            return ApiBaseLoopback;

        if (DeviceInfo.DeviceType == DeviceType.Virtual)
            return ApiBaseAndroidEmulator;

        var env = Environment.GetEnvironmentVariable("TRAVELGUIDE_API_BASE_URL")?.Trim();
        if (!string.IsNullOrWhiteSpace(env)
            && Uri.TryCreate(env, UriKind.Absolute, out var u)
            && (string.Equals(u.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(u.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            return u.ToString().TrimEnd('/');

        return ApiBaseAndroidEmulator;
    }

    /// <summary>Điện thoại thật không thể dùng host emulator <c>10.0.2.2</c>; bỏ qua pref cũ để dùng env / mặc định.</summary>
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

    internal static string ResolveApiBaseUrl()
    {
        var defaultUrl = GetDefaultApiBaseUrlForCurrentPlatform();

        var tourist = Preferences.Get("tourist_api_base_url", "")?.Trim();
        if (Uri.TryCreate(tourist, UriKind.Absolute, out var touristUri) && !IsAndroidPhysicalWithEmulatorHost(touristUri))
            return touristUri.ToString().TrimEnd('/');

        var api = Preferences.Get("api_base_url", "")?.Trim();
        if (Uri.TryCreate(api, UriKind.Absolute, out var apiUri) && !IsAndroidPhysicalWithEmulatorHost(apiUri))
            return apiUri.ToString().TrimEnd('/');

        return defaultUrl;
    }

    internal static (string Primary, string Secondary) ResolveAdminWebBaseUrls()
    {
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
}
