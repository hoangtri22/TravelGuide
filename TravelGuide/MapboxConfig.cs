namespace TravelGuide;

/// <summary>
/// Cấu hình Mapbox GL cho WebView bản đồ (<see cref="MapPage"/>).
/// Thứ tự: <see cref="Preferences"/> (key <see cref="PreferencesKey"/>), sau đó biến môi trường <c>MAPBOX_ACCESS_TOKEN</c>.
/// Không commit token vào git; token lộ nên revoke trên dashboard Mapbox.
/// </summary>
public static class MapboxConfig
{
    /// <summary>Khóa Preferences dùng để lưu Mapbox access token.</summary>
    public const string PreferencesKey = "mapbox_access_token";

    /// <summary>Tên biến môi trường (dev/CI) — ưu tiên sau Preferences nếu Preferences trống.</summary>
    public const string EnvironmentVariableName = "MAPBOX_ACCESS_TOKEN";

    /// <summary>Trả về token đã trim; chuỗi rỗng nếu chưa cấu hình.</summary>
    public static string GetAccessToken()
    {
        var fromPrefs = Preferences.Get(PreferencesKey, string.Empty)?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(fromPrefs))
            return fromPrefs;

        var fromEnv = Environment.GetEnvironmentVariable(EnvironmentVariableName)?.Trim() ?? string.Empty;
        return fromEnv;
    }

    /// <summary>True khi có token hợp lệ để tải Mapbox GL.</summary>
    public static bool HasAccessToken() => !string.IsNullOrWhiteSpace(GetAccessToken());
}
