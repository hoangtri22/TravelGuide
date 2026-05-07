using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.OpenApi.Models;
using QRCoder;

// =============================================================================
// TravelGuide.AdminWeb — Minimal API (Program.cs)
// Đăng ký DI, static files, khởi tạo SQLite; map endpoint: auth, POI (CRUD/duyệt),
// public POI cho app, audio list, tài khoản, dịch thủ công, export JSON.
// =============================================================================

string ResolveWebRootPath()
{
    // Khi chạy từ bin/Debug/net9.0, WebRoot "WEB" relative không tồn tại cạnh exe — tìm thư mục WEB cạnh .csproj.
    var tryPaths = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), "WEB"),
        Path.Combine(AppContext.BaseDirectory, "WEB"),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "WEB")),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WEB"))
    };
    foreach (var p in tryPaths)
    {
        if (Directory.Exists(p))
            return p;
    }

    return Path.Combine(Directory.GetCurrentDirectory(), "WEB");
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = ResolveWebRootPath()
});
void ApplySharedSqlConnection(IConfiguration cfg)
{
    var fromCfg = (cfg["ConnectionStrings:TravelGuideSqlServer"] ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(fromCfg)) return;
    Environment.SetEnvironmentVariable("TRAVELGUIDE_SQLSERVER", fromCfg, EnvironmentVariableTarget.Process);
}
ApplySharedSqlConnection(builder.Configuration);
builder.Services.AddSingleton<AuthStore>();
builder.Services.AddSingleton<TravelGuideDb>();
builder.Services.AddHttpClient();
builder.Services.Configure<TtsQueueOptions>(builder.Configuration.GetSection("TtsQueue"));
builder.Services.AddSingleton<ITtsQueue, TtsQueue>();
builder.Services.AddHostedService<TtsQueueWorker>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TravelGuide AdminWeb API",
        Version = "v1",
        Description = "API for AdminWeb and admin portal features."
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
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "TravelGuide AdminWeb API v1");
    options.RoutePrefix = "swagger";
});
app.UseDefaultFiles();
var apkContentTypes = new FileExtensionContentTypeProvider();
apkContentTypes.Mappings[".apk"] = "application/vnd.android.package-archive";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = apkContentTypes,
    OnPrepareResponse = ctx =>
    {
        // Tránh trình duyệt giữ CSS/JS/HTML cũ khi đang dev — tab Dữ liệu du khách dễ “vỡ layout” nếu thiếu class mới.
        if (!app.Environment.IsDevelopment()) return;
        var name = ctx.File.Name;
        if (name.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    }
});

/// <summary>Google Drive "xem file" không cho tải APK trực tiếp — đổi sang link uc?export=download.</summary>
static string NormalizePublicApkUrl(string url)
{
    url = url.Trim();
    const string marker = "/file/d/";
    var i = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
    if (i < 0) return url;
    var start = i + marker.Length;
    var slash = url.IndexOf('/', start);
    var id = slash < 0 ? url[start..] : url.Substring(start, slash - start);
    if (string.IsNullOrWhiteSpace(id)) return url;
    return $"https://drive.google.com/uc?export=download&id={id}";
}

/// <summary>
/// Thứ tự: nếu có WEB/apk/travelguide-latest.apk → URL cục bộ /download/apk (LAN/đồng bộ bản build);
/// không có file → biến môi trường → appsettings (Drive phải là link uc?export=download hoặc để server tự đổi).
/// </summary>
string? ResolveApkDownloadUrl(HttpContext context)
{
    var apkPath = GetApkFilePath();
    if (File.Exists(apkPath))
        return $"{context.Request.Scheme}://{context.Request.Host}/download/apk";

    var fromEnv = (Environment.GetEnvironmentVariable("TRAVELGUIDE_PUBLIC_APK_URL") ?? string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(fromEnv))
        return NormalizePublicApkUrl(fromEnv);

    var fromCfg = (builder.Configuration["ApkPublic:DownloadUrl"] ?? string.Empty).Trim();
    if (!string.IsNullOrWhiteSpace(fromCfg))
        return NormalizePublicApkUrl(fromCfg);

    return null;
}

