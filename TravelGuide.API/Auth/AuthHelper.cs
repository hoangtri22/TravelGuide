using TravelGuide.API.Models;

namespace TravelGuide.API.Auth;

public static class AuthHelper
{
    public static TouristAuthPrincipal? Authenticate(HttpContext context, AuthStore authStore)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var token = authHeader["Bearer ".Length..].Trim();
        return authStore.GetPrincipal(token);
    }
}
