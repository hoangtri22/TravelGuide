using Microsoft.Maui.Devices;

namespace TravelGuide;

internal static class EndpointResolver
{
    private const string ApiBaseLoopback = "http://127.0.0.1:5096";
    private const string ApiBaseAndroidEmulator = "http://10.0.2.2:5096";
    private const string AdminBaseLoopback = "http://127.0.0.1:5280";
    private const string AdminBaseAndroidEmulator = "http://10.0.2.2:5280";

    internal static string ResolveApiBaseUrl()
    {
        var defaultUrl = DeviceInfo.Platform == DevicePlatform.Android
            ? ApiBaseAndroidEmulator
            : ApiBaseLoopback;

        var tourist = Preferences.Get("tourist_api_base_url", "")?.Trim();
        if (Uri.TryCreate(tourist, UriKind.Absolute, out var touristUri))
            return touristUri.ToString().TrimEnd('/');

        var api = Preferences.Get("api_base_url", "")?.Trim();
        if (Uri.TryCreate(api, UriKind.Absolute, out var apiUri))
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