string GetApkFilePath()
{
    var root = string.IsNullOrWhiteSpace(app.Environment.WebRootPath)
        ? Path.Combine(AppContext.BaseDirectory, "WEB")
        : app.Environment.WebRootPath;
    return Path.Combine(root, "apk", "travelguide-latest.apk");
}

string EnsureStaticApkQr(string publicApkUrl)
{
    var root = string.IsNullOrWhiteSpace(app.Environment.WebRootPath)
        ? Path.Combine(AppContext.BaseDirectory, "WEB")
        : app.Environment.WebRootPath;
    var qrDir = Path.Combine(root, "qrcodes");
    Directory.CreateDirectory(qrDir);
    var qrPath = Path.Combine(qrDir, "apk-download-static.png");
    var metaPath = Path.Combine(qrDir, "apk-download-static.url.txt");

    var shouldRegenerate = !File.Exists(qrPath);
    if (File.Exists(metaPath))
    {
        var old = (File.ReadAllText(metaPath) ?? string.Empty).Trim();
        if (!string.Equals(old, publicApkUrl, StringComparison.Ordinal))
            shouldRegenerate = true;
    }
    else
    {
        shouldRegenerate = true;
    }

    if (shouldRegenerate)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(publicApkUrl, QRCodeGenerator.ECCLevel.Q);
        var pngQr = new PngByteQRCode(qrData);
        var bytes = pngQr.GetGraphic(12);
        File.WriteAllBytes(qrPath, bytes);
        File.WriteAllText(metaPath, publicApkUrl);
    }

    return "/qrcodes/apk-download-static.png";
}

try
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<TravelGuideDb>();
        await db.InitializeAsync();
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine();
    Console.Error.WriteLine("=== TravelGuide.AdminWeb: khoi tao CSDL that bai ===");
    Console.Error.WriteLine(ex);
    Console.Error.WriteLine();
    Console.Error.WriteLine("Goi y: cai SQL Server Express + LocalDB, hoac dat bien moi truong TRAVELGUIDE_SQLSERVER (chuoi ket noi day du).");
    Console.Error.WriteLine("Neu vua sua code: dong het instance Admin Web / Visual Studio debug roi chay lai (tranh khoa file DLL).");
    Console.ResetColor();
    throw;
}

app.MapGet("/download/apk", (HttpContext context) =>
{
    var apkPath = GetApkFilePath();
    if (!File.Exists(apkPath))
        return Results.NotFound("APK file not found. Put file at WEB/apk/travelguide-latest.apk");

    context.Response.Headers["Content-Disposition"] = "attachment; filename=\"app.apk\"";
    return Results.File(apkPath, "application/vnd.android.package-archive", "app.apk");
});

app.MapGet("/api/download/apk-info", (HttpContext context) =>
{
    var publicApkUrl = ResolveApkDownloadUrl(context);
    if (string.IsNullOrWhiteSpace(publicApkUrl))
        return Results.BadRequest(
            "Chưa cấp link APK: đặt ApkPublic:DownloadUrl hoặc TRAVELGUIDE_PUBLIC_APK_URL, hoặc đặt file WEB/apk/travelguide-latest.apk để dùng /download/apk.");

    var qrPath = EnsureStaticApkQr(publicApkUrl);
    var absoluteQr = $"{context.Request.Scheme}://{context.Request.Host}{qrPath}";
    return Results.Ok(new
    {
        downloadUrl = publicApkUrl,
        qrImagePath = qrPath,
        qrImageUrl = absoluteQr
    });
});

