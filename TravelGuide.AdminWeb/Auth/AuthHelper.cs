using TravelGuide.AdminWeb.Models;

namespace TravelGuide.AdminWeb.Auth;

/// <summary>Đọc header <c>Authorization: Bearer …</c> và tra cứu token trong <see cref="AuthStore"/>.</summary>
public static class AuthHelper
{
    /// <summary>Trả về principal nếu token hợp lệ; ngược lại null (401).</summary>
    public static AuthPrincipal? Authenticate(HttpContext context, AuthStore authStore)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var token = authHeader["Bearer ".Length..].Trim();
        return authStore.GetPrincipal(token);
    }
}
