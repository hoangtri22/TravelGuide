using System.Collections.Concurrent;
using System.Security.Cryptography;
using TravelGuide.AdminWeb.Models;

namespace TravelGuide.AdminWeb.Auth;

/// <summary>Lưu token đăng nhập trong RAM (ConcurrentDictionary); không persist sau restart.</summary>
public sealed class AuthStore
{
    private readonly ConcurrentDictionary<string, AuthPrincipal> _tokens = new();

    /// <summary>Sinh token ngẫu nhiên và gắn với user đã đăng nhập.</summary>
    public string CreateToken(UserAccount user)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var role = (user.Role ?? "").Trim().ToLowerInvariant();
        _tokens[token] = new AuthPrincipal(user.Id, user.Username, role);
        return token;
    }

    /// <summary>Tra cứu principal theo token (chuỗi hex).</summary>
    public AuthPrincipal? GetPrincipal(string token)
    {
        return _tokens.TryGetValue(token, out var principal) ? principal : null;
    }
}
