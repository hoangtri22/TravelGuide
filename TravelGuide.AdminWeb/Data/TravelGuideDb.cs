using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TravelGuide.AdminWeb.Models;
using TravelGuide.AdminWeb.Services;

namespace TravelGuide.AdminWeb.Data;

/// <summary>
/// Truy cập SQLite <c>travelguide-admin.db</c>: bảng Poi, UserAccount; seed admin/POI; dịch tự động MyMemory.
/// </summary>
public sealed class TravelGuideDb
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _translationLock = new(1, 1);

    /// <summary>Bỏ khoảng trắng đầu/cuối và ký tự zero-width hay gặp khi copy từ Word/Excel.</summary>
    public static string NormalizeLoginUsername(string? username)
    {
        if (string.IsNullOrEmpty(username)) return "";
        var t = username.Trim();
        var sb = new StringBuilder(t.Length);
        foreach (var c in t)
        {
            if (c is '\u200B' or '\u200C' or '\u200D' or '\uFEFF') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Chuỗi kết nối tới file DB trong thư mục chạy ứng dụng.</summary>
    public TravelGuideDb()
    {
        var dbPath = Path.Combine(AppContext.BaseDirectory, "travelguide-admin.db");
        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>Tạo bảng/cột nếu thiếu, seed admin + POI mẫu khi DB trống.</summary>
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
                    AudioUrl TEXT NOT NULL DEFAULT '',
                    Status TEXT NOT NULL DEFAULT 'published',
                    RejectReason TEXT NOT NULL DEFAULT '',
                    OwnerUserId INTEGER NOT NULL DEFAULT 0,
                    Priority INTEGER NOT NULL DEFAULT 0,
                    MapLink TEXT NOT NULL DEFAULT ''
                  );

                  CREATE TABLE IF NOT EXISTS UserAccount(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Username TEXT NOT NULL UNIQUE,
                    PasswordHash TEXT NOT NULL,
                    DisplayName TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    IsLocked INTEGER NOT NULL DEFAULT 0,
                    RegistrationApproved INTEGER NOT NULL DEFAULT 1
                  );
                  """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();

        await EnsurePoiColumnsAsync(connection);
        await EnsureUserAccountColumnsAsync(connection);

        var hasAdmin = await GetUserByUsernameAsync("admin");
        if (hasAdmin is null)
        {
            await CreateUserAsync("admin", "admin123", "Administrator", "admin");
        }

        await EnsureDemoOwnerAccountsAsync();
        await EnsurePhoOwnerDemoPasswordsAsync();

        await SyncPoisWithExtraPlacesFileAsync();

        var hasData = (await GetPoisAsync()).Count > 0;
        if (!hasData)
        {
            await SeedPoisAsync();
        }
    }

    /// <summary>Bản ghi trong <c>extra_places.json</c> (cùng schema app MAUI).</summary>
    private sealed record ExtraPlaceJson(
        string NameVi,
        string DescVi,
        double Latitude,
        double Longitude,
        double Radius,
        string ImagePath,
        string AudioUrl,
        int Priority,
        string MapLink);

    /// <summary>Ánh xạ tên POI (VI) → username chủ quán phố; POI không có trong bảng → <c>OwnerUserId = 0</c> (điểm chung).</summary>
    private static readonly (string NameVi, string OwnerUsername)[] StreetVendorPoiOwners =
    [
        ("Ốc Oanh", "owner_oc_oanh"),
        ("Ốc Đào 2", "owner_oc_dao_2"),
        ("Sủi cảo Tân Tòng Lợi", "owner_sui_cao_tan_tong_loi"),
        ("Lẩu bò Khu Nhà Cháy", "owner_lau_bo_khu_nha_chay"),
        ("Ốc Vũ", "owner_oc_vu"),
        ("Ốc Linh", "owner_oc_linh")
    ];

    /// <summary>
    /// Đồng bộ POI trong SQLite với <c>extra_places.json</c> (bản copy cùng app): thêm bản ghi thiếu,
    /// gán <c>OwnerUserId</c> cho chủ quán khi đang là 0 — web chủ quán thấy đúng dữ liệu như app.
    /// </summary>
    private async Task SyncPoisWithExtraPlacesFileAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "extra_places.json");
        if (!File.Exists(path)) return;

        List<ExtraPlaceJson> rows;
        try
        {
            await using var stream = File.OpenRead(path);
            rows = await JsonSerializer.DeserializeAsync<List<ExtraPlaceJson>>(stream,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? [];
        }
        catch
        {
            return;
        }

        if (rows.Count == 0) return;

        var ownerIdByUsername = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, username) in StreetVendorPoiOwners)
        {
            var acc = await GetUserByUsernameAsync(username);
            if (acc is not null)
                ownerIdByUsername[username] = acc.Id;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.NameVi)) continue;

            var mappedOwnerId = 0;
            foreach (var (name, username) in StreetVendorPoiOwners)
            {
                if (!string.Equals(name, row.NameVi, StringComparison.Ordinal)) continue;
                if (ownerIdByUsername.TryGetValue(username, out var oid))
                    mappedOwnerId = oid;
                break;
            }

            int? existingId = null;
            int existingOwner = 0;
            await using (var q = connection.CreateCommand())
            {
                q.CommandText = "SELECT Id, OwnerUserId FROM Poi WHERE NameVi = $n LIMIT 1";
                q.Parameters.AddWithValue("$n", row.NameVi);
                await using var reader = await q.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    existingId = reader.GetInt32(0);
                    existingOwner = reader.GetInt32(1);
                }
            }

            if (existingId is null)
            {
                await using var ins = connection.CreateCommand();
                ins.CommandText = """
                                    INSERT INTO Poi(NameVi, NameEn, NameJa, NameKo, NameZh, DescVi, DescEn, DescJa, DescKo, DescZh, Latitude, Longitude, Radius, ImagePath, AudioUrl, Status, RejectReason, OwnerUserId, Priority, MapLink)
                                    VALUES ($nameVi, '', '', '', '', $descVi, '', '', '', '', $lat, $lon, $radius, $imagePath, $audioUrl, 'published', '', $ownerUserId, $priority, $mapLink);
                                    """;
                ins.Parameters.AddWithValue("$nameVi", row.NameVi);
                ins.Parameters.AddWithValue("$descVi", row.DescVi ?? "");
                ins.Parameters.AddWithValue("$lat", row.Latitude);
                ins.Parameters.AddWithValue("$lon", row.Longitude);
                ins.Parameters.AddWithValue("$radius", row.Radius);
                ins.Parameters.AddWithValue("$imagePath", row.ImagePath ?? "");
                ins.Parameters.AddWithValue("$audioUrl", row.AudioUrl ?? "");
                ins.Parameters.AddWithValue("$priority", row.Priority);
                ins.Parameters.AddWithValue("$mapLink", row.MapLink ?? "");
                ins.Parameters.AddWithValue("$ownerUserId", mappedOwnerId);
                await ins.ExecuteNonQueryAsync();
            }
            else if (existingOwner == 0 && mappedOwnerId != 0)
            {
                await using var up = connection.CreateCommand();
                up.CommandText = "UPDATE Poi SET OwnerUserId = $oid WHERE Id = $id AND OwnerUserId = 0";
                up.Parameters.AddWithValue("$oid", mappedOwnerId);
                up.Parameters.AddWithValue("$id", existingId.Value);
                await up.ExecuteNonQueryAsync();
            }
        }
    }

    /// <summary>Đọc danh sách POI; lọc <c>published</c> và theo owner khi cần.</summary>
    public async Task<List<PoiDto>> GetPoisAsync(bool includeUnpublished = true, int? ownerUserId = null)
    {
        var result = new List<PoiDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        var where = new List<string>();
        if (!includeUnpublished)
        {
            where.Add("Status = 'published'");
            if (ownerUserId.HasValue)
                where.Add("(OwnerUserId = 0 OR OwnerUserId = $ownerUserId)");
        }

        cmd.CommandText =
            "SELECT Id, NameVi, NameEn, NameJa, NameKo, NameZh, DescVi, DescEn, DescJa, DescKo, DescZh, Latitude, Longitude, Radius, ImagePath, AudioUrl, Status, RejectReason, OwnerUserId, Priority, MapLink FROM Poi"
            + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "")
            + " ORDER BY Id";
        if (ownerUserId.HasValue)
            cmd.Parameters.AddWithValue("$ownerUserId", ownerUserId.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(ReadPoiDto(reader));

        return result;
    }

    /// <summary>POI do một chủ quán tạo — mọi trạng thái (pending / published / rejected).</summary>
    public async Task<List<PoiDto>> GetPoisOwnedByUserAsync(int ownerUserId)
    {
        var result = new List<PoiDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT Id, NameVi, NameEn, NameJa, NameKo, NameZh, DescVi, DescEn, DescJa, DescKo, DescZh, Latitude, Longitude, Radius, ImagePath, AudioUrl, Status, RejectReason, OwnerUserId, Priority, MapLink FROM Poi WHERE OwnerUserId = $oid ORDER BY Id";
        cmd.Parameters.AddWithValue("$oid", ownerUserId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(ReadPoiDto(reader));
        return result;
    }

    /// <summary>Một POI theo Id (đọc trực tiếp từ DB).</summary>
    public async Task<PoiDto?> GetPoiAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT Id, NameVi, NameEn, NameJa, NameKo, NameZh, DescVi, DescEn, DescJa, DescKo, DescZh, Latitude, Longitude, Radius, ImagePath, AudioUrl, Status, RejectReason, OwnerUserId, Priority, MapLink FROM Poi WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return ReadPoiDto(reader);
    }

    private static PoiDto ReadPoiDto(SqliteDataReader reader) =>
        new(
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
            reader.GetString(15),
            reader.GetString(16),
            reader.GetString(17),
            reader.GetInt32(18),
            reader.GetInt32(19),
            reader.GetString(20)
        );

    /// <summary>Thêm POI; admin → published, owner → pending và gán OwnerUserId.</summary>
    public async Task<int> CreatePoiAsync(PoiDto poi, AuthPrincipal principal)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO Poi(NameVi, NameEn, NameJa, NameKo, NameZh, DescVi, DescEn, DescJa, DescKo, DescZh, Latitude, Longitude, Radius, ImagePath, AudioUrl, Status, RejectReason, OwnerUserId, Priority, MapLink)
                          VALUES ($nameVi, $nameEn, $nameJa, $nameKo, $nameZh, $descVi, $descEn, $descJa, $descKo, $descZh, $lat, $lon, $radius, $imagePath, $audioUrl, $status, $rejectReason, $ownerUserId, $priority, $mapLink);
                          SELECT last_insert_rowid();
                          """;
        BindPoi(cmd, poi);
        var status = principal.Role == "admin" ? "published" : "pending";
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$rejectReason", "");
        cmd.Parameters.AddWithValue("$ownerUserId", principal.Role == "owner" ? principal.UserId : 0);
        var id = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        return (int)id;
    }

    /// <summary>Cập nhật nội dung/vị trí POI; owner chỉ sửa bản nháp của mình.</summary>
    public async Task<bool> UpdatePoiAsync(int id, PoiDto poi, AuthPrincipal principal)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var existing = await GetPoiByIdInternalAsync(connection, id);
        if (existing is null) return false;

        if (principal.Role != "admin")
        {
            if (existing.OwnerUserId != principal.UserId) return false;
            // Owner được sửa nội dung cả khi đã published (trước đây bị chặn → nút Sửa/Lưu như không hoạt động).
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          UPDATE Poi SET
                            NameVi = $nameVi, NameEn = $nameEn, NameJa = $nameJa, NameKo = $nameKo, NameZh = $nameZh,
                            DescVi = $descVi, DescEn = $descEn, DescJa = $descJa, DescKo = $descKo, DescZh = $descZh,
                            Latitude = $lat, Longitude = $lon, Radius = $radius, ImagePath = $imagePath, AudioUrl = $audioUrl,
                            Priority = $priority, MapLink = $mapLink
                          WHERE Id = $id;
                          """;
        BindPoi(cmd, poi);
        cmd.Parameters.AddWithValue("$id", id);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    /// <summary>Xóa POI theo Id (chỉ admin qua endpoint).</summary>
    public async Task<bool> DeletePoiAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Poi WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Đặt trạng thái duyệt (published/rejected) và lý do từ chối.</summary>
    public async Task<bool> SetPoiStatusAsync(int id, string status, string rejectReason)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Poi SET Status = $status, RejectReason = $rejectReason WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$rejectReason", rejectReason ?? "");
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Cập nhật chỉ các cột tên/mô tả ngoài tiếng Việt.</summary>
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

    /// <summary>Danh sách Id/NameVi/AudioUrl cho màn quản lý audio.</summary>
    public async Task<List<object>> GetAudioAsync()
    {
        var pois = await GetPoisAsync(includeUnpublished: true);
        return ToAudioList(pois);
    }

    /// <summary>Danh sách audio chỉ trong phạm vi POI của chủ quán.</summary>
    public async Task<List<object>> GetAudioAsyncForOwner(int ownerUserId)
    {
        var pois = await GetPoisOwnedByUserAsync(ownerUserId);
        return ToAudioList(pois);
    }

    private static List<object> ToAudioList(List<PoiDto> pois) =>
        pois.Select(x => (object)new { x.Id, x.NameVi, x.AudioUrl }).ToList();

    /// <summary>POI đã publish dạng rút gọn để export JSON.</summary>
    public async Task<List<ExportPoi>> GetExportPlacesAsync()
    {
        var pois = await GetPoisAsync(includeUnpublished: false);
        return pois.Select(x => new ExportPoi(
            x.NameVi, x.DescVi, x.Latitude, x.Longitude, x.Radius, x.ImagePath ?? "",
            x.AudioUrl ?? "", x.Priority, x.MapLink ?? ""
        )).ToList();
    }

    /// <summary>Tất cả user (kèm hash — chỉ dùng nội bộ admin).</summary>
    public async Task<List<UserAccount>> GetUsersAsync()
    {
        var result = new List<UserAccount>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT Id, Username, PasswordHash, DisplayName, Role, IsLocked, RegistrationApproved FROM UserAccount ORDER BY Id";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new UserAccount(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5) != 0,
                reader.GetInt32(6) != 0
            ));
        }

        return result;
    }

    /// <summary>Tra user theo username (đăng nhập). Không phân biệt hoa/thường, bỏ khoảng trắng đầu/cuối.</summary>
    public async Task<UserAccount?> GetUserByUsernameAsync(string username)
    {
        var key = NormalizeLoginUsername(username);
        if (key.Length == 0) return null;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT Id, Username, PasswordHash, DisplayName, Role, IsLocked, RegistrationApproved FROM UserAccount WHERE LOWER(TRIM(Username)) = LOWER($username)";
        cmd.Parameters.AddWithValue("$username", key);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new UserAccount(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5) != 0,
            reader.GetInt32(6) != 0
        );
    }

    /// <summary>
    /// Hai tài khoản demo role <c>owner</c> (chủ quán) nếu chưa tồn tại — mật khẩu chỉ dùng môi trường demo, đổi sau khi triển khai thật.
    /// </summary>
    private async Task EnsureDemoOwnerAccountsAsync()
    {
        const string demoPassword = "chuquan123";
        if (await GetUserByUsernameAsync("chuquan1") is null)
            await CreateUserAsync("chuquan1", demoPassword, "Chủ quán — Khu A", "owner");
        if (await GetUserByUsernameAsync("chuquan2") is null)
            await CreateUserAsync("chuquan2", demoPassword, "Chủ quán — Khu B", "owner");
    }

    /// <summary>
    /// Đồ án: gán cùng mật khẩu demo cho các username chủ quán phố (không lọc theo Role — tránh lệch chữ hoa/thường trong DB).
    /// Gọi khi khởi động và trước khi đăng nhập để hash luôn khớp <c>VkQuan@123</c>.
    /// </summary>
    public async Task EnsurePhoOwnerDemoPasswordsAsync()
    {
        const string password = "VkQuan@123";
        var hash = PasswordTools.Hash(password);
        (string User, string Display)[] pairs =
        [
            ("owner_oc_oanh", "Chủ quán - Ốc Oanh"),
            ("owner_oc_dao_2", "Chủ quán - Ốc Đào 2"),
            ("owner_sui_cao_tan_tong_loi", "Chủ quán - Sủi cảo Tân Tòng Lợi"),
            ("owner_lau_bo_khu_nha_chay", "Chủ quán - Lẩu bò Khu Nhà Cháy"),
            ("owner_oc_vu", "Chủ quán - Ốc Vũ"),
            ("owner_oc_linh", "Chủ quán - Ốc Linh")
        ];
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        foreach (var (user, _) in pairs)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "UPDATE UserAccount SET PasswordHash = $hash WHERE LOWER(TRIM(Username)) = LOWER(TRIM($u))";
            cmd.Parameters.AddWithValue("$hash", hash);
            cmd.Parameters.AddWithValue("$u", user);
            await cmd.ExecuteNonQueryAsync();
        }

        foreach (var (user, display) in pairs)
        {
            if (await GetUserByUsernameAsync(user) is null)
                await CreateUserAsync(user, password, display, "owner");
        }
    }

    /// <summary>Tạo user mới; false nếu username trùng. <paramref name="registrationApproved"/>: false = chủ quán tự đăng ký, chờ admin duyệt.</summary>
    public async Task<bool> CreateUserAsync(string username, string password, string displayName, string role, bool registrationApproved = true)
    {
        var existing = await GetUserByUsernameAsync(username);
        if (existing is not null) return false;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO UserAccount(Username, PasswordHash, DisplayName, Role, IsLocked, RegistrationApproved) VALUES ($username, $hash, $displayName, $role, 0, $regOk)";
        cmd.Parameters.AddWithValue("$username", username);
        cmd.Parameters.AddWithValue("$hash", PasswordTools.Hash(password));
        cmd.Parameters.AddWithValue("$displayName", displayName);
        cmd.Parameters.AddWithValue("$role", role is "admin" or "owner" ? role : "owner");
        cmd.Parameters.AddWithValue("$regOk", registrationApproved ? 1 : 0);
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    /// <summary>Admin duyệt đăng ký chủ quán (cho phép đăng nhập).</summary>
    public async Task<bool> ApproveOwnerRegistrationAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "UPDATE UserAccount SET RegistrationApproved = 1 WHERE Id = $id AND Role = 'owner' AND LOWER(TRIM(Username)) <> 'admin'";
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Admin từ chối đăng ký: xóa tài khoản chủ quán đang chờ duyệt.</summary>
    public async Task<bool> RejectPendingOwnerRegistrationAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "DELETE FROM UserAccount WHERE Id = $id AND Role = 'owner' AND RegistrationApproved = 0 AND LOWER(TRIM(Username)) <> 'admin'";
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Cập nhật profile/vai trò; tùy chọn đổi mật khẩu (hash mới).</summary>
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

    /// <summary>Khóa/mở khóa đăng nhập; không áp dụng cho user <c>admin</c>.</summary>
    public async Task<bool> SetUserLockedAsync(int id, bool locked)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE UserAccount SET IsLocked = $locked WHERE Id = $id AND Username <> 'admin'";
        cmd.Parameters.AddWithValue("$locked", locked ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>Xóa user; không cho xóa tài khoản <c>admin</c>.</summary>
    public async Task<bool> DeleteUserAsync(int id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM UserAccount WHERE Id = $id AND Username <> 'admin'";
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    /// Với mỗi POI, nếu thiếu tên/mô tả theo ngôn ngữ thì gọi MyMemory và ghi DB.
    /// Có thể giới hạn một POI hoặc một <paramref name="targetLang"/>.
    /// </summary>
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

    /// <summary>Dịch tên/mô tả sang <paramref name="lang"/> nếu field đang trống.</summary>
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

    /// <summary>Đọc tên theo mã ngôn ngữ từ <see cref="PoiDto"/>.</summary>
    private static string GetNameByLang(PoiDto poi, string lang) => lang switch
    {
        "en" => poi.NameEn,
        "ja" => poi.NameJa,
        "ko" => poi.NameKo,
        "zh" => poi.NameZh,
        _ => ""
    };

    /// <summary>Đọc mô tả theo mã ngôn ngữ.</summary>
    private static string GetDescByLang(PoiDto poi, string lang) => lang switch
    {
        "en" => poi.DescEn,
        "ja" => poi.DescJa,
        "ko" => poi.DescKo,
        "zh" => poi.DescZh,
        _ => ""
    };

    /// <summary>Gán tên đã dịch (record <c>with</c>).</summary>
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

    /// <summary>Gán mô tả đã dịch (record <c>with</c>).</summary>
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

    /// <summary>Gán tham số SQL cho INSERT/UPDATE POI.</summary>
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
        cmd.Parameters.AddWithValue("$priority", poi.Priority);
        cmd.Parameters.AddWithValue("$mapLink", poi.MapLink ?? string.Empty);
    }

    /// <summary>Chèn vài POI mẫu khi database chưa có dữ liệu.</summary>
    private async Task SeedPoisAsync()
    {
        var seed = new[]
        {
            new PoiDto(0, "Cổng chào Phố Ẩm thực Vĩnh Khánh", "", "", "", "", "Điểm chào đầu tuyến phố ẩm thực Vĩnh Khánh.", "", "", "", "", 10.7595, 106.7012, 80, "gatevinhkhanh.jpg", "", "published", "", 0, 10, ""),
            new PoiDto(0, "Ốc Oanh", "", "", "", "", "Quán ốc nổi tiếng với món càng ghẹ rang muối.", "", "", "", "", 10.7588, 106.7018, 50, "ocoanh.jpg", "", "published", "", 0, 5, ""),
            new PoiDto(0, "Cafe Era", "", "", "", "", "Không gian cà phê thư giãn giữa tuyến phố.", "", "", "", "", 10.7585, 106.7025, 45, "cafeera.jpg", "", "published", "", 0, 5, "")
        };

        foreach (var poi in seed)
        {
            await CreatePoiAsync(poi, new AuthPrincipal(0, "seed", "admin"));
        }
    }

    /// <summary>Migration nhẹ: thêm cột Status/RejectReason/OwnerUserId nếu DB cũ thiếu.</summary>
    private static async Task EnsurePoiColumnsAsync(SqliteConnection connection)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(Poi);";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                existing.Add(reader.GetString(1));
            }
        }

        async Task AddColumnIfMissing(string name, string sqlTypeAndDefault)
        {
            if (existing.Contains(name)) return;
            await using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE Poi ADD COLUMN {name} {sqlTypeAndDefault};";
            await alter.ExecuteNonQueryAsync();
        }

        await AddColumnIfMissing("Status", "TEXT NOT NULL DEFAULT 'published'");
        await AddColumnIfMissing("RejectReason", "TEXT NOT NULL DEFAULT ''");
        await AddColumnIfMissing("OwnerUserId", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfMissing("Priority", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfMissing("MapLink", "TEXT NOT NULL DEFAULT ''");
    }

    private static async Task EnsureUserAccountColumnsAsync(SqliteConnection connection)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(UserAccount);";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                existing.Add(reader.GetString(1));
            }
        }

        async Task AddUserColumnIfMissing(string name, string sqlTypeAndDefault)
        {
            if (existing.Contains(name)) return;
            await using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE UserAccount ADD COLUMN {name} {sqlTypeAndDefault};";
            await alter.ExecuteNonQueryAsync();
            existing.Add(name);
        }

        await AddUserColumnIfMissing("IsLocked", "INTEGER NOT NULL DEFAULT 0");
        await AddUserColumnIfMissing("RegistrationApproved", "INTEGER NOT NULL DEFAULT 1");
    }

    /// <summary>Đọc Id/Status/OwnerUserId để kiểm tra quyền sửa POI.</summary>
    private static async Task<PoiRow?> GetPoiByIdInternalAsync(SqliteConnection connection, int id)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Status, OwnerUserId FROM Poi WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new PoiRow(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2));
    }

    /// <summary>Hàng tối thiểu phục vụ kiểm tra quyền cập nhật.</summary>
    private sealed record PoiRow(int Id, string Status, int OwnerUserId);
}
