using TravelGuide.Models;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Storage;

namespace TravelGuide;

/// <summary>
/// Geofence Engine: So sánh vị trí user vs từng POI,
/// quyết định khi nào phát thuyết minh (có debounce + cooldown).
/// </summary>
public class GeofenceEngine
{
    private readonly DatabaseService _dbService;
    private readonly NarrationEngine _narrationEngine;
    private readonly TouristAuthService _authService;

    /// <summary>EventType ghi vào TouristPoiQrScanLog khi GPS xác nhận đang trong bán kính POI (admin heatmap).</summary>
    public const string GpsInsidePoiEventType = "poi_gps_inside";

    // ── Debounce + Cooldown ──────────────────────────────────────────────
    // Cooldown: sau khi phát 1 POI, đợi X giây mới phát POI đó lại
    private const int CooldownSeconds = 60;

    // Debounce: user phải ở trong vùng liên tục X giây mới phát
    // (tránh trigger khi chỉ đi qua nhanh)
    private const int DebounceSeconds = 3;

    // Dict lưu: POI id → thời điểm phát lần cuối
    private readonly Dictionary<int, DateTime> _lastTriggered = new();

    // Dict lưu: POI id → thời điểm bắt đầu ở trong vùng
    private readonly Dictionary<int, DateTime> _enterTime = new();

    // POI đang được phát hiện trong vùng hiện tại
    private int? _currentInsidePoi = null;

    private readonly Dictionary<int, DateTime> _lastGpsPresenceUtc = new();
    private const int GpsPresenceLogIntervalSeconds = 45;

    // Event thông báo ra ngoài (MapPage, HomePage lắng nghe)
    public event Action<TouristPlace>? OnPoiEntered;
    public event Action<TouristPlace>? OnPoiTriggered;
    public event Action? OnPoiExited;

    /// <summary>Tiêm dịch vụ POI và engine TTS dùng khi trigger tự động.</summary>
    public GeofenceEngine(DatabaseService dbService, NarrationEngine narrationEngine, TouristAuthService authService)
    {
        _dbService = dbService;
        _narrationEngine = narrationEngine;
        _authService = authService;
    }

    /// <summary>
    /// Gọi mỗi khi có cập nhật vị trí GPS mới.
    /// </summary>
    public async Task ProcessLocationAsync(Location userLocation)
    {
        var places = await _dbService.GetPlacesAsync();

        // Nhiều vùng chồng nhau: chọn POI có tâm gần nhất (Priority chỉ dùng để phá hòa nếu cần)
        var insidePoi = places
            .Select(p => new
            {
                P = p,
                DistM = Location.CalculateDistance(
                    userLocation.Latitude, userLocation.Longitude,
                    p.Latitude, p.Longitude,
                    DistanceUnits.Kilometers) * 1000
            })
            .Where(x => x.DistM <= x.P.Radius)
            .OrderBy(x => x.DistM)
            .ThenByDescending(x => x.P.Priority)
            .Select(x => x.P)
            .FirstOrDefault();

        if (insidePoi != null)
            await HandleInsidePoi(insidePoi);
        else
            HandleOutsideAllPoi();
    }

    // ── Private helpers ─────────────────────────────────────────────────

    private async Task HandleInsidePoi(TouristPlace poi)
    {
        var now = DateTime.UtcNow;

        // Nếu vừa bước vào POI mới → ghi nhận thời điểm enter
        if (_currentInsidePoi != poi.Id)
        {
            _currentInsidePoi = poi.Id;
            _enterTime[poi.Id] = now;
            OnPoiEntered?.Invoke(poi); // UI có thể hiện banner
            System.Diagnostics.Debug.WriteLine($"[GEO] Entered: {poi.Name}");
        }

        // ── Debounce check: phải ở trong vùng đủ lâu ──
        if (!_enterTime.TryGetValue(poi.Id, out var enterTime)) return;
        if ((now - enterTime).TotalSeconds < DebounceSeconds) return;

        await TryLogGpsPresenceThrottledAsync(poi, now);

        // ── Cooldown check: chưa phát trong vòng X giây ──
        if (_lastTriggered.TryGetValue(poi.Id, out var lastTime))
        {
            if ((now - lastTime).TotalSeconds < CooldownSeconds)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[GEO] Cooldown active for: {poi.Name} " +
                    $"({(int)(CooldownSeconds - (now - lastTime).TotalSeconds)}s còn lại)");
                return;
            }
        }

        // ✅ Đủ điều kiện → phát thuyết minh!
        _lastTriggered[poi.Id] = now;
        OnPoiTriggered?.Invoke(poi);
        System.Diagnostics.Debug.WriteLine($"[GEO] Triggered: {poi.Name}");
        await _narrationEngine.SpeakAsync(poi);
    }

    private async Task TryLogGpsPresenceThrottledAsync(TouristPlace poi, DateTime nowUtc)
    {
        if (_lastGpsPresenceUtc.TryGetValue(poi.Id, out var last) &&
            (nowUtc - last).TotalSeconds < GpsPresenceLogIntervalSeconds)
            return;

        _lastGpsPresenceUtc[poi.Id] = nowUtc;

        try
        {
            const string prefKey = "tg_device_install_id";
            var deviceId = Preferences.Get(prefKey, "");
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                deviceId = Guid.NewGuid().ToString("N");
                Preferences.Set(prefKey, deviceId);
            }

            var manufacturer = DeviceInfo.Current.Manufacturer ?? "";
            var model = DeviceInfo.Current.Model ?? "";
            var deviceModel = $"{manufacturer} {model}".Trim();
            if (string.IsNullOrWhiteSpace(deviceModel)) deviceModel = "unknown";

            await _authService.LogPoiQrScanAsync(
                poi.Id,
                poi.NameVi,
                GpsInsidePoiEventType,
                0m,
                deviceId,
                deviceModel,
                DeviceInfo.Current.Platform.ToString());
        }
        catch
        {
            // Giống luồng quét QR: không chặn geofence nếu log lỗi.
        }
    }

    private void HandleOutsideAllPoi()
    {
        if (_currentInsidePoi == null) return;

        System.Diagnostics.Debug.WriteLine($"[GEO] Exited all POI");
        _currentInsidePoi = null;
        _enterTime.Clear();
        _lastGpsPresenceUtc.Clear();
        OnPoiExited?.Invoke();
    }

    /// <summary>Reset cooldown của 1 POI (dùng khi test)</summary>
    public void ResetCooldown(int poiId) => _lastTriggered.Remove(poiId);

    /// <summary>Reset toàn bộ (dùng khi restart tour)</summary>
    public void ResetAll()
    {
        _lastTriggered.Clear();
        _enterTime.Clear();
        _currentInsidePoi = null;
        _lastGpsPresenceUtc.Clear();
    }
}