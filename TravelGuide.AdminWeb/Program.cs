// =============================================================================
// TravelGuide.AdminWeb — Minimal API (Program.cs)
// Đăng ký DI, static files, khởi tạo SQLite; map endpoint: auth, POI (CRUD/duyệt),
// public POI cho app, audio list, tài khoản, dịch thủ công, export JSON.
// =============================================================================

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<AuthStore>();
builder.Services.AddSingleton<TravelGuideDb>();
builder.Services.AddHttpClient();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TravelGuideDb>();
    await db.InitializeAsync();
}

app.MapPost("/api/auth/login", async (LoginRequest request, TravelGuideDb db, AuthStore authStore) =>
{
    await db.EnsurePhoOwnerDemoPasswordsAsync();
    var loginUser = TravelGuideDb.NormalizeLoginUsername(request.Username);
    var user = await db.GetUserByUsernameAsync(loginUser);
    var passwordTry = (request.Password ?? "").Trim();
    if (user is null || user.PasswordHash != PasswordTools.Hash(passwordTry))
    {
        return Results.Json(new
        {
            message = user is null
                ? "Không có tài khoản này trong CSDL của server (kiểm tra đã chạy đúng bản Admin Web / file travelguide-admin.db)."
                : "Sai mật khẩu. Chủ quán 6 quán phố: VkQuan@123 (ký tự @ và số 123)."
        }, statusCode: 401);
    }

    if (user.IsLocked)
    {
        return Results.Json(new { message = "Tài khoản đã bị khóa. Liên hệ quản trị viên." }, statusCode: 403);
    }

    if (user.Role == "owner" && !user.RegistrationApproved)
    {
        return Results.Json(new
        {
            message = "Tài khoản chờ admin duyệt đăng ký. Vui lòng thử đăng nhập sau khi được phê duyệt."
        }, statusCode: 403);
    }

    var token = authStore.CreateToken(user);
    return Results.Ok(new
    {
        token,
        username = user.Username,
        role = user.Role,
        displayName = user.DisplayName
    });
});

app.MapPost("/api/auth/register", async (RegisterRequest req, TravelGuideDb db) =>
{
    var u = TravelGuideDb.NormalizeLoginUsername(req.Username);
    var pw = (req.Password ?? "").Trim();
    var display = (req.DisplayName ?? "").Trim();
    if (u.Length < 3)
        return Results.BadRequest("Tên đăng nhập ít nhất 3 ký tự.");
    if (pw.Length < 6)
        return Results.BadRequest("Mật khẩu ít nhất 6 ký tự.");
    if (display.Length < 2)
        return Results.BadRequest("Tên quán / tên hiển thị ít nhất 2 ký tự.");
    if (string.Equals(u, "admin", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("Không được đăng ký tên đăng nhập này.");

    var ok = await db.CreateUserAsync(u, pw, display, "owner", registrationApproved: false);
    return ok
        ? Results.Ok(new { message = "Đã gửi đăng ký chủ quán. Vui lòng chờ admin duyệt trước khi đăng nhập." })
        : Results.BadRequest("Tên đăng nhập đã tồn tại.");
});

app.MapGet("/api/pois", async (HttpContext context, TravelGuideDb db, AuthStore authStore, IHttpClientFactory httpClientFactory, string? lang) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role == "admin")
        await db.EnsureAutoTranslationsAsync(httpClientFactory.CreateClient(), targetLang: lang);
    var pois = principal.Role == "admin"
        ? await db.GetPoisAsync(includeUnpublished: true)
        : await db.GetPoisOwnedByUserAsync(principal.UserId);
    return Results.Ok(pois);
});

app.MapGet("/api/public/pois", async (TravelGuideDb db, IHttpClientFactory httpClientFactory, string? lang) =>
{
    await db.EnsureAutoTranslationsAsync(httpClientFactory.CreateClient(), targetLang: lang);
    var pois = await db.GetPoisAsync(includeUnpublished: false);
    return Results.Ok(pois);
});

app.MapPost("/api/pois", async (HttpContext context, PoiDto poi, TravelGuideDb db, AuthStore authStore, IHttpClientFactory httpClientFactory) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();

    var id = await db.CreatePoiAsync(poi, principal);
    await db.EnsureAutoTranslationsAsync(httpClientFactory.CreateClient(), id);
    return Results.Ok(new { id });
});

