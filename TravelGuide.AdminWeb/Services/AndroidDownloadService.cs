using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace TravelGuide.AdminWeb.Services;

public sealed class AndroidDownloadOptions
{
    public const string SectionName = "AndroidDownload";

    // Link APK public (Google Drive direct, CDN, server static file...)
    public string ApkUrl { get; set; } = "";

    // Base URL public của AdminWeb, ví dụ: https://travelguide.app
    public string PublicBaseUrl { get; set; } = "";

    // Path nhận QR scan để redirect tới APK.
    public string DownloadPath { get; set; } = "/download/android";

    // Tên file QR lưu trong WEB/qrcodes.
    public string QrFileName { get; set; } = "android-download.png";

    public int QrImageSizePx { get; set; } = 320;
}

public static class AndroidDownloadService
{
    public static string NormalizeDownloadPath(string? path)
    {
        var p = (path ?? "").Trim();
        if (p.Length == 0) return "/download/android";
        if (!p.StartsWith('/')) p = "/" + p;
        return p;
    }

    public static string GetNormalizedQrFileName(AndroidDownloadOptions options)
    {
        var fileName = string.IsNullOrWhiteSpace(options.QrFileName)
            ? "android-download.png"
            : options.QrFileName.Trim();
        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            fileName += ".png";
        return fileName;
    }

    public static string BuildDownloadUrl(AndroidDownloadOptions options)
    {
        var path = NormalizeDownloadPath(options.DownloadPath);
        var baseUrl = (options.PublicBaseUrl ?? "").Trim().TrimEnd('/');
        if (baseUrl.Length == 0) return path;
        return baseUrl + path;
    }

    public static string BuildDownloadUrl(AndroidDownloadOptions options, HttpRequest request)
    {
        var configured = BuildDownloadUrl(options);
        if (configured.StartsWith("/", StringComparison.Ordinal))
        {
            var baseFromRequest = $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/');
            return baseFromRequest + configured;
        }

        return configured;
    }

    public static async Task<string?> TryGenerateQrAsync(
        AndroidDownloadOptions options,
        IWebHostEnvironment env,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken = default)
    {
        var root = env.WebRootPath;
        if (string.IsNullOrWhiteSpace(root))
            return null;

        var payload = BuildDownloadUrl(options);
        if (payload.StartsWith("/", StringComparison.Ordinal))
            return null;
        var remote = PoiQrCodeGenerator.BuildRemoteQrImageUrl(payload, options.QrImageSizePx);
        var fileName = GetNormalizedQrFileName(options);

        try
        {
            var dir = Path.Combine(root, "qrcodes");
            Directory.CreateDirectory(dir);
            var outPath = Path.Combine(dir, fileName);

            var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(20);
            using var resp = await http.GetAsync(remote, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!resp.IsSuccessStatusCode)
                return null;

            await using var input = await resp.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(outPath);
            await input.CopyToAsync(output, cancellationToken);
            return $"/qrcodes/{fileName}";
        }
        catch
        {
            return null;
        }
    }
}