static async Task<string?> SaveUploadedAudioAsync(IFormFile file, IWebHostEnvironment env, CancellationToken cancellationToken)
{
    if (file.Length <= 0) return null;

    var ext = Path.GetExtension(file.FileName ?? string.Empty).Trim().ToLowerInvariant();
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".m4a", ".aac", ".ogg"
    };
    if (!allowed.Contains(ext))
        throw new InvalidOperationException("Định dạng audio không hỗ trợ. Chỉ cho phép: .mp3, .wav, .m4a, .aac, .ogg");

    var root = string.IsNullOrWhiteSpace(env.WebRootPath)
        ? Path.Combine(AppContext.BaseDirectory, "WEB")
        : env.WebRootPath;
    var audioDir = Path.Combine(root, "audio");
    Directory.CreateDirectory(audioDir);

    var safeName = Path.GetFileNameWithoutExtension(file.FileName ?? string.Empty);
    safeName = string.Join("-", safeName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
    if (safeName.Length == 0) safeName = "audio";
    if (safeName.Length > 60) safeName = safeName[..60];

    var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
    var finalName = $"{safeName}-{stamp}{ext}";
    var fullPath = Path.Combine(audioDir, finalName);

    await using var fs = File.Create(fullPath);
    await file.CopyToAsync(fs, cancellationToken);
    return $"audio/{finalName}";
}

static async Task<string?> SaveUploadedPoiImageAsync(IFormFile file, IWebHostEnvironment env, CancellationToken cancellationToken)
{
    if (file.Length <= 0) return null;

    var ext = Path.GetExtension(file.FileName ?? string.Empty).Trim().ToLowerInvariant();
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif"
    };
    if (!allowed.Contains(ext))
        throw new InvalidOperationException("Định dạng ảnh không hỗ trợ. Chỉ cho phép: .jpg, .jpeg, .png, .webp, .gif");

    var root = string.IsNullOrWhiteSpace(env.WebRootPath)
        ? Path.Combine(AppContext.BaseDirectory, "WEB")
        : env.WebRootPath;
    var imgDir = Path.Combine(root, "images");
    Directory.CreateDirectory(imgDir);

    var safeName = Path.GetFileNameWithoutExtension(file.FileName ?? string.Empty);
    safeName = string.Join("-", safeName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
    if (safeName.Length == 0) safeName = "poi";
    if (safeName.Length > 50) safeName = safeName[..50];

    var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
    var finalName = $"{safeName}-{stamp}{ext}";
    var fullPath = Path.Combine(imgDir, finalName);

    await using var fs = File.Create(fullPath);
    await file.CopyToAsync(fs, cancellationToken);
    return $"images/{finalName}";
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
    var roleNorm = (user.Role ?? "").Trim().ToLowerInvariant();
    return Results.Json(
        new
        {
            token,
            username = user.Username,
            role = roleNorm,
            displayName = user.DisplayName
        },
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
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

app.MapGet("/api/pois/{id:int}", async (HttpContext context, int id, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    var poi = await db.GetPoiAsync(id);
    if (poi is null) return Results.NotFound();
    if (principal.Role != "admin" && poi.OwnerUserId != principal.UserId) return Results.Forbid();
    return Results.Ok(poi);
});

app.MapGet("/api/public/pois", async (TravelGuideDb db, IHttpClientFactory httpClientFactory, string? lang) =>
{
    await db.EnsureAutoTranslationsAsync(httpClientFactory.CreateClient(), targetLang: lang);
    var pois = await db.GetPoisAsync(includeUnpublished: false);
    return Results.Ok(pois);
});

app.MapGet("/api/public/tourist-comments/{poiId:int}", async (int poiId, TravelGuideDb db, int take = 100) =>
{
    var rows = await db.GetPublicCommentsByPoiAsync(poiId, take);
    return Results.Ok(rows);
});

app.MapPost("/api/pois", async (HttpContext context, PoiDto poi, TravelGuideDb db, AuthStore authStore, IHttpClientFactory httpClientFactory) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();

    var id = await db.CreatePoiAsync(poi, principal);
    await db.EnsureAutoTranslationsAsync(httpClientFactory.CreateClient(), id);
    return Results.Ok(new { id });
});

app.MapPost("/api/upload/audio", async (HttpContext context, AuthStore authStore, IWebHostEnvironment env) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (!context.Request.HasFormContentType) return Results.BadRequest("Thiếu dữ liệu multipart/form-data.");

    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var file = form.Files["file"];
    if (file is null) return Results.BadRequest("Không tìm thấy file audio.");

    try
    {
        var relativePath = await SaveUploadedAudioAsync(file, env, context.RequestAborted);
        if (string.IsNullOrWhiteSpace(relativePath))
            return Results.BadRequest("File audio rỗng.");
        return Results.Ok(new { audioUrl = relativePath });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(ex.Message);
    }
});

