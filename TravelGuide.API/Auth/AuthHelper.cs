using TravelGuide.API.Models;

namespace TravelGuide.API.Auth;

public static class AuthHelper
{
    public static string? GetBearerToken(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        var token = authHeader["Bearer ".Length..].Trim();
        return token.Length == 0 ? null : token;
    }

    public static TouristAuthPrincipal? Authenticate(HttpContext context, AuthStore authStore)
    {
        var token = GetBearerToken(context);
        return token is null ? null : authStore.GetPrincipal(token);
    }
}
