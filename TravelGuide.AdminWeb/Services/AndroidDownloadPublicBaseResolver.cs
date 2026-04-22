using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TravelGuide.AdminWeb.Services;

/// <summary>
/// Khi dev mở AdminWeb qua localhost, QR/redirect tải app vẫn cần URL mà điện thoại truy cập được (IP LAN hoặc PublicBaseUrl).
/// </summary>
internal static class AndroidDownloadPublicBaseResolver
{
    internal static string Resolve(HttpRequest request, AndroidDownloadOptions options)
    {
        var pathBase = request.PathBase.HasValue ? request.PathBase.Value!.TrimEnd('/') : "";
        var baseSuffix = string.IsNullOrEmpty(pathBase) ? "" : pathBase;

        var pb = (options.PublicBaseUrl ?? "").Trim().TrimEnd('/');
        if (pb.Length > 0
            && Uri.TryCreate(pb, UriKind.Absolute, out var pubUri)
            && (string.Equals(pubUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(pubUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            return pubUri.Scheme + "://" + pubUri.Authority + baseSuffix;

        var hostName = request.Host.Host;
        if (!IsLoopbackHost(hostName))
            return $"{request.Scheme}://{request.Host.Value}{baseSuffix}".TrimEnd('/');

        var lan = TryGetFirstPrivateLanIPv4();
        if (lan is null)
            return $"{request.Scheme}://{request.Host.Value}{baseSuffix}".TrimEnd('/');

        var port = request.Host.Port;
        var portSegment = port.HasValue ? $":{port.Value}" : "";
        return $"{request.Scheme}://{lan}{portSegment}{baseSuffix}".TrimEnd('/');
    }

    internal static bool IsLoopbackRequest(HttpRequest request) => IsLoopbackHost(request.Host.Host);

    internal static bool UsedLanFallback(HttpRequest request, AndroidDownloadOptions options)
    {
        if (!IsLoopbackHost(request.Host.Host))
            return false;
        if (!string.IsNullOrWhiteSpace((options.PublicBaseUrl ?? "").Trim()))
            return false;
        return TryGetFirstPrivateLanIPv4() is not null;
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!IPAddress.TryParse(host, out var ip))
            return false;
        return IPAddress.IsLoopback(ip);
    }

    private static string? TryGetFirstPrivateLanIPv4()
    {
        string? bestIp = null;
        var bestScore = int.MinValue;
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                continue;
            if (ni.Description.Contains("virtual", StringComparison.OrdinalIgnoreCase)
                || ni.Description.Contains("vmware", StringComparison.OrdinalIgnoreCase)
                || ni.Description.Contains("hyper-v", StringComparison.OrdinalIgnoreCase)
                || ni.Description.Contains("wsl", StringComparison.OrdinalIgnoreCase)
                || ni.Description.Contains("docker", StringComparison.OrdinalIgnoreCase))
                continue;

            var props = ni.GetIPProperties();
            var hasGateway = props.GatewayAddresses.Any(g =>
                g?.Address is not null &&
                g.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.Any.Equals(g.Address) &&
                !IPAddress.None.Equals(g.Address));

            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;
                var b = ua.Address.GetAddressBytes();
                if (IsPrivateLan(b))
                {
                    var candidate = ua.Address.ToString();
                    var score = ScoreLanCandidate(b, hasGateway);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIp = candidate;
                    }
                }
            }
        }

        return bestIp;
    }

    private static int ScoreLanCandidate(byte[] b, bool hasGateway)
    {
        // Ưu tiên lớp mạng phổ biến cho Wi-Fi nội bộ:
        // 192.168.x.x > 10.x.x.x > 172.16-31.x.x (thường dính network ảo trên Windows).
        var score = 0;
        if (hasGateway) score += 100;
        if (b[0] == 192 && b[1] == 168) score += 60;
        else if (b[0] == 10) score += 40;
        else if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) score += 20;
        return score;
    }

    private static bool IsPrivateLan(byte[] b)
    {
        if (b.Length < 4)
            return false;
        // Bỏ APIPA — thường không route được cho điện thoại.
        if (b[0] == 169 && b[1] == 254)
            return false;
        if (b[0] == 10)
            return true;
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            return true;
        if (b[0] == 192 && b[1] == 168)
            return true;
        return false;
    }
}
