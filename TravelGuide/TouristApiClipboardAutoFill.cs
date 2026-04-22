namespace TravelGuide;

/// <summary>
/// Windows + MAUI trên cùng máy với <c>dev-tunnel.ps1</c>: tự điền URL tunnel từ clipboard vào ô API khi mở màn hình,
/// nếu URL đang lưu là localhost/emulator hoặc là tunnel cũ khác URL trong clipboard.
/// </summary>
internal static class TouristApiClipboardAutoFill
{
    internal static async Task TryApplyWindowsTunnelClipboardAsync(Entry apiEntry, TouristAuthService auth)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            if (!Clipboard.Default.HasText)
                return;

            var raw = await Clipboard.Default.GetTextAsync();
            var candidate = TryExtractHttpUrl(raw);
            if (string.IsNullOrWhiteSpace(candidate))
                return;

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var tunnelUri))
                return;

            if (!IsTunnelHost(tunnelUri))
                return;

            var normalized = tunnelUri.ToString().TrimEnd('/');
            var current = auth.GetCurrentApiBaseUrl().Trim().TrimEnd('/');
            if (string.Equals(normalized, current, StringComparison.OrdinalIgnoreCase))
                return;

            if (!ShouldReplaceCurrentWithTunnel(current, normalized))
                return;

            apiEntry.Text = normalized;
        }
        catch
        {
            // clipboard / UI — bỏ qua
        }
    }

    private static string? TryExtractHttpUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Trim().TrimEnd('/', '"', '\'');
            if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    private static bool IsTunnelHost(Uri uri)
    {
        var h = uri.Host;
        return h.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase)
               || h.EndsWith(".ngrok-free.app", StringComparison.OrdinalIgnoreCase)
               || h.EndsWith(".ngrok.io", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Không ghi đè khi người dùng đang cố ý dùng IP LAN / server riêng.</summary>
    private static bool ShouldReplaceCurrentWithTunnel(string currentTrimmed, string tunnelNormalized)
    {
        if (!Uri.TryCreate(currentTrimmed, UriKind.Absolute, out var curUri))
            return true;

        var host = curUri.Host;
        if (host is "127.0.0.1" or "localhost" or "10.0.2.2")
            return true;

        if (IsTunnelHost(curUri)
            && !string.Equals(
                curUri.ToString().TrimEnd('/'),
                tunnelNormalized,
                StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
