using System.Security.Cryptography;
using System.Text;

namespace TravelGuide.AdminWeb.Services;

/// <summary>Băm mật khẩu SHA256 hex (đơn giản; không salt — đủ demo/CMS nội bộ).</summary>
public static class PasswordTools
{
    /// <summary>Hash UTF-8 của <paramref name="input"/> thành chuỗi hex viết hoa.</summary>
    public static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
