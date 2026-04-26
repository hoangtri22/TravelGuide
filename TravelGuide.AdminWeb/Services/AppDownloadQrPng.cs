using QRCoder;

namespace TravelGuide.AdminWeb.Services;

/// <summary>Tạo ảnh PNG mã QR cục bộ — không phụ thuộc dịch vụ ngoài (tránh lỗi tải ảnh QR / mạng chặn).</summary>
internal static class AppDownloadQrPng
{
    internal static byte[] Encode(string payload, int targetSizePx)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return [];

        targetSizePx = targetSizePx <= 0 ? 320 : Math.Clamp(targetSizePx, 128, 1024);

        using var gen = new QRCodeGenerator();
        // ECC cao + ô lớn hơn giúp camera điện thoại đọc URL tunnel dài ổn định hơn.
        using var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.H);
        var png = new PngByteQRCode(data);
        var modules = data.ModuleMatrix.Count;
        var basePpm = targetSizePx / Math.Max(modules + 8, 12);
        var ppm = Math.Clamp(basePpm, 6, 28);
        return png.GetGraphic(ppm);
    }
}
