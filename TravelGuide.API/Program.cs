using Microsoft.OpenApi.Models;
using System.Text.Json;
using TravelGuide.API.Auth;
using TravelGuide.API.Data;
using TravelGuide.API.Models;
using TravelGuide.API.Services;

var builder = WebApplication.CreateBuilder(args);
void ApplySharedSqlConnection(IConfiguration cfg)
{
    var fromCfg = (cfg["ConnectionStrings:TravelGuideSqlServer"] ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(fromCfg)) return;
    Environment.SetEnvironmentVariable("TRAVELGUIDE_SQLSERVER", fromCfg, EnvironmentVariableTarget.Process);
}
ApplySharedSqlConnection(builder.Configuration);
builder.Services.AddSingleton<TouristDb>();
builder.Services.AddSingleton<PoiPublicReader>();
builder.Services.AddSingleton<AuthStore>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TravelGuide Public API",
        Version = "v1",
        Description = "Public API for tourist app authentication and POI data."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter token in format: Bearer {token}"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "TravelGuide Public API v1");
    options.RoutePrefix = "swagger";
});

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TouristDb>();
    await db.InitializeAsync();
}

app.MapGet("/", () => Results.Ok(new { service = "TravelGuide.API", status = "ok" }));

string ResolveApkFilePathFromApiHost()
{
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "TravelGuide.AdminWeb", "WEB", "apk", "travelguide-latest.apk"),
        Path.Combine(Directory.GetCurrentDirectory(), "WEB", "apk", "travelguide-latest.apk"),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "TravelGuide.AdminWeb", "WEB", "apk", "travelguide-latest.apk")),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "WEB", "apk", "travelguide-latest.apk"))
    };
    return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
}

app.MapGet("/download/apk", (HttpContext context) =>
{
    var apkPath = ResolveApkFilePathFromApiHost();
    if (!File.Exists(apkPath))
        return Results.NotFound("APK file not found. Put file at TravelGuide.AdminWeb/WEB/apk/travelguide-latest.apk");

    context.Response.Headers["Content-Disposition"] = "attachment; filename=\"app.apk\"";
    return Results.File(apkPath, "application/vnd.android.package-archive", "app.apk");
});

app.MapPost("/api/tourist/auth/device-login", async (TouristDeviceLoginRequest request, TouristDb db, AuthStore authStore) =>
{
    var deviceId = (request.DeviceId ?? string.Empty).Trim();
    if (deviceId.Length < 6)
        return Results.BadRequest("DeviceId không hợp lệ.");

    var user = await db.GetOrCreateDeviceUserAsync(deviceId, request.DeviceName);
    var token = authStore.CreateToken(user);
    try
    {
        await db.RecordLoginSessionAsync(user.Id, token, request.DeviceId);
    }
    catch
    {
        // Vẫn trả token để app hoạt động ngay cả khi DB phiên chưa sẵn sàng.
    }

    return Results.Ok(new
    {
        token,
        username = user.Username,
        displayName = user.DisplayName,
        accountTier = user.AccountTier
    });
});

app.MapGet("/api/tourist/auth/me", async (HttpContext context, AuthStore authStore, TouristDb db) =>
{
    var bearer = AuthHelper.GetBearerToken(context);
    var principal = bearer is null ? null : authStore.GetPrincipal(bearer);
    if (principal is null) return Results.Unauthorized();
    if (bearer is not null)
    {
        try
        {
            await db.TouchSessionByBearerTokenAsync(bearer);
        }
        catch
        {
            // ignore
        }
    }

    var user = await db.GetUserByUsernameAsync(principal.Username);
    if (user is null) return Results.Unauthorized();
    return Results.Ok(new
    {
        principal.UserId,
        principal.Username,
        user.DisplayName,
        user.AccountTier
    });
});

app.MapPost("/api/tourist/auth/logout", async (HttpContext context, AuthStore authStore, TouristDb db) =>
{
    var bearer = AuthHelper.GetBearerToken(context);
    if (string.IsNullOrWhiteSpace(bearer))
        return Results.Unauthorized();
    var principal = authStore.GetPrincipal(bearer);
    if (principal is null)
        return Results.Unauthorized();
    authStore.RemoveToken(bearer);
    try
    {
        // Thu hồi mọi phiên SQL của user (mỗi lần đăng nhập cũ có thể còn dòng chưa revoked).
        await db.RevokeAllSessionsForTouristAsync(principal.UserId);
    }
    catch
    {
        // vẫn xóa token bộ nhớ; DB có thể khác phiên bản bảng
    }

    return Results.Ok(new { message = "Đã đăng xuất." });
});

app.MapPost("/api/tourist/premium/redeem", async (HttpContext context, PremiumRedeemRequest body, AuthStore authStore, TouristDb db, IConfiguration configuration) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();

    var expectedPremium = configuration.GetValue<decimal>("TouristPricing:PremiumActivationVnd", 60000m);
    var declared = body.AmountVnd ?? expectedPremium;
    if (declared != expectedPremium)
        return Results.BadRequest($"Phí kích hoạt Premium là {expectedPremium:N0} VND.");

    var (ok, message) = await db.RedeemPremiumClaimAsync(principal.UserId, body.ClaimCode ?? "");
    return ok ? Results.Ok(new { message }) : Results.BadRequest(message);
});