app.MapPost("/api/upload/poi-image", async (HttpContext context, AuthStore authStore, IWebHostEnvironment env) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (!context.Request.HasFormContentType) return Results.BadRequest("Thiếu dữ liệu multipart/form-data.");

    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var file = form.Files["file"];
    if (file is null) return Results.BadRequest("Không tìm thấy file ảnh.");

    try
    {
        var relativePath = await SaveUploadedPoiImageAsync(file, env, context.RequestAborted);
        if (string.IsNullOrWhiteSpace(relativePath))
            return Results.BadRequest("File ảnh rỗng.");
        return Results.Ok(new { imagePath = relativePath });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(ex.Message);
    }
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
    static string ResolvePasswordHint(string username, string passwordHash)
    {
        var u = (username ?? "").Trim().ToLowerInvariant();
        static bool HashMatches(string rawPassword, string hashFromDb) =>
            string.Equals(PasswordTools.Hash(rawPassword), hashFromDb, StringComparison.Ordinal);

        // Ưu tiên đọc theo hash hiện có trong DB để web phản ánh đúng mật khẩu đang dùng.
        if (HashMatches("admin123", passwordHash)) return "admin123";
        if (HashMatches("chuquan123", passwordHash)) return "chuquan123";
        if (HashMatches("VkQuan@123", passwordHash)) return "VkQuan@123";

        // Dự phòng theo username cho DB cũ/chưa đồng bộ hash.
        if (u == "admin") return "admin123";
        if (u is "chuquan1" or "chuquan2") return "chuquan123";
        if (u.StartsWith("owner_oc_") || u is "owner_sui_cao_tan_tong_loi" or "owner_lau_bo_khu_nha_chay")
            return "VkQuan@123";
        return "";
    }

    return Results.Ok(accounts.Select(x => new
    {
        x.Id,
        x.Username,
        passwordHash = x.PasswordHash,
        passwordHint = ResolvePasswordHint(x.Username, x.PasswordHash),
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

app.MapPost("/api/translations/{id:int}/regenerate", async (HttpContext context, int id, TravelGuideDb db, AuthStore authStore, IHttpClientFactory httpClientFactory, string? lang) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    var existing = await db.GetPoiAsync(id);
    if (existing is null) return Results.NotFound();
    if (principal.Role != "admin" && existing.OwnerUserId != principal.UserId)
        return Results.Forbid();

    var cleared = await db.ClearTranslationsAsync(id, lang);
    if (!cleared)
        return Results.BadRequest("Ngôn ngữ không hợp lệ. Chỉ hỗ trợ en/ja/ko/zh.");

    await db.EnsureAutoTranslationsAsync(httpClientFactory.CreateClient(), id, lang);
    var poi = await db.GetPoiAsync(id);
    return poi is null ? Results.NotFound() : Results.Ok(poi);
});

app.MapGet("/api/export/extra_places.json", async (HttpContext context, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();
    var places = await db.GetExportPlacesAsync();
    return Results.Json(places);
});

app.MapGet("/api/tourists/overview", async (HttpContext context, TravelGuideDb db, AuthStore authStore, DateOnly? visitHistoryChartWeekStart, DateOnly? touristWeekChartMonday) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();
    var data = await db.GetTouristOverviewAsync(visitHistoryChartWeekStart, touristWeekChartMonday);
    return Results.Ok(data);
});

app.MapPut("/api/tourists/{id:int}/tier", async (HttpContext context, int id, UpdateTouristTierRequest request, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var ok = await db.UpdateTouristUserTierAsync(id, request.AccountTier);
    return ok
        ? Results.Ok(new { message = "Đã cập nhật tier du khách." })
        : Results.NotFound("Không tìm thấy tài khoản du khách.");
});

app.MapPost("/api/tourists/purge-legacy", async (HttpContext context, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();
    var result = await db.PurgeLegacyTouristUsersAsync();
    return Results.Ok(result);
});

app.MapGet("/api/tourists/poi-scan-dashboard", async (HttpContext context, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();
    var data = await db.GetTouristPoiScanDashboardAsync();
    return Results.Ok(data);
});

app.MapGet("/api/comments", async (HttpContext context, TravelGuideDb db, AuthStore authStore, string? status, string? search, int page = 1, int pageSize = 20) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();
    var data = await db.GetCommentsAsync(status, search, page, pageSize);
    return Results.Ok(data);
});

app.MapPut("/api/comments/{id:long}/status", async (HttpContext context, long id, UpdateCommentStatusRequest request, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();
    var ok = await db.UpdateCommentStatusAsync(id, request.Status, request.Reason);
    return ok ? Results.Ok(new { message = "Đã cập nhật trạng thái bình luận." }) : Results.NotFound("Không tìm thấy bình luận.");
});

app.MapPut("/api/comments/{id:long}/reply", async (HttpContext context, long id, ReplyCommentRequest request, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();
    var ok = await db.ReplyCommentAsync(id, request.Reply);
    return ok ? Results.Ok(new { message = "Đã lưu phản hồi admin." }) : Results.NotFound("Không tìm thấy bình luận.");
});

app.MapPost("/api/comments", async (HttpContext context, CreateCommentRequest request, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();
    var id = await db.CreateCommentAsync(request);
    return id > 0 ? Results.Ok(new { id, message = "Đã tạo bình luận." }) : Results.BadRequest("Dữ liệu bình luận không hợp lệ.");
});

app.MapDelete("/api/comments/{id:long}", async (HttpContext context, long id, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();
    var ok = await db.DeleteCommentAsync(id);
    return ok ? Results.Ok(new { message = "Đã xóa bình luận." }) : Results.NotFound("Không tìm thấy bình luận.");
});

app.MapPost("/api/tts/queue", async (HttpContext context, TtsQueueEnqueueRequest request, ITtsQueue queue, AuthStore authStore) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest("Text không được rỗng.");
    }

    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();

    var job = new TtsQueueJob
    {
        Text = request.Text.Trim(),
        Voice = string.IsNullOrWhiteSpace(request.Voice) ? "vi-VN" : request.Voice.Trim()
    };

    var accepted = await queue.EnqueueAsync(job, context.RequestAborted);
    if (!accepted)
    {
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    return Results.Accepted($"/api/tts/queue/{job.Id}", new
    {
        jobId = job.Id,
        status = job.Status
    });
});

app.MapGet("/api/tts/queue/metrics", (HttpContext context, ITtsQueue queue, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();

    return Results.Ok(queue.GetMetricsSnapshot());
});

app.MapGet("/api/tts/queue/{id:guid}", (HttpContext context, Guid id, ITtsQueue queue, AuthStore authStore) =>
{
    var principal = AuthHelper.Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();

    return queue.TryGetJob(id, out var job) && job is not null
        ? Results.Ok(job)
        : Results.NotFound();
});

app.Run();
