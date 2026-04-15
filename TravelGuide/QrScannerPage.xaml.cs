using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Maui.Devices;
using TravelGuide.Models;
using ZXing.Net.Maui;

namespace TravelGuide;

public partial class QrScannerPage : ContentPage
{
    private readonly DatabaseService _dbService;
    private readonly TouristAuthService _authService;
    private bool _isHandlingResult;

    public QrScannerPage(DatabaseService dbService, TouristAuthService authService)
    {
        InitializeComponent();
        _dbService = dbService;
        _authService = authService;
        CameraView.Options = new BarcodeReaderOptions
        {
            AutoRotate = true,
            Multiple = false,
            Formats = BarcodeFormats.TwoDimensional
        };
    }

    private async void OnOpenScanHistoryClicked(object? sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync(nameof(QrScanHistoryPage));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Navigation error", ex.Message, "OK");
        }
    }

    private async void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (_isHandlingResult) return;
        var value = e.Results?.FirstOrDefault()?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return;

        _isHandlingResult = true;
        CameraView.IsDetecting = false;

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            StatusLabel.Text = $"Đã quét: {value}";
            try
            {
                await HandleQrPayloadAsync(value);
            }
            finally
            {
                _isHandlingResult = false;
                try
                {
                    CameraView.IsDetecting = true;
                }
                catch
                {
                    // Trang đã bị gỡ khỏi stack sau khi mở chi tiết.
                }
            }
        });
    }

    private async Task HandleQrPayloadAsync(string raw)
    {
        var premiumCode = TryParsePremiumClaim(raw);
        if (premiumCode is not null)
        {
            await HandlePremiumClaimFlowAsync(premiumCode);
            return;
        }

        var places = await _dbService.GetPlacesAsync();
        var place = TryResolvePlace(raw, places);
        if (place is null)
        {
            await DisplayAlert("Không tìm thấy", "QR không phải mã Premium hoặc địa điểm hợp lệ.", "OK");
            return;
        }

        await HandlePoiAfterScanAsync(place);
    }

    private async Task HandlePremiumClaimFlowAsync(string claimCode)
    {
        var me = await _authService.GetMeAsync();
        if (!me.Ok)
        {
            await DisplayAlert("Đăng nhập", "Vui lòng đăng nhập tài khoản du khách trước khi kích hoạt Premium.", "OK");
            return;
        }

        var fee = TouristPricing.PremiumActivationVnd;
        var confirm = await DisplayAlert(
            "Kích hoạt Premium",
            $"Phí kích hoạt (mô phỏng): {fee:N0} VND.\n\nMã: {claimCode}\n\nXác nhận đã thanh toán và kích hoạt Premium?",
            "Xác nhận",
            "Huỷ");
        if (!confirm) return;

        var (ok, message) = await _authService.RedeemPremiumClaimAsync(claimCode, fee);
        await DisplayAlert(ok ? "Thành công" : "Thất bại", message, "OK");
        if (ok)
            await _authService.GetMeAsync();

        await Navigation.PopAsync();
    }

    private async Task HandlePoiAfterScanAsync(TouristPlace place)
    {
        var me = await _authService.GetMeAsync();
        if (!me.Ok)
        {
            await DisplayAlert(
                "Đăng nhập",
                "Đăng nhập du khách để xác nhận thanh toán và xem địa điểm (tài khoản Premium xem miễn phí).",
                "OK");
            return;
        }

        var isPremium = string.Equals(me.AccountTier, "premium", StringComparison.OrdinalIgnoreCase);
        if (isPremium)
        {
            await OpenPlaceDetailAsync(place, 0, "premium_access");
            return;
        }

        var access = await _authService.GetPoiAccessAsync(place.Id);
        if (!access.Ok)
        {
            await DisplayAlert("Lỗi", string.IsNullOrWhiteSpace(access.Message) ? "Không kiểm tra được quyền truy cập." : access.Message, "OK");
            return;
        }

        if (access.HasAccess)
        {
            await OpenPlaceDetailAsync(place, 0, "unlocked_access");
            return;
        }

        var price = access.PriceVnd;
        var pay = await DisplayAlert(
            "Thanh toán địa điểm",
            $"Địa điểm này cần xác nhận thanh toán (mô phỏng).\n\nSố tiền: {price:N0} VND\n\nBấm Xác nhận để ghi nhận và xem chi tiết.",
            "Xác nhận",
            "Huỷ");
        if (!pay) return;

        var (paidOk, paidMsg) = await _authService.ConfirmPoiPurchaseAsync(place.Id, price);
        if (!paidOk)
        {
            await DisplayAlert("Thất bại", paidMsg, "OK");
            return;
        }

        await OpenPlaceDetailAsync(place, price, "purchase");
    }

    private static string GetOrCreateDeviceInstallId()
    {
        const string key = "tg_device_install_id";
        var v = Preferences.Get(key, "");
        if (!string.IsNullOrWhiteSpace(v)) return v;
        v = Guid.NewGuid().ToString("N");
        Preferences.Set(key, v);
        return v;
    }

    private async Task OpenPlaceDetailAsync(TouristPlace place, decimal amountVnd, string eventType)
    {
        var deviceId = GetOrCreateDeviceInstallId();
        var manufacturer = DeviceInfo.Current.Manufacturer ?? "";
        var model = DeviceInfo.Current.Model ?? "";
        var deviceModel = $"{manufacturer} {model}".Trim();
        if (string.IsNullOrWhiteSpace(deviceModel)) deviceModel = "unknown";
        var platform = DeviceInfo.Current.Platform.ToString();
        await _authService.LogPoiQrScanAsync(
            place.Id,
            place.NameVi,
            eventType,
            amountVnd,
            deviceId,
            deviceModel,
            platform);

        if (Handler?.MauiContext == null) return;
        var detailPage = Handler.MauiContext.Services.GetRequiredService<PlaceDetailPage>();
        detailPage.LoadPlace(place);
        await Navigation.PushAsync(detailPage);
        Navigation.RemovePage(this);
    }

    /// <summary>Nội dung QR kích hoạt Premium: <c>tg-premium:MÃ</c> hoặc URL có <c>tg_premium=</c>.</summary>
    private static string? TryParsePremiumClaim(string raw)
    {
        var t = (raw ?? string.Empty).Trim();
        const string prefix = "tg-premium:";
        if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var code = t[prefix.Length..].Trim();
            return code.Length > 0 ? code : null;
        }

        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri)) return null;
        var q = ParseQuery(uri.Query);
        if (q.TryGetValue("tg_premium", out var c) && !string.IsNullOrWhiteSpace(c))
            return c.Trim();
        if (q.TryGetValue("premium_code", out var c2) && !string.IsNullOrWhiteSpace(c2))
            return c2.Trim();
        return null;
    }

    private static TouristPlace? TryResolvePlace(string raw, List<TouristPlace> places)
    {
        if (places.Count == 0) return null;

        var byId = TryParsePoiId(raw);
        if (byId.HasValue)
        {
            return places.FirstOrDefault(p => p.Id == byId.Value);
        }

        var byCoord = TryParseCoordinates(raw);
        if (byCoord.HasValue)
        {
            var (lat, lon) = byCoord.Value;
            return places
                .Select(p => new
                {
                    Place = p,
                    DistanceKm = Location.CalculateDistance(lat, lon, p.Latitude, p.Longitude, DistanceUnits.Kilometers)
                })
                .OrderBy(x => x.DistanceKm)
                .FirstOrDefault(x => x.DistanceKm <= 0.2)?.Place;
        }

        return places.FirstOrDefault(p =>
            string.Equals((p.MapLink ?? "").Trim(), raw, StringComparison.OrdinalIgnoreCase));
    }

    private static int? TryParsePoiId(string raw)
    {
        if (int.TryParse(raw, out var directId) && directId > 0) return directId;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if ((segments[i].Equals("poi", StringComparison.OrdinalIgnoreCase)
                 || segments[i].Equals("pois", StringComparison.OrdinalIgnoreCase))
                && int.TryParse(segments[i + 1], out var fromPath)
                && fromPath > 0)
            {
                return fromPath;
            }
        }

        var query = ParseQuery(uri.Query);
        foreach (var key in new[] { "id", "poiId", "poi_id" })
        {
            if (query.TryGetValue(key, out var value)
                && int.TryParse(value, out var fromQuery)
                && fromQuery > 0)
            {
                return fromQuery;
            }
        }

        return null;
    }

    private static (double Lat, double Lon)? TryParseCoordinates(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
        var q = ParseQuery(uri.Query);
        if (!q.TryGetValue("q", out var value) || string.IsNullOrWhiteSpace(value)) return null;

        var match = Regex.Match(value, @"^\s*(-?\d+(\.\d+)?)\s*,\s*(-?\d+(\.\d+)?)\s*$");
        if (!match.Success) return null;

        if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            && double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            return (lat, lon);
        }

        return null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var raw = query.TrimStart('?');
        foreach (var pair in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx < 0) continue;
            var key = Uri.UnescapeDataString(pair[..idx]);
            var value = Uri.UnescapeDataString(pair[(idx + 1)..]);
            result[key] = value;
        }
        return result;
    }
}
