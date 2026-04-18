using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using TravelGuide.AdminWeb.Models;
using TravelGuide.AdminWeb.Services;

namespace TravelGuide.AdminWeb.Data;

/// <summary>
/// Truy cập SQL Server: bảng Poi, UserAccount; seed admin/POI; dịch tự động MyMemory.
/// </summary>
public sealed class TravelGuideDb
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _translationLock = new(1, 1);
    private static readonly JsonSerializerOptions CaseInsensitiveJson = new() { PropertyNameCaseInsensitive = true };

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

    /// <summary>Chuỗi kết nối SQL Server (localdb mặc định nếu không cấu hình biến môi trường).</summary>
    public TravelGuideDb()
    {
        _connectionString =
            Environment.GetEnvironmentVariable("TRAVELGUIDE_SQLSERVER")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=TravelGuideDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
    }

    /// <summary>Tạo bảng/cột nếu thiếu, seed admin + POI mẫu khi DB trống.</summary>
    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var sql = """
                  IF OBJECT_ID(N'dbo.Poi', N'U') IS NULL
                  BEGIN
                    CREATE TABLE dbo.Poi(
                      Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                      NameVi NVARCHAR(300) NOT NULL,
                      NameEn NVARCHAR(300) NOT NULL DEFAULT N'',
                      NameJa NVARCHAR(300) NOT NULL DEFAULT N'',
                      NameKo NVARCHAR(300) NOT NULL DEFAULT N'',
                      NameZh NVARCHAR(300) NOT NULL DEFAULT N'',
                      DescVi NVARCHAR(MAX) NOT NULL,
                      DescEn NVARCHAR(MAX) NOT NULL DEFAULT N'',
                      DescJa NVARCHAR(MAX) NOT NULL DEFAULT N'',
                      DescKo NVARCHAR(MAX) NOT NULL DEFAULT N'',
                      DescZh NVARCHAR(MAX) NOT NULL DEFAULT N'',
                      Latitude FLOAT NOT NULL,
                      Longitude FLOAT NOT NULL,
                      Radius FLOAT NOT NULL DEFAULT 50,
                      ImagePath NVARCHAR(500) NOT NULL DEFAULT N'',
                      AudioUrl NVARCHAR(1000) NOT NULL DEFAULT N'',
                      QrImagePath NVARCHAR(1000) NULL,
                      Status NVARCHAR(30) NOT NULL DEFAULT N'published',
                      RejectReason NVARCHAR(1000) NOT NULL DEFAULT N'',
                      OwnerUserId INT NOT NULL DEFAULT 0,
                      Priority INT NOT NULL DEFAULT 0,
                      MapLink NVARCHAR(1000) NOT NULL DEFAULT N'',
                      Price DECIMAL(18,2) NOT NULL DEFAULT 0,
                      Tag NVARCHAR(100) NOT NULL DEFAULT N'Địa Điểm Du Lịch'
                    );
                  END;

                  IF OBJECT_ID(N'dbo.UserAccount', N'U') IS NULL
                  BEGIN
                    CREATE TABLE dbo.UserAccount(
                      Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                      Username NVARCHAR(120) NOT NULL UNIQUE,
                      PasswordHash NVARCHAR(256) NOT NULL,
                      DisplayName NVARCHAR(200) NOT NULL,
                      Role NVARCHAR(30) NOT NULL,
                      IsLocked BIT NOT NULL DEFAULT 0,
                      RegistrationApproved BIT NOT NULL DEFAULT 1
                    );
                  END;

                  IF OBJECT_ID(N'dbo.TouristUser', N'U') IS NULL
                  BEGIN
                    CREATE TABLE dbo.TouristUser(
                      Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                      Username NVARCHAR(100) NOT NULL UNIQUE,
                      PasswordHash NVARCHAR(256) NOT NULL,
                      DisplayName NVARCHAR(200) NOT NULL,
                      AccountTier NVARCHAR(20) NOT NULL DEFAULT N'free',
                      CreatedAtUtc DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
                      UpdatedAtUtc DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
                    );
                  END;

                  IF OBJECT_ID(N'dbo.RefreshToken', N'U') IS NULL
                  BEGIN
                    CREATE TABLE dbo.RefreshToken(
                      Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                      TouristUserId INT NOT NULL,
                      TokenHash NVARCHAR(256) NOT NULL,
                      DeviceId NVARCHAR(120) NULL,
                      UserAgent NVARCHAR(500) NULL,
                      IpAddress NVARCHAR(64) NULL,
                      ExpiresAtUtc DATETIME2(0) NOT NULL,
                      RevokedAtUtc DATETIME2(0) NULL,
                      CreatedAtUtc DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
                      FOREIGN KEY (TouristUserId) REFERENCES dbo.TouristUser(Id)
                    );
                    CREATE UNIQUE INDEX UX_RefreshToken_TokenHash ON dbo.RefreshToken(TokenHash);
                    CREATE INDEX IX_RefreshToken_TouristUserId ON dbo.RefreshToken(TouristUserId, ExpiresAtUtc DESC);
                  END;

                  IF OBJECT_ID(N'dbo.TouristFavorite', N'U') IS NULL
                  BEGIN
                    CREATE TABLE dbo.TouristFavorite(
                      Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                      TouristUserId INT NOT NULL,
                      PoiId INT NOT NULL,
                      CreatedAtUtc DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
                      FOREIGN KEY (TouristUserId) REFERENCES dbo.TouristUser(Id),
                      FOREIGN KEY (PoiId) REFERENCES dbo.Poi(Id),
                      UNIQUE (TouristUserId, PoiId)
                    );
                  END;

                  IF OBJECT_ID(N'dbo.TouristVisitHistory', N'U') IS NULL
                  BEGIN
                    CREATE TABLE dbo.TouristVisitHistory(
                      Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                      TouristUserId INT NOT NULL,
                      PoiId INT NOT NULL,
                      EventType NVARCHAR(30) NOT NULL DEFAULT N'view',
                      PlaybackSeconds INT NOT NULL DEFAULT 0,
                      WatchedPercent DECIMAL(5,2) NOT NULL DEFAULT 0,
                      Source NVARCHAR(50) NULL,
                      OccurredAtUtc DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
                      FOREIGN KEY (TouristUserId) REFERENCES dbo.TouristUser(Id),
                      FOREIGN KEY (PoiId) REFERENCES dbo.Poi(Id)
                    );
                  END;

                  IF OBJECT_ID(N'dbo.PaymentTransaction', N'U') IS NULL
                  BEGIN
                    CREATE TABLE dbo.PaymentTransaction(
                      Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                      TouristUserId INT NOT NULL,
                      Provider NVARCHAR(50) NOT NULL,
                      ProviderRef NVARCHAR(150) NOT NULL,
                      PlanCode NVARCHAR(50) NOT NULL DEFAULT N'premium_monthly',
                      Currency NVARCHAR(10) NOT NULL DEFAULT N'VND',
                      Amount DECIMAL(18,2) NOT NULL,
                      Status NVARCHAR(30) NOT NULL,
                      PaidAtUtc DATETIME2(0) NULL,
                      ExpiresAtUtc DATETIME2(0) NULL,
                      RawPayloadJson NVARCHAR(MAX) NULL,
                      CreatedAtUtc DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
                      UpdatedAtUtc DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
                      FOREIGN KEY (TouristUserId) REFERENCES dbo.TouristUser(Id),
                      UNIQUE (Provider, ProviderRef)
                    );
                  END;
                  """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();

        await EnsurePoiColumnsAsync(connection);
        await EnsureQrImagePathNullableAsync(connection);
        await EnsureUserAccountColumnsAsync(connection);
        await EnsureTouristUserAccountTierColumnAsync(connection);
        await EnsureTouristPoiQrScanLogTableAsync(connection);
        await EnsureTouristCommentTableAsync(connection);

        var hasAdmin = await GetUserByUsernameAsync("admin");
        if (hasAdmin is null)
        {
            await CreateUserAsync("admin", "admin123", "Administrator", "admin");
        }

        await EnsureDemoOwnerAccountsAsync();
        await EnsurePhoOwnerDemoPasswordsAsync();
        await EnsureDemoCommentsAsync();

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
        string QrImagePath,
        int Priority,
        string MapLink,
        decimal Price,
        string Tag);

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
            rows = await JsonSerializer.DeserializeAsync<List<ExtraPlaceJson>>(stream, CaseInsensitiveJson)
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
                q.CommandText = "SELECT Id, OwnerUserId FROM Poi WHERE NameVi = $n";
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
                                    INSERT INTO Poi(NameVi, NameEn, NameJa, NameKo, NameZh, DescVi, DescEn, DescJa, DescKo, DescZh, Latitude, Longitude, Radius, ImagePath, AudioUrl, QrImagePath, Status, RejectReason, OwnerUserId, Priority, MapLink, Price, Tag)
                                    VALUES ($nameVi, '', '', '', '', $descVi, '', '', '', '', $lat, $lon, $radius, $imagePath, $audioUrl, $qrImagePath, 'published', '', $ownerUserId, $priority, $mapLink, $price, $tag);
                                    """;
                ins.Parameters.AddWithValue("$nameVi", row.NameVi);
                ins.Parameters.AddWithValue("$descVi", row.DescVi ?? "");
                ins.Parameters.AddWithValue("$lat", row.Latitude);
                ins.Parameters.AddWithValue("$lon", row.Longitude);
                ins.Parameters.AddWithValue("$radius", row.Radius);
                ins.Parameters.AddWithValue("$imagePath", row.ImagePath ?? "");
                ins.Parameters.AddWithValue("$audioUrl", row.AudioUrl ?? "");
                ins.Parameters.AddWithValue("$qrImagePath", string.IsNullOrWhiteSpace(row.QrImagePath) ? null : row.QrImagePath);
                ins.Parameters.AddWithValue("$priority", row.Priority);
                ins.Parameters.AddWithValue("$mapLink", row.MapLink ?? "");
                ins.Parameters.AddWithValue("$price", row.Price < 0 ? 0 : row.Price);
                ins.Parameters.AddWithValue("$tag", NormalizeTag(row.Tag));
                ins.Parameters.AddWithValue("$ownerUserId", mappedOwnerId);
                await ins.ExecuteNonQueryAsync();
            }
            else if (existingOwner == 0 && mappedOwnerId != 0)
            {
                await using var up = connection.CreateCommand();
                up.CommandText = """
                                 UPDATE Poi SET
                                   DescVi = $descVi,
                                   Latitude = $lat,
                                   Longitude = $lon,
                                   Radius = $radius,
                                   ImagePath = $imagePath,
                                   AudioUrl = $audioUrl,
                                   QrImagePath = $qrImagePath,
                                   Priority = $priority,
                                   MapLink = $mapLink,
                                   Price = $price,
                                   Tag = $tag,
                                   OwnerUserId = $oid
                                 WHERE Id = $id;
                                 """;
                up.Parameters.AddWithValue("$descVi", row.DescVi ?? "");
                up.Parameters.AddWithValue("$lat", row.Latitude);
                up.Parameters.AddWithValue("$lon", row.Longitude);
                up.Parameters.AddWithValue("$radius", row.Radius);
                up.Parameters.AddWithValue("$imagePath", row.ImagePath ?? "");
                up.Parameters.AddWithValue("$audioUrl", row.AudioUrl ?? "");
                up.Parameters.AddWithValue("$qrImagePath", string.IsNullOrWhiteSpace(row.QrImagePath) ? null : row.QrImagePath);
                up.Parameters.AddWithValue("$priority", row.Priority);
                up.Parameters.AddWithValue("$mapLink", row.MapLink ?? "");
                up.Parameters.AddWithValue("$price", row.Price < 0 ? 0 : row.Price);
                up.Parameters.AddWithValue("$tag", NormalizeTag(row.Tag));
                up.Parameters.AddWithValue("$oid", mappedOwnerId);
                up.Parameters.AddWithValue("$id", existingId.Value);
                await up.ExecuteNonQueryAsync();
            }
            else if (existingId is not null)
            {
                await using var up = connection.CreateCommand();
                up.CommandText = """
                                 UPDATE Poi SET
                                   DescVi = $descVi,
                                   Latitude = $lat,
                                   Longitude = $lon,
                                   Radius = $radius,
                                   ImagePath = $imagePath,
                                   AudioUrl = $audioUrl,
                                   QrImagePath = $qrImagePath,
                                   Priority = $priority,
                                   MapLink = $mapLink,
                                   Price = $price,
                                   Tag = $tag
                                 WHERE Id = $id;
                                 """;
                up.Parameters.AddWithValue("$descVi", row.DescVi ?? "");
                up.Parameters.AddWithValue("$lat", row.Latitude);
                up.Parameters.AddWithValue("$lon", row.Longitude);
                up.Parameters.AddWithValue("$radius", row.Radius);
                up.Parameters.AddWithValue("$imagePath", row.ImagePath ?? "");
                up.Parameters.AddWithValue("$audioUrl", row.AudioUrl ?? "");
                up.Parameters.AddWithValue("$qrImagePath", string.IsNullOrWhiteSpace(row.QrImagePath) ? null : row.QrImagePath);
                up.Parameters.AddWithValue("$priority", row.Priority);
                up.Parameters.AddWithValue("$mapLink", row.MapLink ?? "");
                up.Parameters.AddWithValue("$price", row.Price < 0 ? 0 : row.Price);
                up.Parameters.AddWithValue("$tag", NormalizeTag(row.Tag));
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
            "SELECT Id, NameVi, NameEn, NameJa, NameKo, NameZh, DescVi, DescEn, DescJa, DescKo, DescZh, Latitude, Longitude, Radius, ImagePath, AudioUrl, QrImagePath, Status, RejectReason, OwnerUserId, Priority, MapLink, Price, Tag FROM Poi"
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
            "SELECT Id, NameVi, NameEn, NameJa, NameKo, NameZh, DescVi, DescEn, DescJa, DescKo, DescZh, Latitude, Longitude, Radius, ImagePath, AudioUrl, QrImagePath, Status, RejectReason, OwnerUserId, Priority, MapLink, Price, Tag FROM Poi WHERE OwnerUserId = $oid ORDER BY Id";
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
            "SELECT Id, NameVi, NameEn, NameJa, NameKo, NameZh, DescVi, DescEn, DescJa, DescKo, DescZh, Latitude, Longitude, Radius, ImagePath, AudioUrl, QrImagePath, Status, RejectReason, OwnerUserId, Priority, MapLink, Price, Tag FROM Poi WHERE Id = $id";
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
            reader.IsDBNull(16) ? null : reader.GetString(16),
            reader.GetString(17),
            reader.GetString(18),
            reader.GetInt32(19),
            reader.GetInt32(20),
            reader.GetString(21),
            Convert.ToDecimal(reader.GetDouble(22)),
            reader.GetString(23)
        );

    /// <summary>Thêm POI; admin → published, owner → pending và gán OwnerUserId.</summary>
    public async Task<int> CreatePoiAsync(PoiDto poi, AuthPrincipal principal)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO Poi(NameVi, NameEn, NameJa, NameKo, NameZh, DescVi, DescEn, DescJa, DescKo, DescZh, Latitude, Longitude, Radius, ImagePath, AudioUrl, QrImagePath, Status, RejectReason, OwnerUserId, Priority, MapLink, Price, Tag)
                          VALUES ($nameVi, $nameEn, $nameJa, $nameKo, $nameZh, $descVi, $descEn, $descJa, $descKo, $descZh, $lat, $lon, $radius, $imagePath, $audioUrl, $qrImagePath, $status, $rejectReason, $ownerUserId, $priority, $mapLink, $price, $tag);
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

        // Khi nguồn tiếng Việt thay đổi, xóa bản dịch liên quan để luồng auto-translate dịch lại.
        var normalizedIncomingNameVi = (poi.NameVi ?? "").Trim();
        var normalizedExistingNameVi = (existing.NameVi ?? "").Trim();
        var normalizedIncomingDescVi = (poi.DescVi ?? "").Trim();
        var normalizedExistingDescVi = (existing.DescVi ?? "").Trim();

        var sourceNameChanged = !string.Equals(
            normalizedIncomingNameVi,
            normalizedExistingNameVi,
            StringComparison.Ordinal);
        var sourceDescChanged = !string.Equals(
            normalizedIncomingDescVi,
            normalizedExistingDescVi,
            StringComparison.Ordinal);

        if (sourceNameChanged || sourceDescChanged)
        {
            poi = poi with
            {
                NameEn = sourceNameChanged ? "" : poi.NameEn,
                NameJa = sourceNameChanged ? "" : poi.NameJa,
                NameKo = sourceNameChanged ? "" : poi.NameKo,
                NameZh = sourceNameChanged ? "" : poi.NameZh,
                DescEn = sourceDescChanged ? "" : poi.DescEn,
                DescJa = sourceDescChanged ? "" : poi.DescJa,
                DescKo = sourceDescChanged ? "" : poi.DescKo,
                DescZh = sourceDescChanged ? "" : poi.DescZh
            };
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          UPDATE Poi SET
                            NameVi = $nameVi, NameEn = $nameEn, NameJa = $nameJa, NameKo = $nameKo, NameZh = $nameZh,
                            DescVi = $descVi, DescEn = $descEn, DescJa = $descJa, DescKo = $descKo, DescZh = $descZh,
                            Latitude = $lat, Longitude = $lon, Radius = $radius, ImagePath = $imagePath, AudioUrl = $audioUrl, QrImagePath = $qrImagePath,
                            Priority = $priority, MapLink = $mapLink, Price = $price, Tag = $tag
                          WHERE Id = $id;
                          """;
        BindPoi(cmd, poi);
        cmd.Parameters.AddWithValue("$id", id);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    /// <summary>Cập nhật đường dẫn/URL ảnh QR (ví dụ sau khi tạo POI và gọi API sinh QR).</summary>
    public async Task<bool> UpdatePoiQrImagePathAsync(int id, string? qrImagePath)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Poi SET QrImagePath = $qr WHERE Id = $id";
        cmd.Parameters.AddWithValue("$qr", (object?)qrImagePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
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

    /// <summary>Xóa bản dịch hiện có để cho phép gọi auto-translate lại từ nguồn tiếng Việt.</summary>
    public async Task<bool> ClearTranslationsAsync(int id, string? targetLang = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();

        var lang = (targetLang ?? "").Trim().ToLowerInvariant();
        if (lang.Length == 0)
        {
            cmd.CommandText = """
                              UPDATE Poi SET
                                NameEn = N'', NameJa = N'', NameKo = N'', NameZh = N'',
                                DescEn = N'', DescJa = N'', DescKo = N'', DescZh = N''
                              WHERE Id = $id;
                              """;
        }
        else if (lang == "en")
        {
            cmd.CommandText = "UPDATE Poi SET NameEn = N'', DescEn = N'' WHERE Id = $id;";
        }
        else if (lang == "ja")
        {
            cmd.CommandText = "UPDATE Poi SET NameJa = N'', DescJa = N'' WHERE Id = $id;";
        }
        else if (lang == "ko")
        {
            cmd.CommandText = "UPDATE Poi SET NameKo = N'', DescKo = N'' WHERE Id = $id;";
        }
        else if (lang == "zh")
        {
            cmd.CommandText = "UPDATE Poi SET NameZh = N'', DescZh = N'' WHERE Id = $id;";
        }
        else
        {
            return false;
        }

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
        [.. pois.Select(x => (object)new { x.Id, x.NameVi, x.AudioUrl })];

    /// <summary>POI đã publish dạng rút gọn để export JSON.</summary>
    public async Task<List<ExportPoi>> GetExportPlacesAsync()
    {
        var pois = await GetPoisAsync(includeUnpublished: false);
        return [.. pois.Select(x => new ExportPoi(
            x.NameVi, x.DescVi, x.Latitude, x.Longitude, x.Radius, x.ImagePath ?? "",
            x.AudioUrl ?? "", x.QrImagePath ?? "", x.Priority, x.MapLink ?? "", x.Price, x.Tag
        ))];
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
                pois = [.. pois.Where(p => p.Id == onlyPoiId.Value)];

            foreach (var poi in pois)
            {
                if (string.IsNullOrWhiteSpace(poi.NameVi) || string.IsNullOrWhiteSpace(poi.DescVi))
                    continue;

                var next = poi with { };
                var changed = false;
                IReadOnlyList<string> langs = string.IsNullOrWhiteSpace(targetLang)
                    ? ["en", "ja", "ko", "zh"]
                    : [targetLang.ToLowerInvariant()];

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

    private static string NormalizeTag(string? rawTag)
    {
        var t = (rawTag ?? "").Trim();
        if (string.IsNullOrWhiteSpace(t)) return "Địa Điểm Du Lịch";
        var key = t.ToLowerInvariant();
        return key switch
        {
            "quan an" or "quán ăn" => "Quán Ăn",
            "quan nuoc" or "quán nước" => "Quán Nước",
            "di tich lich su" or "di tích lịch sử" => "Di Tích Lịch Sử",
            "dia diem du lich" or "địa điểm du lịch" => "Địa Điểm Du Lịch",
            _ => t
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
        cmd.Parameters.AddWithValue("$qrImagePath", string.IsNullOrWhiteSpace(poi.QrImagePath) ? null : poi.QrImagePath.Trim());
        cmd.Parameters.AddWithValue("$priority", poi.Priority);
        cmd.Parameters.AddWithValue("$mapLink", poi.MapLink ?? string.Empty);
        cmd.Parameters.AddWithValue("$price", poi.Price < 0 ? 0 : poi.Price);
        cmd.Parameters.AddWithValue("$tag", NormalizeTag(poi.Tag));
    }

    /// <summary>Chèn vài POI mẫu khi database chưa có dữ liệu.</summary>
    private async Task SeedPoisAsync()
    {
        var seed = new[]
        {
            new PoiDto(0, "Cổng chào Phố Ẩm thực Vĩnh Khánh", "", "", "", "", "Điểm chào đầu tuyến phố ẩm thực Vĩnh Khánh.", "", "", "", "", 10.7595, 106.7012, 80, "gatevinhkhanh.jpg", "", null, "published", "", 0, 10, "", 0, "Địa Điểm Du Lịch"),
            new PoiDto(0, "Ốc Oanh", "", "", "", "", "Quán ốc nổi tiếng với món càng ghẹ rang muối.", "", "", "", "", 10.7588, 106.7018, 50, "ocoanh.jpg", "", null, "published", "", 0, 5, "", 120000, "Quán Ăn"),
            new PoiDto(0, "Cafe Era", "", "", "", "", "Không gian cà phê thư giãn giữa tuyến phố.", "", "", "", "", 10.7585, 106.7025, 45, "cafeera.jpg", "", null, "published", "", 0, 5, "", 45000, "Quán Nước")
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
            cmd.CommandText = """
                              SELECT COLUMN_NAME
                              FROM INFORMATION_SCHEMA.COLUMNS
                              WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Poi';
                              """;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                existing.Add(reader.GetString(0));
            }
        }

        async Task AddColumnIfMissing(string name, string sqlTypeAndDefault)
        {
            if (existing.Contains(name)) return;
            await using var alter = connection.CreateCommand();
            // T-SQL: không dùng ADD COLUMN (cú pháp SQLite); SQL Server dùng ADD <tên cột> <kiểu>...
            alter.CommandText = $"ALTER TABLE dbo.Poi ADD [{name}] {sqlTypeAndDefault};";
            await alter.ExecuteNonQueryAsync();
        }

        await AddColumnIfMissing("Status", "NVARCHAR(30) NOT NULL DEFAULT 'published'");
        await AddColumnIfMissing("RejectReason", "NVARCHAR(1000) NOT NULL DEFAULT ''");
        await AddColumnIfMissing("OwnerUserId", "INT NOT NULL DEFAULT 0");
        await AddColumnIfMissing("Priority", "INT NOT NULL DEFAULT 0");
        await AddColumnIfMissing("MapLink", "NVARCHAR(1000) NOT NULL DEFAULT ''");
        await AddColumnIfMissing("QrImagePath", "NVARCHAR(1000) NULL");
        await AddColumnIfMissing("Price", "DECIMAL(18,2) NOT NULL DEFAULT 0");
        await AddColumnIfMissing("Tag", "NVARCHAR(100) NOT NULL DEFAULT N'Địa Điểm Du Lịch'");
    }

    /// <summary>Cho phép <c>QrImagePath</c> NULL trên DB đã tạo trước khi cột hỗ trợ null.</summary>
    private static async Task EnsureQrImagePathNullableAsync(SqliteConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                            IF EXISTS (
                              SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                              WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'Poi' AND COLUMN_NAME = N'QrImagePath' AND IS_NULLABLE = N'NO')
                            BEGIN
                              ALTER TABLE dbo.Poi ALTER COLUMN QrImagePath NVARCHAR(1000) NULL;
                            END
                            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task EnsureUserAccountColumnsAsync(SqliteConnection connection)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                              SELECT COLUMN_NAME
                              FROM INFORMATION_SCHEMA.COLUMNS
                              WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'UserAccount';
                              """;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                existing.Add(reader.GetString(0));
            }
        }

        async Task AddUserColumnIfMissing(string name, string sqlTypeAndDefault)
        {
            if (existing.Contains(name)) return;
            await using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE dbo.UserAccount ADD [{name}] {sqlTypeAndDefault};";
            await alter.ExecuteNonQueryAsync();
            existing.Add(name);
        }

        await AddUserColumnIfMissing("IsLocked", "BIT NOT NULL DEFAULT 0");
        await AddUserColumnIfMissing("RegistrationApproved", "BIT NOT NULL DEFAULT 1");
    }

    /// <summary>Bản TouristUser do API tạo trước đó có thể thiếu <c>AccountTier</c> — bổ sung để web đọc được.</summary>
    private static async Task EnsureTouristUserAccountTierColumnAsync(SqliteConnection connection)
    {
        await using var existsTable = connection.CreateCommand();
        existsTable.CommandText = """
                                  SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                                  WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'TouristUser';
                                  """;
        if (await existsTable.ExecuteScalarAsync() is null) return;

        await using var existsCol = connection.CreateCommand();
        existsCol.CommandText = """
                                  SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                                  WHERE TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'TouristUser' AND COLUMN_NAME = N'AccountTier';
                                  """;
        if (await existsCol.ExecuteScalarAsync() is not null) return;

        await using var alter = connection.CreateCommand();
        alter.CommandText = """
                            ALTER TABLE dbo.TouristUser
                            ADD AccountTier NVARCHAR(20) NOT NULL CONSTRAINT DF_TouristUser_AccountTier DEFAULT N'free';
                            """;
        await alter.ExecuteNonQueryAsync();
    }

    /// <summary>Đọc thông tin tối thiểu phục vụ kiểm tra quyền + phát hiện thay đổi nguồn VI.</summary>
    private static async Task<PoiRow?> GetPoiByIdInternalAsync(SqliteConnection connection, int id)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Status, OwnerUserId, NameVi, DescVi FROM Poi WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new PoiRow(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4));
    }

    /// <summary>Hàng tối thiểu phục vụ kiểm tra quyền cập nhật và so sánh NameVi/DescVi.</summary>
    private sealed record PoiRow(int Id, string Status, int OwnerUserId, string NameVi, string DescVi);

    public async Task<object> GetTouristOverviewAsync()
    {
        // Từng khối tách biệt: thiếu bảng phụ (RefreshToken, …) không làm hỏng danh sách TouristUser.
        var users = await SafeTouristQuery(GetTouristUsersAsync);
        var refreshTokens = await SafeTouristQuery(GetTouristRefreshTokensAsync);
        var favorites = await SafeTouristQuery(GetTouristFavoritesAsync);
        var visitHistory = await SafeTouristQuery(GetTouristVisitHistoryAsync);
        var payments = await SafeTouristQuery(GetPaymentTransactionsAsync);
        var dashboard = BuildTouristDashboard(users, refreshTokens, visitHistory);
        return new
        {
            users,
            refreshTokens,
            favorites,
            visitHistory,
            payments,
            dashboard
        };
    }

    /// <summary>Thống kê nhanh + dữ liệu LIVE/sparkline cho tab Dữ liệu du khách.</summary>
    private static object BuildTouristDashboard(
        List<TouristUserDto> users,
        List<TouristRefreshTokenDto> refreshTokens,
        List<TouristVisitHistoryDto> visitHistory)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var windowStart = now.AddHours(-24);
        // "Trực tuyến" = token còn hạn VÀ có tín hiệu gần đây (lịch sử xem / đăng nhập lại). Tránh hiển thị online sau khi đã tắt app
        // nhưng token vẫn còn hạn hàng giờ.
        var presenceCutoff = now.AddMinutes(-2);

        var lastVisitByUser = visitHistory
            .GroupBy(h => h.TouristUserId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.OccurredAtUtc));

        static DateTime PresenceSignal(
            int touristUserId,
            List<TouristRefreshTokenDto> tokens,
            IReadOnlyDictionary<int, DateTime> lastVisits,
            DateTime utcNow)
        {
            var lastVisit = lastVisits.TryGetValue(touristUserId, out var lv) ? lv : DateTime.MinValue;
            var newestValidTokenCreate = tokens
                .Where(t => t.TouristUserId == touristUserId && t.RevokedAtUtc is null && t.ExpiresAtUtc > utcNow)
                .Select(t => t.CreatedAtUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
            return lastVisit > newestValidTokenCreate ? lastVisit : newestValidTokenCreate;
        }

        var validTokenUserIds = refreshTokens
            .Where(t => t.RevokedAtUtc is null && t.ExpiresAtUtc > now)
            .Select(t => t.TouristUserId)
            .Distinct()
            .ToList();

        var onlineCount = validTokenUserIds.Count(uid =>
            PresenceSignal(uid, refreshTokens, lastVisitByUser, now) >= presenceCutoff);

        var nonRevokedUserIds = refreshTokens
            .Where(t => t.RevokedAtUtc is null)
            .Select(t => t.TouristUserId)
            .ToHashSet();

        var activatedCount = users.Count(u =>
            nonRevokedUserIds.Contains(u.Id)
            || string.Equals(u.AccountTier, "premium", StringComparison.OrdinalIgnoreCase));

        var totalAccounts = users.Count;

        var sessionsToday = visitHistory.Count(h => h.OccurredAtUtc >= today && h.OccurredAtUtc < today.AddDays(1));

        var hourly = new int[24];
        foreach (var h in visitHistory.Where(x => x.OccurredAtUtc >= windowStart))
        {
            var slot = (int)Math.Floor((h.OccurredAtUtc - windowStart).TotalHours);
            if (slot is >= 0 and < 24)
                hourly[slot]++;
        }

        var routes = new[] { "/home", "/destinations", "/bookings", "/explore", "/profile" };
        var liveSessions = new List<object>();
        var recentValid = refreshTokens
            .Where(t => t.RevokedAtUtc is null && t.ExpiresAtUtc > now)
            .GroupBy(t => t.TouristUserId)
            .Select(g => g.OrderByDescending(t => t.CreatedAtUtc).First())
            .Where(t => PresenceSignal(t.TouristUserId, refreshTokens, lastVisitByUser, now) >= presenceCutoff)
            .OrderByDescending(t => PresenceSignal(t.TouristUserId, refreshTokens, lastVisitByUser, now))
            .Take(6)
            .ToList();

        foreach (var t in recentValid)
        {
            var u = users.FirstOrDefault(x => x.Id == t.TouristUserId);
            var uname = u?.Username ?? t.Username;
            var disp = u?.DisplayName ?? uname;
            var tierLabel = string.Equals(u?.AccountTier, "premium", StringComparison.OrdinalIgnoreCase) ? "Premium" : "Free";
            var route = routes[Math.Abs(uname.GetHashCode()) % routes.Length];
            var signal = PresenceSignal(t.TouristUserId, refreshTokens, lastVisitByUser, now);
            var mins = Math.Max(1, (int)(now - signal).TotalMinutes);
            liveSessions.Add(new
            {
                username = uname,
                displayName = disp,
                tierLabel,
                route,
                minutesAgo = mins
            });
        }

        return new
        {
            onlineCount,
            totalAccounts,
            activatedCount,
            sessionsToday,
            hourlyActivity = hourly,
            liveSessions
        };
    }

    /// <summary>Lịch sử quét QR POI + thống kê doanh thu (AmountVnd) cho admin.</summary>
    public async Task<object> GetTouristPoiScanDashboardAsync()
    {
        var logs = await SafeTouristQuery(GetPoiQrScanLogsAsync);
        var revenueByPoi = await SafeTouristQuery(GetPoiQrScanRevenueByPoiAsync);
        var totalScans = await SafeCountQuery(GetPoiQrScanTotalCountAsync);
        decimal grandTotal = 0;
        foreach (var r in revenueByPoi)
            grandTotal += r.TotalVnd;
        return new
        {
            logs,
            revenueByPoi,
            grandTotalVnd = grandTotal,
            totalScans
        };
    }

    public async Task<CommentListResponseDto> GetCommentsAsync(string? status, string? search, int page = 1, int pageSize = 20)
    {
        var statusNorm = NormalizeCommentStatus(status, allowAll: true);
        var searchNorm = (search ?? "").Trim();
        page = page <= 0 ? 1 : page;
        pageSize = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        static string BuildWhere(bool filterStatus, bool filterSearch)
        {
            var parts = new List<string>();
            if (filterStatus) parts.Add("Status = $status");
            if (filterSearch)
                parts.Add("(Username LIKE $kw OR PoiNameVi LIKE $kw OR Content LIKE $kw OR AdminReply LIKE $kw)");
            return parts.Count == 0 ? "" : " WHERE " + string.Join(" AND ", parts);
        }

        var whereSql = BuildWhere(statusNorm != "all", !string.IsNullOrWhiteSpace(searchNorm));
        var rows = new List<TouristCommentDto>();
        var totalItems = 0;
        var stats = new CommentStatsDto(0, 0, 0, 0, 0);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        async Task<int> ReadCountAsync(string sql)
        {
            await using var c = connection.CreateCommand();
            c.CommandText = sql;
            var raw = await c.ExecuteScalarAsync();
            return raw is null || raw == DBNull.Value ? 0 : Convert.ToInt32(raw);
        }

        stats = new CommentStatsDto(
            await ReadCountAsync("SELECT COUNT(*) FROM TouristComment;"),
            await ReadCountAsync("SELECT COUNT(*) FROM TouristComment WHERE Status = 'pending';"),
            await ReadCountAsync("SELECT COUNT(*) FROM TouristComment WHERE Status = 'approved';"),
            await ReadCountAsync("SELECT COUNT(*) FROM TouristComment WHERE Status = 'rejected';"),
            await ReadCountAsync("SELECT COUNT(*) FROM TouristComment WHERE Status = 'hidden';"));

        await using (var countCmd = connection.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM TouristComment" + whereSql + ";";
            if (statusNorm != "all") countCmd.Parameters.AddWithValue("$status", statusNorm);
            if (!string.IsNullOrWhiteSpace(searchNorm)) countCmd.Parameters.AddWithValue("$kw", "%" + searchNorm + "%");
            var raw = await countCmd.ExecuteScalarAsync();
            totalItems = raw is null || raw == DBNull.Value ? 0 : Convert.ToInt32(raw);
        }

        await using (var listCmd = connection.CreateCommand())
        {
            listCmd.CommandText = $"""
                                   SELECT Id, TouristUserId, Username, PoiId, PoiNameVi, Rating, Content, Status, AdminReply, RejectReason, CreatedAtUtc, AdminReplyAtUtc, UpdatedAtUtc
                                   FROM TouristComment{whereSql}
                                   ORDER BY CreatedAtUtc DESC, Id DESC
                                   OFFSET {skip} ROWS FETCH NEXT {pageSize} ROWS ONLY;
                                   """;
            if (statusNorm != "all") listCmd.Parameters.AddWithValue("$status", statusNorm);
            if (!string.IsNullOrWhiteSpace(searchNorm)) listCmd.Parameters.AddWithValue("$kw", "%" + searchNorm + "%");

            await using var reader = await listCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(new TouristCommentDto(
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    reader.GetInt32(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetDateTime(10),
                    reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                    reader.GetDateTime(12)));
            }
        }

        return new CommentListResponseDto(rows, stats, page, pageSize, totalItems);
    }

    /// <summary>Bình luận hiển thị trên app (pending + approved), không cần đăng nhập — đồng bộ với <c>TravelGuide.API</c>.</summary>
    public async Task<List<TouristCommentDto>> GetPublicCommentsByPoiAsync(int poiId, int take = 100)
    {
        var rows = new List<TouristCommentDto>();
        if (poiId <= 0) return rows;
        var top = Math.Clamp(take, 1, 500);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var listCmd = connection.CreateCommand();
        listCmd.CommandText = $"""
                                 SELECT Id, TouristUserId, Username, PoiId, PoiNameVi, Rating, Content, Status, AdminReply, RejectReason, CreatedAtUtc, AdminReplyAtUtc, UpdatedAtUtc
                                 FROM TouristComment
                                 WHERE PoiId = $poiId AND LOWER(LTRIM(RTRIM(Status))) IN ('pending', 'approved')
                                 ORDER BY CreatedAtUtc DESC, Id DESC
                                 OFFSET 0 ROWS FETCH NEXT {top} ROWS ONLY;
                                 """;
        listCmd.Parameters.AddWithValue("$poiId", poiId);

        await using var reader = await listCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new TouristCommentDto(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetDateTime(10),
                reader.IsDBNull(11) ? null : reader.GetDateTime(11),
                reader.GetDateTime(12)));
        }

        return rows;
    }

    public async Task<bool> UpdateCommentStatusAsync(long id, string? status, string? reason)
    {
        if (id <= 0) return false;
        var statusNorm = NormalizeCommentStatus(status, allowAll: false);
        var reasonNorm = statusNorm == "rejected" ? (reason ?? "").Trim() : "";
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          UPDATE TouristComment
                          SET Status = $status,
                              RejectReason = $reason,
                              UpdatedAtUtc = SYSUTCDATETIME()
                          WHERE Id = $id;
                          """;
        cmd.Parameters.AddWithValue("$status", statusNorm);
        cmd.Parameters.AddWithValue("$reason", reasonNorm);
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> ReplyCommentAsync(long id, string? reply)
    {
        if (id <= 0) return false;
        var replyNorm = (reply ?? "").Trim();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          UPDATE TouristComment
                          SET AdminReply = $reply,
                              AdminReplyAtUtc = CASE WHEN LEN($reply) > 0 THEN SYSUTCDATETIME() ELSE NULL END,
                              UpdatedAtUtc = SYSUTCDATETIME()
                          WHERE Id = $id;
                          """;
        cmd.Parameters.AddWithValue("$reply", replyNorm);
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<long> CreateCommentAsync(CreateCommentRequest request)
    {
        var username = string.IsNullOrWhiteSpace(request.Username) ? "guest" : request.Username.Trim();
        var poiName = string.IsNullOrWhiteSpace(request.PoiNameVi) ? $"POI #{request.PoiId}" : request.PoiNameVi.Trim();
        var rating = Math.Clamp(request.Rating, 1, 5);
        var content = (request.Content ?? "").Trim();
        if (request.PoiId <= 0 || content.Length == 0) return 0;
        var status = NormalizeCommentStatus(request.Status, allowAll: false);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO TouristComment(TouristUserId, Username, PoiId, PoiNameVi, Rating, Content, Status, AdminReply, RejectReason, CreatedAtUtc, UpdatedAtUtc, AdminReplyAtUtc)
                          VALUES ($touristUserId, $username, $poiId, $poiNameVi, $rating, $content, $status, N'', N'', SYSUTCDATETIME(), SYSUTCDATETIME(), NULL);
                          SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
                          """;
        cmd.Parameters.AddWithValue("$touristUserId", request.TouristUserId.HasValue ? request.TouristUserId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$username", username);
        cmd.Parameters.AddWithValue("$poiId", request.PoiId);
        cmd.Parameters.AddWithValue("$poiNameVi", poiName);
        cmd.Parameters.AddWithValue("$rating", rating);
        cmd.Parameters.AddWithValue("$content", content);
        cmd.Parameters.AddWithValue("$status", status);
        var raw = await cmd.ExecuteScalarAsync();
        return raw is null || raw == DBNull.Value ? 0 : Convert.ToInt64(raw);
    }

    public async Task<bool> DeleteCommentAsync(long id)
    {
        if (id <= 0) return false;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM TouristComment WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static string NormalizeCommentStatus(string? status, bool allowAll)
    {
        var s = (status ?? "").Trim().ToLowerInvariant();
        return s switch
        {
            "pending" => "pending",
            "approved" => "approved",
            "rejected" => "rejected",
            "hidden" => "hidden",
            "all" when allowAll => "all",
            _ => allowAll ? "all" : "pending"
        };
    }

    private static async Task EnsureTouristPoiQrScanLogTableAsync(SqliteConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          IF OBJECT_ID(N'dbo.TouristPoiQrScanLog', N'U') IS NULL
                          BEGIN
                            CREATE TABLE dbo.TouristPoiQrScanLog(
                              Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                              TouristUserId INT NOT NULL,
                              Username NVARCHAR(100) NOT NULL,
                              PoiId INT NOT NULL,
                              PoiNameVi NVARCHAR(300) NOT NULL DEFAULT N'',
                              EventType NVARCHAR(40) NOT NULL DEFAULT N'poi_qr_access',
                              AmountVnd DECIMAL(18,2) NOT NULL DEFAULT 0,
                              DeviceId NVARCHAR(120) NULL,
                              DeviceModel NVARCHAR(200) NULL,
                              AppPlatform NVARCHAR(40) NULL,
                              CreatedAtUtc DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
                            );
                            CREATE INDEX IX_TouristPoiQrScanLog_Created ON dbo.TouristPoiQrScanLog(CreatedAtUtc DESC);
                            CREATE INDEX IX_TouristPoiQrScanLog_Poi ON dbo.TouristPoiQrScanLog(PoiId);
                          END;
                          """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task EnsureTouristCommentTableAsync(SqliteConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          IF OBJECT_ID(N'dbo.TouristComment', N'U') IS NULL
                          BEGIN
                            CREATE TABLE dbo.TouristComment(
                              Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                              TouristUserId INT NULL,
                              Username NVARCHAR(100) NOT NULL,
                              PoiId INT NOT NULL,
                              PoiNameVi NVARCHAR(300) NOT NULL DEFAULT N'',
                              Rating INT NOT NULL DEFAULT 5,
                              Content NVARCHAR(MAX) NOT NULL,
                              Status NVARCHAR(20) NOT NULL DEFAULT N'pending',
                              AdminReply NVARCHAR(1000) NOT NULL DEFAULT N'',
                              RejectReason NVARCHAR(1000) NOT NULL DEFAULT N'',
                              CreatedAtUtc DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
                              AdminReplyAtUtc DATETIME2(0) NULL,
                              UpdatedAtUtc DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
                            );
                            CREATE INDEX IX_TouristComment_Status ON dbo.TouristComment(Status, CreatedAtUtc DESC);
                            CREATE INDEX IX_TouristComment_Poi ON dbo.TouristComment(PoiId, CreatedAtUtc DESC);
                          END;
                          """;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task EnsureDemoCommentsAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using (var countCmd = connection.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM TouristComment;";
            var raw = await countCmd.ExecuteScalarAsync();
            var count = raw is null || raw == DBNull.Value ? 0 : Convert.ToInt32(raw);
            if (count > 0) return;
        }

        var demos = new (string User, int PoiId, string PoiName, int Rating, string Content, string Status, string Reply)[]
        {
            ("tranngoc", 1, "Nhà hàng Hải Sản Phú Quốc", 5, "Nhà hàng cực kỳ tươi ngon, hải sản được lấy trực tiếp từ biển mỗi sáng.", "pending", ""),
            ("minhlong", 2, "Cà phê Sân Thượng Đà Lạt", 4, "View đẹp, đồ uống ổn. Cuối tuần hơi đông nhưng đáng trải nghiệm.", "approved", "Cảm ơn bạn đã chia sẻ trải nghiệm."),
            ("hoanglinh", 3, "Khu du lịch Bà Nà Hills", 3, "Khá đông khách và xếp hàng lâu, giá vé hơi cao.", "pending", ""),
            ("vantung", 4, "Phố cổ Hội An", 5, "Hội An về đêm rất lung linh, không khí dễ chịu.", "approved", ""),
            ("phuchau", 5, "Khách sạn Mường Thanh Nha Trang", 1, "Nội dung bị ẩn do vi phạm chính sách bình luận.", "rejected", "")
        };

        foreach (var x in demos)
        {
            await using var ins = connection.CreateCommand();
            ins.CommandText = """
                              INSERT INTO TouristComment(TouristUserId, Username, PoiId, PoiNameVi, Rating, Content, Status, AdminReply, RejectReason, CreatedAtUtc, UpdatedAtUtc, AdminReplyAtUtc)
                              VALUES (NULL, $u, $poiId, $poiName, $rating, $content, $status, $reply, CASE WHEN $status = 'rejected' THEN N'Vi phạm quy tắc cộng đồng' ELSE N'' END, SYSUTCDATETIME(), SYSUTCDATETIME(), CASE WHEN LEN($reply) > 0 THEN SYSUTCDATETIME() ELSE NULL END);
                              """;
            ins.Parameters.AddWithValue("$u", x.User);
            ins.Parameters.AddWithValue("$poiId", x.PoiId);
            ins.Parameters.AddWithValue("$poiName", x.PoiName);
            ins.Parameters.AddWithValue("$rating", x.Rating);
            ins.Parameters.AddWithValue("$content", x.Content);
            ins.Parameters.AddWithValue("$status", x.Status);
            ins.Parameters.AddWithValue("$reply", x.Reply);
            await ins.ExecuteNonQueryAsync();
        }
    }

    private async Task<List<PoiQrScanLogDto>> GetPoiQrScanLogsAsync()
    {
        var result = new List<PoiQrScanLogDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          SELECT TOP 500 Id, TouristUserId, Username, PoiId, PoiNameVi, EventType, AmountVnd,
                                 DeviceId, DeviceModel, AppPlatform, CreatedAtUtc
                          FROM TouristPoiQrScanLog
                          ORDER BY Id DESC;
                          """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new PoiQrScanLogDto(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                Convert.ToDecimal(reader.GetDouble(6)),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetDateTime(10)));
        }

        return result;
    }

    private async Task<List<PoiQrScanRevenueDto>> GetPoiQrScanRevenueByPoiAsync()
    {
        var result = new List<PoiQrScanRevenueDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          SELECT PoiId,
                                 MAX(PoiNameVi) AS PoiNameVi,
                                 SUM(AmountVnd) AS TotalVnd,
                                 COUNT(*) AS ScanCount
                          FROM TouristPoiQrScanLog
                          GROUP BY PoiId
                          ORDER BY TotalVnd DESC;
                          """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new PoiQrScanRevenueDto(
                reader.GetInt32(0),
                reader.GetString(1),
                Convert.ToDecimal(reader.GetDouble(2)),
                reader.GetInt32(3)));
        }

        return result;
    }

    private async Task<int> GetPoiQrScanTotalCountAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM TouristPoiQrScanLog;";
        var raw = await cmd.ExecuteScalarAsync();
        if (raw is null || raw == DBNull.Value) return 0;
        return Convert.ToInt32(raw);
    }

    private static async Task<List<T>> SafeTouristQuery<T>(Func<Task<List<T>>> query)
    {
        try
        {
            return await query();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TravelGuideDb] GetTouristOverviewAsync: {ex.Message}");
            return [];
        }
    }

    private static async Task<int> SafeCountQuery(Func<Task<int>> query)
    {
        try
        {
            return await query();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TravelGuideDb] SafeCountQuery: {ex.Message}");
            return 0;
        }
    }

    private static bool LooksLikeSqlServerConnection(string cs)
    {
        if (string.IsNullOrWhiteSpace(cs)) return false;
        if (cs.Contains("localdb", StringComparison.OrdinalIgnoreCase)) return true;
        if (cs.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase)) return true;
        return cs.Contains("Server=", StringComparison.OrdinalIgnoreCase)
               && cs.Contains("Database=", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<List<TouristUserDto>> GetTouristUsersAsync()
    {
        if (LooksLikeSqlServerConnection(_connectionString))
            return await GetTouristUsersSqlServerAsync();

        var result = new List<TouristUserDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          SELECT u.Id, u.Username, u.DisplayName, u.AccountTier, u.CreatedAtUtc,
                                 IFNULL((SELECT COUNT(*) FROM TouristVisitHistory h WHERE h.TouristUserId = u.Id), 0) AS visitCount
                          FROM TouristUser u
                          ORDER BY u.Id DESC;
                          """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TouristUserDto(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDateTime(4),
                reader.GetInt32(5)));
        }
        return result;
    }

    private async Task<List<TouristUserDto>> GetTouristUsersSqlServerAsync()
    {
        var result = new List<TouristUserDto>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          SELECT TOP 500 Id, Username, DisplayName, AccountTier, CreatedAtUtc
                          FROM dbo.TouristUser
                          ORDER BY Id DESC;
                          """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TouristUserDto(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDateTime(4)));
        }
        return result;
    }

    /// <summary>Admin đổi tier cho tài khoản du khách (chỉ <c>free</c>/<c>premium</c>).</summary>
    public async Task<bool> UpdateTouristUserTierAsync(int touristUserId, string? accountTier)
    {
        if (touristUserId <= 0) return false;
        var tier = NormalizeTouristTier(accountTier);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          UPDATE TouristUser
                          SET AccountTier = $tier,
                              UpdatedAtUtc = SYSUTCDATETIME()
                          WHERE Id = $id;
                          """;
        cmd.Parameters.AddWithValue("$tier", tier);
        cmd.Parameters.AddWithValue("$id", touristUserId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static string NormalizeTouristTier(string? tier)
    {
        var normalized = (tier ?? "free").Trim().ToLowerInvariant();
        return normalized == "premium" ? "premium" : "free";
    }

    private async Task<List<TouristRefreshTokenDto>> GetTouristRefreshTokensAsync()
    {
        if (LooksLikeSqlServerConnection(_connectionString))
            return await GetTouristRefreshTokensSqlServerAsync();

        var result = new List<TouristRefreshTokenDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          SELECT r.Id, r.TouristUserId, u.Username, COALESCE(r.DeviceId, ''), r.ExpiresAtUtc, r.RevokedAtUtc, r.CreatedAtUtc
                          FROM RefreshToken r
                          INNER JOIN TouristUser u ON u.Id = r.TouristUserId
                          ORDER BY r.Id DESC
                          LIMIT 500;
                          """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TouristRefreshTokenDto(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                reader.GetDateTime(6)));
        }
        return result;
    }

    private async Task<List<TouristRefreshTokenDto>> GetTouristRefreshTokensSqlServerAsync()
    {
        var result = new List<TouristRefreshTokenDto>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          SELECT TOP 500 r.Id, r.TouristUserId, u.Username, ISNULL(r.DeviceId, N''), r.ExpiresAtUtc, r.RevokedAtUtc, r.CreatedAtUtc
                          FROM dbo.RefreshToken r
                          INNER JOIN dbo.TouristUser u ON u.Id = r.TouristUserId
                          ORDER BY r.Id DESC;
                          """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TouristRefreshTokenDto(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                reader.GetDateTime(6)));
        }
        return result;
    }

    private async Task<List<TouristFavoriteDto>> GetTouristFavoritesAsync()
    {
        var result = new List<TouristFavoriteDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          SELECT TOP 200 f.Id, f.TouristUserId, u.Username, f.PoiId, p.NameVi, f.CreatedAtUtc
                          FROM TouristFavorite f
                          INNER JOIN TouristUser u ON u.Id = f.TouristUserId
                          INNER JOIN Poi p ON p.Id = f.PoiId
                          ORDER BY f.Id DESC;
                          """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TouristFavoriteDto(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetDateTime(5)));
        }
        return result;
    }

    private async Task<List<TouristVisitHistoryDto>> GetTouristVisitHistoryAsync()
    {
        var result = new List<TouristVisitHistoryDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          SELECT TOP 300 h.Id, h.TouristUserId, u.Username, h.PoiId, p.NameVi, h.EventType, h.PlaybackSeconds, h.WatchedPercent, h.OccurredAtUtc
                          FROM TouristVisitHistory h
                          INNER JOIN TouristUser u ON u.Id = h.TouristUserId
                          INNER JOIN Poi p ON p.Id = h.PoiId
                          ORDER BY h.Id DESC;
                          """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new TouristVisitHistoryDto(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                Convert.ToDecimal(reader.GetDouble(7)),
                reader.GetDateTime(8)));
        }
        return result;
    }

    private async Task<List<PaymentTransactionDto>> GetPaymentTransactionsAsync()
    {
        var result = new List<PaymentTransactionDto>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          SELECT TOP 200 t.Id, t.TouristUserId, u.Username, t.Provider, t.ProviderRef, t.PlanCode, t.Currency, t.Amount, t.Status, t.CreatedAtUtc
                          FROM PaymentTransaction t
                          INNER JOIN TouristUser u ON u.Id = t.TouristUserId
                          ORDER BY t.Id DESC;
                          """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new PaymentTransactionDto(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                Convert.ToDecimal(reader.GetDouble(7)),
                reader.GetString(8),
                reader.GetDateTime(9)));
        }
        return result;
    }
}
