using TravelGuide.Models;
using Microsoft.Maui.Devices.Sensors;

namespace TravelGuide;

/// <summary>
/// Geofence Engine: So sánh vị trí user vs từng POI,
/// quyết định khi nào phát thuyết minh (có debounce + cooldown).
/// </summary>
public class GeofenceEngine
{
    private readonly DatabaseService _dbService;
    private readonly NarrationEngine _narrationEngine;

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

    // Event thông báo ra ngoài (MapPage, HomePage lắng nghe)
    public event Action<TouristPlace>? OnPoiEntered;
    public event Action<TouristPlace>? OnPoiTriggered;
    public event Action? OnPoiExited;

    public GeofenceEngine(DatabaseService dbService, NarrationEngine narrationEngine)
    {
        _dbService = dbService;
        _narrationEngine = narrationEngine;
    }

    /// <summary>
    /// Gọi mỗi khi có cập nhật vị trí GPS mới.
    /// </summary>
    public async Task ProcessLocationAsync(Location userLocation)
    {
        var places = await _dbService.GetPlacesAsync();

        // Tìm POI gần nhất đang trong phạm vi Radius
        TouristPlace? insidePoi = null;
        double minDist = double.MaxValue;

        foreach (var p in places)
        {
            double dist = Location.CalculateDistance(
                userLocation.Latitude, userLocation.Longitude,
                p.Latitude, p.Longitude,
                DistanceUnits.Kilometers) * 1000; // → mét

            if (dist <= p.Radius && dist < minDist)
            {
                minDist = dist;
                insidePoi = p;
            }
        }

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

    private void HandleOutsideAllPoi()
    {
        if (_currentInsidePoi == null) return;

        System.Diagnostics.Debug.WriteLine($"[GEO] Exited all POI");
        _currentInsidePoi = null;
        _enterTime.Clear();
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
    }
}