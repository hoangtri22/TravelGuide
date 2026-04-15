using System.Security.Cryptography;
using System.Text;

namespace TravelGuide.API.Services;

public static class PasswordTools
{
    public static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
