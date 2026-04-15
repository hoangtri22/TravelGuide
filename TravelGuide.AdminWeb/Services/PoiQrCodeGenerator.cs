using Microsoft.AspNetCore.Hosting;
using TravelGuide.AdminWeb.Data;
using TravelGuide.AdminWeb.Models;

namespace TravelGuide.AdminWeb.Services;

/// <summary>Cấu hình nội dung mã QR và kích thước ảnh (api.qrserver.com).</summary>
public sealed class PoiQrOptions
{
    public const string SectionName = "PoiQr";

    /// <summary>Kích thước cạnh ảnh PNG (pixel).</summary>
    public int ImageSizePx { get; set; } = 256;

    /// <summary>
    /// URL mã hóa trong QR, có thể dùng <c>{id}</c> thay bằng Id POI.
    /// Để trống: ưu tiên MapLink của POI, không có thì link Google Maps từ tọa độ.
    /// </summary>
    public string? DataUrlTemplate { get; set; }
}

/// <summary>Tải ảnh QR từ dịch vụ công khai và lưu dưới WEB/qrcodes, hoặc lưu URL từ xa nếu lỗi.</summary>
public static class PoiQrCodeGenerator
{
    public static string BuildPayload(int poiId, PoiDto poi, string? dataUrlTemplate)
    {
        if (!string.IsNullOrWhiteSpace(dataUrlTemplate))
        {
            return dataUrlTemplate.Replace("{id}", poiId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase);
        }

        var map = (poi.MapLink ?? "").Trim();
        if (map.Length > 0) return map;
        return FormattableString.Invariant($"https://www.google.com/maps?q={poi.Latitude},{poi.Longitude}");
    }

    public static string BuildRemoteQrImageUrl(string payload, int sizePx)
    {
        var s = sizePx <= 0 ? 256 : sizePx;
        return $"https://api.qrserver.com/v1/create-qr-code/?size={s}x{s}&data={Uri.EscapeDataString(payload)}";
    }

    public static async Task<string?> TryGenerateAndStoreAsync(
        int poiId,
        PoiDto submittedPoi,
        TravelGuideDb db,
        HttpClient http,
        IWebHostEnvironment env,
        PoiQrOptions options,
        CancellationToken cancellationToken = default)
    {
        var payload = BuildPayload(poiId, submittedPoi, options.DataUrlTemplate);
        var remote = BuildRemoteQrImageUrl(payload, options.ImageSizePx);
        try
        {
            using var resp = await http.GetAsync(remote, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                await db.UpdatePoiQrImagePathAsync(poiId, remote);
                return remote;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
            {
                await db.UpdatePoiQrImagePathAsync(poiId, remote);
                return remote;
            }

            var root = env.WebRootPath;
            if (string.IsNullOrEmpty(root))
            {
                await db.UpdatePoiQrImagePathAsync(poiId, remote);
                return remote;
            }

            var dir = Path.Combine(root, "qrcodes");
            Directory.CreateDirectory(dir);
            var file = $"poi-{poiId}.png";
            await File.WriteAllBytesAsync(Path.Combine(dir, file), bytes, cancellationToken);
            var rel = $"/qrcodes/{file}";
            await db.UpdatePoiQrImagePathAsync(poiId, rel);
            return rel;
        }
        catch
        {
            await db.UpdatePoiQrImagePathAsync(poiId, remote);
            return remote;
        }
    }
}
