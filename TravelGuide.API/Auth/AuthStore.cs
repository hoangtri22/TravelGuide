using System.Collections.Concurrent;
using System.Security.Cryptography;
using TravelGuide.API.Models;

namespace TravelGuide.API.Auth;

public sealed class AuthStore
{
    private readonly ConcurrentDictionary<string, TouristAuthPrincipal> _tokens = new();

    public string CreateToken(TouristUser user)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        _tokens[token] = new TouristAuthPrincipal(user.Id, user.Username);
        return token;
    }

    public TouristAuthPrincipal? GetPrincipal(string token)
    {
        return _tokens.TryGetValue(token, out var principal) ? principal : null;
    }

    public void RemoveToken(string token) => _tokens.TryRemove(token, out _);
}