app.MapGet("/api/tourist/pois/{id:int}/access", async (HttpContext context, int id, AuthStore authStore, TouristDb db, PoiPublicReader poiReader, IConfiguration configuration) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    var poi = await poiReader.GetPublishedByIdAsync(id, context.RequestAborted);
    if (poi is null) return Results.NotFound();
    var user = await db.GetUserByIdAsync(principal.UserId);
    if (user is null) return Results.Unauthorized();
    var premium = string.Equals(user.AccountTier, "premium", StringComparison.OrdinalIgnoreCase);
    var unlocked = await db.IsPoiUnlockedAsync(principal.UserId, id);
    var hasAccess = premium || unlocked;
    var freeTierUnlockVnd = configuration.GetValue<decimal>("TouristPricing:DefaultPoiUnlockVnd", 1000m);
    return Results.Ok(new
    {
        hasAccess,
        requiresPurchase = !hasAccess,
        priceVnd = hasAccess ? 0m : freeTierUnlockVnd
    });
});

app.MapPost("/api/tourist/pois/{id:int}/purchase-confirm", async (HttpContext context, int id, PoiUnlockConfirmRequest? body, AuthStore authStore, TouristDb db, PoiPublicReader poiReader, IConfiguration configuration) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    var poi = await poiReader.GetPublishedByIdAsync(id, context.RequestAborted);
    if (poi is null) return Results.NotFound();
    var expected = configuration.GetValue<decimal>("TouristPricing:DefaultPoiUnlockVnd", 1000m);
    var amount = body?.AmountVnd ?? expected;
    if (amount != expected)
        return Results.BadRequest($"Số tiền mở địa điểm (Free) là {expected:N0} VND.");
    var (ok, message) = await db.ConfirmPoiUnlockAsync(principal.UserId, id, amount);
    return ok ? Results.Ok(new { message }) : Results.BadRequest(message);
});

app.MapPost("/api/tourist/pois/scan-log", async (HttpContext context, TouristPoiScanLogRequest body, AuthStore authStore, TouristDb db) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (body.PoiId <= 0) return Results.BadRequest("PoiId không hợp lệ.");
    await db.InsertPoiQrScanLogAsync(
        principal.UserId,
        principal.Username,
        body.PoiId,
        body.PoiNameVi,
        body.EventType ?? "poi_qr_access",
        body.AmountVnd,
        body.DeviceId,
        body.DeviceModel,
        body.AppPlatform);
    return Results.Ok();
});

app.MapGet("/api/tourist/pois/my-scan-history", async (HttpContext context, AuthStore authStore, TouristDb db) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    var rows = await db.GetMyPoiScanHistoryAsync(principal.UserId, 200);
    return Results.Ok(rows);
});

app.MapGet("/api/tourist/pois/gps-heatmap", async (HttpContext context, AuthStore authStore, TouristDb db, int minutes = 90) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    var rows = await db.GetGpsHeatmapByPoiAsync(minutes);
    return Results.Ok(rows.Select(x => new { poiId = x.PoiId, poiNameVi = x.PoiNameVi, gpsHits = x.GpsHits }));
});

app.MapPost("/api/tourist/comments", async (HttpContext context, TouristCommentCreateRequest request, AuthStore authStore, TouristDb db) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (request.PoiId <= 0) return Results.BadRequest("PoiId không hợp lệ.");
    var content = (request.Content ?? "").Trim();
    if (content.Length < 4) return Results.BadRequest("Nội dung bình luận quá ngắn.");
    var id = await db.InsertTouristCommentAsync(
        principal.UserId,
        principal.Username,
        request.PoiId,
        request.PoiNameVi,
        request.Rating,
        content);
    return id > 0 ? Results.Ok(new { id, message = "Đã gửi bình luận." }) : Results.BadRequest("Không thể gửi bình luận.");
});

app.MapGet("/api/tourist/comments/{poiId:int}", async (int poiId, TouristDb db, int take = 100) =>
{
    var rows = await db.GetTouristCommentsByPoiAsync(poiId, take);
    return Results.Ok(rows);
});

app.MapGet("/api/public/pois", async (PoiPublicReader poiReader, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await poiReader.GetPublishedAsync(ct));
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[PoiPublic] SQL: {ex.Message}");
        return Results.Ok(await LoadPublicPoisAsync());
    }
});

app.Run();

static async Task<List<PublicPoiDto>> LoadPublicPoisAsync()
{
    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "extra_places.json"),
        Path.Combine(AppContext.BaseDirectory, "Resources", "Raw", "extra_places.json")
    };
    var filePath = candidates.FirstOrDefault(File.Exists);
    if (string.IsNullOrWhiteSpace(filePath)) return [];

    await using var stream = File.OpenRead(filePath);
    var rows = await JsonSerializer.DeserializeAsync<List<ExtraPlaceRow>>(stream,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? [];

    var nextId = 1;
    return rows.Select(x => new PublicPoiDto(
        nextId++,
        x.NameVi ?? "",
        "",
        "",
        "",
        "",
        x.DescVi ?? "",
        "",
        "",
        "",
        "",
        x.Latitude,
        x.Longitude,
        x.Radius <= 0 ? 50 : x.Radius,
        x.ImagePath ?? "",
        x.AudioUrl ?? "",
        x.Priority,
        x.MapLink ?? "",
        x.Price < 0 ? 0 : x.Price,
        string.IsNullOrWhiteSpace(x.Tag) ? "Địa Điểm Du Lịch" : x.Tag.Trim(),
        x.QrImagePath ?? ""
    )).ToList();
}

internal sealed class ExtraPlaceRow
{
    public string? NameVi { get; set; }
    public string? DescVi { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Radius { get; set; }
    public string? ImagePath { get; set; }
    public string? AudioUrl { get; set; }
    public string? QrImagePath { get; set; }
    public int Priority { get; set; }
    public string? MapLink { get; set; }
    public decimal Price { get; set; }
    public string? Tag { get; set; }
}
