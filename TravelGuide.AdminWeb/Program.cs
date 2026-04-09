using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<AuthStore>();
builder.Services.AddSingleton<TravelGuideDb>();
builder.Services.AddHttpClient();

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
    var user = await db.GetUserByUsernameAsync(request.Username);
    if (user is null || user.PasswordHash != PasswordTools.Hash(request.Password))
    {
        return Results.Unauthorized();
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

app.MapGet("/api/pois", async (HttpContext context, TravelGuideDb db, AuthStore authStore, IHttpClientFactory httpClientFactory, string? lang) =>
{
    var principal = Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    await db.EnsureAutoTranslationsAsync(httpClientFactory.CreateClient(), targetLang: lang);
    var pois = await db.GetPoisAsync();
    return Results.Ok(pois);
});

app.MapGet("/api/public/pois", async (TravelGuideDb db, IHttpClientFactory httpClientFactory, string? lang) =>
{
    await db.EnsureAutoTranslationsAsync(httpClientFactory.CreateClient(), targetLang: lang);
    var pois = await db.GetPoisAsync();
    return Results.Ok(pois);
});

app.MapPost("/api/pois", async (HttpContext context, PoiDto poi, TravelGuideDb db, AuthStore authStore, IHttpClientFactory httpClientFactory) =>
{
    var principal = Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var id = await db.CreatePoiAsync(poi);
    await db.EnsureAutoTranslationsAsync(httpClientFactory.CreateClient(), id);
    return Results.Ok(new { id });
});

app.MapPut("/api/pois/{id:int}", async (HttpContext context, int id, PoiDto poi, TravelGuideDb db, AuthStore authStore, IHttpClientFactory httpClientFactory) =>
{
    var principal = Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var updated = await db.UpdatePoiAsync(id, poi);
    if (updated) await db.EnsureAutoTranslationsAsync(httpClientFactory.CreateClient(), id);
    return updated ? Results.Ok() : Results.NotFound();
});

app.MapDelete("/api/pois/{id:int}", async (HttpContext context, int id, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var deleted = await db.DeletePoiAsync(id);
    return deleted ? Results.Ok() : Results.NotFound();
});

app.MapGet("/api/audio", async (HttpContext context, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    var audio = await db.GetAudioAsync();
    return Results.Ok(audio);
});

app.MapGet("/api/accounts", async (HttpContext context, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var accounts = await db.GetUsersAsync();
    return Results.Ok(accounts.Select(x => new { x.Id, x.Username, x.DisplayName, x.Role }));
});

app.MapPost("/api/accounts", async (HttpContext context, CreateAccountRequest request, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var ok = await db.CreateUserAsync(request.Username, request.Password, request.DisplayName, request.Role);
    return ok ? Results.Ok() : Results.BadRequest("Username already exists.");
});

app.MapPut("/api/accounts/{id:int}", async (HttpContext context, int id, UpdateAccountRequest request, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var updated = await db.UpdateUserAsync(id, request.DisplayName, request.Role, request.NewPassword);
    return updated ? Results.Ok() : Results.NotFound();
});

app.MapDelete("/api/accounts/{id:int}", async (HttpContext context, int id, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var deleted = await db.DeleteUserAsync(id);
    return deleted ? Results.Ok() : Results.NotFound();
});

app.MapGet("/api/translations/{id:int}", async (HttpContext context, int id, TravelGuideDb db, AuthStore authStore, IHttpClientFactory httpClientFactory, string? lang) =>
{
    var principal = Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    await db.EnsureAutoTranslationsAsync(httpClientFactory.CreateClient(), id, lang);
    var poi = await db.GetPoiAsync(id);
    return poi is null ? Results.NotFound() : Results.Ok(poi);
});

app.MapPut("/api/translations/{id:int}", async (HttpContext context, int id, TranslationUpdateRequest request, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    if (principal.Role != "admin") return Results.Forbid();

    var updated = await db.UpdateTranslationsAsync(id, request);
    return updated ? Results.Ok() : Results.NotFound();
});

app.MapGet("/api/export/extra_places.json", async (HttpContext context, TravelGuideDb db, AuthStore authStore) =>
{
    var principal = Authenticate(context, authStore);
    if (principal is null) return Results.Unauthorized();
    var places = await db.GetExportPlacesAsync();
    return Results.Json(places);
});

app.Run();

static AuthPrincipal? Authenticate(HttpContext context, AuthStore authStore)
{
    var authHeader = context.Request.Headers.Authorization.ToString();
    if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return null;

    var token = authHeader["Bearer ".Length..].Trim();
    return authStore.GetPrincipal(token);
}

sealed class AuthStore
{
    private readonly ConcurrentDictionary<string, AuthPrincipal> _tokens = new();

    public string CreateToken(UserAccount user)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        _tokens[token] = new AuthPrincipal(user.Id, user.Username, user.Role);
        return token;
    }

    public AuthPrincipal? GetPrincipal(string token)
    {
        return _tokens.TryGetValue(token, out var principal) ? principal : null;
    }
}