app.MapPut("/api/pois/{id:int}", async (HttpContext context, int id, PoiDto poi, TravelGuideDb db, AuthStore authStore, IHttpClientFactory httpClientFactory) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();

    var updated = await db.UpdatePoiAsync(id, poi, principal);
    if (updated) await db.EnsureAutoTranslationsAsync(httpClientFactory.CreateClient(), id);
    return updated ? Results.Ok() : Results.NotFound();
});

app.MapDelete("/api/pois/{id:int}", async (HttpContext context, int id, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var deleted = await db.DeletePoiAsync(id);
    return deleted ? Results.Ok() : Results.NotFound();
});

app.MapPut("/api/pois/{id:int}/approve", async (HttpContext context, int id, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var updated = await db.SetPoiStatusAsync(id, status: "published", rejectReason: "");
    return updated ? Results.Ok() : Results.NotFound();
});

app.MapPut("/api/pois/{id:int}/reject", async (HttpContext context, int id, RejectPoiRequest request, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var updated = await db.SetPoiStatusAsync(id, status: "rejected", rejectReason: request.Reason ?? "");
    return updated ? Results.Ok() : Results.NotFound();
});

app.MapGet("/api/audio", async (HttpContext context, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    var audio = principal.Role == "admin"
        ? await db.GetAudioAsync()
        : await db.GetAudioAsyncForOwner(principal.UserId);
    return Results.Ok(audio);
});

app.MapGet("/api/accounts", async (HttpContext context, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var accounts = await db.GetUsersAsync();
    return Results.Ok(accounts.Select(x => new
    {
        x.Id,
        x.Username,
        x.DisplayName,
        x.Role,
        isLocked = x.IsLocked,
        registrationApproved = x.RegistrationApproved
    }));
});

app.MapPost("/api/accounts", async (HttpContext context, CreateAccountRequest request, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var ok = await db.CreateUserAsync(request.Username, request.Password, request.DisplayName, request.Role);
    return ok ? Results.Ok() : Results.BadRequest("Username already exists.");
});

app.MapPut("/api/accounts/{id:int}", async (HttpContext context, int id, UpdateAccountRequest request, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var updated = await db.UpdateUserAsync(id, request.DisplayName, request.Role, request.NewPassword);
    return updated ? Results.Ok() : Results.NotFound();
});

app.MapDelete("/api/accounts/{id:int}", async (HttpContext context, int id, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var deleted = await db.DeleteUserAsync(id);
    return deleted ? Results.Ok() : Results.NotFound();
});

app.MapPut("/api/accounts/{id:int}/lock", async (HttpContext context, int id, SetAccountLockRequest request, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var ok = await db.SetUserLockedAsync(id, request.Locked);
    return ok ? Results.Ok() : Results.BadRequest("Không thể khóa tài khoản admin hoặc không tồn tại.");
});

app.MapPut("/api/accounts/{id:int}/approve-registration", async (HttpContext context, int id, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var ok = await db.ApproveOwnerRegistrationAsync(id);
    return ok ? Results.Ok() : Results.BadRequest("Không duyệt được (chỉ áp dụng tài khoản chủ quán).");
});

app.MapPut("/api/accounts/{id:int}/reject-registration", async (HttpContext context, int id, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var ok = await db.RejectPendingOwnerRegistrationAsync(id);
    return ok ? Results.Ok() : Results.BadRequest("Chỉ từ chối được đăng ký đang chờ duyệt.");
});

app.MapGet("/api/translations/{id:int}", async (HttpContext context, int id, TravelGuideDb db, AuthStore authStore, IHttpClientFactory httpClientFactory, string? lang) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    var existing = await db.GetPoiAsync(id);
    if (existing is null) return Results.NotFound();
    if (principal.Role != "admin" && existing.OwnerUserId != principal.UserId)
        return Results.Forbid();
    await db.EnsureAutoTranslationsAsync(httpClientFactory.CreateClient(), id, lang);
    var poi = await db.GetPoiAsync(id);
    return poi is null ? Results.NotFound() : Results.Ok(poi);
});

app.MapPut("/api/translations/{id:int}", async (HttpContext context, int id, TranslationUpdateRequest request, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    var existing = await db.GetPoiAsync(id);
    if (existing is null) return Results.NotFound();
    if (principal.Role != "admin" && existing.OwnerUserId != principal.UserId)
        return Results.Forbid();

    var updated = await db.UpdateTranslationsAsync(id, request);
    return updated ? Results.Ok() : Results.NotFound();
});

app.MapGet("/api/export/extra_places.json", async (HttpContext context, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();
    var places = await db.GetExportPlacesAsync();
    return Results.Json(places);
});

app.Run();