sealed class TravelGuideDb
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _translationLock = new(1, 1);

    public TravelGuideDb()
    {
        var dbPath = Path.Combine(AppContext.BaseDirectory, "travelguide-admin.db");
        _connectionString = $"Data Source={dbPath}";
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = """
                  CREATE TABLE IF NOT EXISTS Poi(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    NameVi TEXT NOT NULL,
                    NameEn TEXT NOT NULL DEFAULT '',
                    NameJa TEXT NOT NULL DEFAULT '',
                    NameKo TEXT NOT NULL DEFAULT '',
                    NameZh TEXT NOT NULL DEFAULT '',
                    DescVi TEXT NOT NULL,
                    DescEn TEXT NOT NULL DEFAULT '',
                    DescJa TEXT NOT NULL DEFAULT '',
                    DescKo TEXT NOT NULL DEFAULT '',
                    DescZh TEXT NOT NULL DEFAULT '',
                    Latitude REAL NOT NULL,
                    Longitude REAL NOT NULL,
                    Radius REAL NOT NULL DEFAULT 50,
                    ImagePath TEXT NOT NULL DEFAULT '',
                    AudioUrl TEXT NOT NULL DEFAULT ''
                  );

                  CREATE TABLE IF NOT EXISTS UserAccount(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL UNIQUE,
                    PasswordHash TEXT NOT NULL,
                    DisplayName TEXT NOT NULL,
                    Role TEXT NOT NULL
                  );
                  """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();

        var hasAdmin = await GetUserByUsernameAsync("admin");
        if (hasAdmin is null)
        {
            await CreateUserAsync("admin", "admin123", "Administrator", "admin");
        }

        var hasData = (await GetPoisAsync()).Count > 0;
        if (!hasData)
        {
            await SeedPoisAsync();
        }
    }

    public async Task<List<PoiDto>> GetPoisAsync()
    {
        var result = new List<PoiDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, NameVi, NameEn, NameJa, NameKo, NameZh, DescVi, DescEn, DescJa, DescKo, DescZh, Latitude, Longitude, Radius, ImagePath, AudioUrl FROM Poi ORDER BY Id";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new PoiDto(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetDouble(11),
                reader.GetDouble(12),
                reader.GetDouble(13),
                reader.GetString(14),
                reader.GetString(15)
            ));
        }

        return result;
    }

    public async Task<PoiDto?> GetPoiAsync(int id)
    {
        return (await GetPoisAsync()).FirstOrDefault(x => x.Id == id);
    }

    public async Task<int> CreatePoiAsync(PoiDto poi)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO Poi(NameVi, NameEn, NameJa, NameKo, NameZh, DescVi, DescEn, DescJa, DescKo, DescZh, Latitude, Longitude, Radius, ImagePath, AudioUrl)
                          VALUES ($nameVi, $nameEn, $nameJa, $nameKo, $nameZh, $descVi, $descEn, $descJa, $descKo, $descZh, $lat, $lon, $radius, $imagePath, $audioUrl);
                          SELECT last_insert_rowid();
                          """;
        BindPoi(cmd, poi);
        var id = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        return (int)id;
    }

    public async Task<bool> UpdatePoiAsync(int id, PoiDto poi)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          UPDATE Poi SET
                            NameVi = $nameVi, NameEn = $nameEn, NameJa = $nameJa, NameKo = $nameKo, NameZh = $nameZh,
                            DescVi = $descVi, DescEn = $descEn, DescJa = $descJa, DescKo = $descKo, DescZh = $descZh,
                            Latitude = $lat, Longitude = $lon, Radius = $radius, ImagePath = $imagePath, AudioUrl = $audioUrl
                          WHERE Id = $id;
                          """;
        BindPoi(cmd, poi);
        cmd.Parameters.AddWithValue("$id", id);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<bool> DeletePoiAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Poi WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> UpdateTranslationsAsync(int id, TranslationUpdateRequest request)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          UPDATE Poi SET
                            NameEn = $nameEn, NameJa = $nameJa, NameKo = $nameKo, NameZh = $nameZh,
                            DescEn = $descEn, DescJa = $descJa, DescKo = $descKo, DescZh = $descZh
                          WHERE Id = $id;
                          """;
        cmd.Parameters.AddWithValue("$nameEn", request.NameEn);
        cmd.Parameters.AddWithValue("$nameJa", request.NameJa);
        cmd.Parameters.AddWithValue("$nameKo", request.NameKo);
        cmd.Parameters.AddWithValue("$nameZh", request.NameZh);
        cmd.Parameters.AddWithValue("$descEn", request.DescEn);
        cmd.Parameters.AddWithValue("$descJa", request.DescJa);
        cmd.Parameters.AddWithValue("$descKo", request.DescKo);
        cmd.Parameters.AddWithValue("$descZh", request.DescZh);
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<object>> GetAudioAsync()
    {
        var pois = await GetPoisAsync();
        return pois.Select(x => (object)new
        {
            x.Id,
            x.NameVi,
            x.AudioUrl
        }).ToList();
    }

    public async Task<List<ExportPoi>> GetExportPlacesAsync()
    {
        var pois = await GetPoisAsync();
        return pois.Select(x => new ExportPoi(
            x.NameVi, x.DescVi, x.Latitude, x.Longitude, x.Radius, x.ImagePath
        )).ToList();
    }

    public async Task<List<UserAccount>> GetUsersAsync()
    {
        var result = new List<UserAccount>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Username, PasswordHash, DisplayName, Role FROM UserAccount ORDER BY Id";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new UserAccount(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)
            ));
        }

        return result;
    }

    public async Task<UserAccount?> GetUserByUsernameAsync(string username)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Username, PasswordHash, DisplayName, Role FROM UserAccount WHERE Username = $username";
        cmd.Parameters.AddWithValue("$username", username);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new UserAccount(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4)
        );
    }

    public async Task<bool> CreateUserAsync(string username, string password, string displayName, string role)
    {
        var existing = await GetUserByUsernameAsync(username);
        if (existing is not null) return false;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO UserAccount(Username, PasswordHash, DisplayName, Role) VALUES ($username, $hash, $displayName, $role)";
        cmd.Parameters.AddWithValue("$username", username);
        cmd.Parameters.AddWithValue("$hash", PasswordTools.Hash(password));
        cmd.Parameters.AddWithValue("$displayName", displayName);
        cmd.Parameters.AddWithValue("$role", role is "admin" or "owner" ? role : "owner");
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<bool> UpdateUserAsync(int id, string displayName, string role, string? newPassword)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            cmd.CommandText = "UPDATE UserAccount SET DisplayName = $displayName, Role = $role WHERE Id = $id";
        }
        else
        {
            cmd.CommandText = "UPDATE UserAccount SET DisplayName = $displayName, Role = $role, PasswordHash = $hash WHERE Id = $id";
            cmd.Parameters.AddWithValue("$hash", PasswordTools.Hash(newPassword));
        }

        cmd.Parameters.AddWithValue("$displayName", displayName);
        cmd.Parameters.AddWithValue("$role", role is "admin" or "owner" ? role : "owner");
        cmd.Parameters.AddWithValue("$id", id);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteUserAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM UserAccount WHERE Id = $id AND Username <> 'admin'";
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task EnsureAutoTranslationsAsync(HttpClient httpClient, int? onlyPoiId = null, string? targetLang = null)
    {
        await _translationLock.WaitAsync();
        try
        {
            var pois = await GetPoisAsync();
            if (onlyPoiId.HasValue)
                pois = pois.Where(p => p.Id == onlyPoiId.Value).ToList();

            foreach (var poi in pois)
            {
                if (string.IsNullOrWhiteSpace(poi.NameVi) || string.IsNullOrWhiteSpace(poi.DescVi))
                    continue;

                var next = poi with { };
                var changed = false;
                var langs = string.IsNullOrWhiteSpace(targetLang)
                    ? new[] { "en", "ja", "ko", "zh" }
                    : new[] { targetLang.ToLowerInvariant() };

                foreach (var lang in langs)
                {
                    if (lang is not ("en" or "ja" or "ko" or "zh")) continue;
                    (next, var c) = await FillLangAsync(httpClient, next, lang);
                    changed |= c;
                }

                if (!changed) continue;

                await UpdateTranslationsAsync(poi.Id, new TranslationUpdateRequest(
                    next.NameEn, next.NameJa, next.NameKo, next.NameZh,
                    next.DescEn, next.DescJa, next.DescKo, next.DescZh
                ));
            }
        }
        finally
        {
            _translationLock.Release();
        }
    }

    private static async Task<(PoiDto Poi, bool Changed)> FillLangAsync(HttpClient http, PoiDto poi, string lang)
    {
        var changed = false;

        if (string.IsNullOrWhiteSpace(GetNameByLang(poi, lang)))
        {
            var translated = await MyMemoryTranslator.TranslateAsync(http, poi.NameVi, lang);
            if (!string.IsNullOrWhiteSpace(translated))
            {
                SetNameByLang(ref poi, lang, translated);
                changed = true;
            }
        }

        if (string.IsNullOrWhiteSpace(GetDescByLang(poi, lang)))
        {
            var translated = await MyMemoryTranslator.TranslateAsync(http, poi.DescVi, lang);
            if (!string.IsNullOrWhiteSpace(translated))
            {
                SetDescByLang(ref poi, lang, translated);
                changed = true;
            }
        }

        return (poi, changed);
    }

    private static string GetNameByLang(PoiDto poi, string lang) => lang switch
    {
        "en" => poi.NameEn,
        "ja" => poi.NameJa,
        "ko" => poi.NameKo,
        "zh" => poi.NameZh,
        _ => ""
    };

    private static string GetDescByLang(PoiDto poi, string lang) => lang switch
    {
        "en" => poi.DescEn,
        "ja" => poi.DescJa,
        "ko" => poi.DescKo,
        "zh" => poi.DescZh,
        _ => ""
    };

    private static void SetNameByLang(ref PoiDto poi, string lang, string value)
    {
        poi = lang switch
        {
            "en" => poi with { NameEn = value },
            "ja" => poi with { NameJa = value },
            "ko" => poi with { NameKo = value },
            "zh" => poi with { NameZh = value },
            _ => poi
        };
    }

    private static void SetDescByLang(ref PoiDto poi, string lang, string value)
    {
        poi = lang switch
        {
            "en" => poi with { DescEn = value },
            "ja" => poi with { DescJa = value },
            "ko" => poi with { DescKo = value },
            "zh" => poi with { DescZh = value },
            _ => poi
        };
    }

    private static void BindPoi(SqliteCommand cmd, PoiDto poi)
    {
        cmd.Parameters.AddWithValue("$nameVi", poi.NameVi);
        cmd.Parameters.AddWithValue("$nameEn", poi.NameEn ?? string.Empty);
        cmd.Parameters.AddWithValue("$nameJa", poi.NameJa ?? string.Empty);
        cmd.Parameters.AddWithValue("$nameKo", poi.NameKo ?? string.Empty);
        cmd.Parameters.AddWithValue("$nameZh", poi.NameZh ?? string.Empty);
        cmd.Parameters.AddWithValue("$descVi", poi.DescVi);
        cmd.Parameters.AddWithValue("$descEn", poi.DescEn ?? string.Empty);
        cmd.Parameters.AddWithValue("$descJa", poi.DescJa ?? string.Empty);
        cmd.Parameters.AddWithValue("$descKo", poi.DescKo ?? string.Empty);
        cmd.Parameters.AddWithValue("$descZh", poi.DescZh ?? string.Empty);
        cmd.Parameters.AddWithValue("$lat", poi.Latitude);
        cmd.Parameters.AddWithValue("$lon", poi.Longitude);
        cmd.Parameters.AddWithValue("$radius", poi.Radius);
        cmd.Parameters.AddWithValue("$imagePath", poi.ImagePath ?? string.Empty);
        cmd.Parameters.AddWithValue("$audioUrl", poi.AudioUrl ?? string.Empty);
    }

    private async Task SeedPoisAsync()
    {
        var seed = new[]
        {
            new PoiDto(0, "Cổng chào Phố Ẩm thực Vĩnh Khánh", "", "", "", "", "Điểm chào đầu tuyến phố ẩm thực Vĩnh Khánh.", "", "", "", "", 10.7595, 106.7012, 80, "gatevinhkhanh.jpg", ""),
            new PoiDto(0, "Ốc Oanh", "", "", "", "", "Quán ốc nổi tiếng với món càng ghẹ rang muối.", "", "", "", "", 10.7588, 106.7018, 50, "ocoanh.jpg", ""),
            new PoiDto(0, "Cafe Era", "", "", "", "", "Không gian cà phê thư giãn giữa tuyến phố.", "", "", "", "", 10.7585, 106.7025, 45, "cafeera.jpg", "")
        };

        foreach (var poi in seed)
        {
            await CreatePoiAsync(poi);
        }
    }
}


static class MyMemoryTranslator
{
    public static async Task<string> TranslateAsync(HttpClient httpClient, string sourceText, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return sourceText;
        var pair = $"vi|{targetLang}";
        var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(sourceText)}&langpair={pair}";
        try
        {
            var json = await httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("responseStatus").GetInt32() != 200) return "";
            return root.GetProperty("responseData").GetProperty("translatedText").GetString() ?? "";
        }
        catch
        {
            return "";
        }
    }
}

static class PasswordTools
{
    public static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}

record AuthPrincipal(int UserId, string Username, string Role);
record LoginRequest(string Username, string Password);
record CreateAccountRequest(string Username, string Password, string DisplayName, string Role);
record UpdateAccountRequest(string DisplayName, string Role, string? NewPassword);
record TranslationUpdateRequest(string NameEn, string NameJa, string NameKo, string NameZh, string DescEn, string DescJa, string DescKo, string DescZh);
record UserAccount(int Id, string Username, string PasswordHash, string DisplayName, string Role);
record ExportPoi(string NameVi, string DescVi, double Latitude, double Longitude, double Radius, string ImagePath);
record PoiDto(
    int Id,
    string NameVi,
    string NameEn,
    string NameJa,
    string NameKo,
    string NameZh,
    string DescVi,
    string DescEn,
    string DescJa,
    string DescKo,
    string DescZh,
    double Latitude,
    double Longitude,
    double Radius,
    string ImagePath,
    string AudioUrl
);
