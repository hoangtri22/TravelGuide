using Microsoft.Data.SqlClient;
using TravelGuide.API.Models;
using TravelGuide.API.Services;

namespace TravelGuide.API.Data;

public sealed class TouristDb
{
    private readonly string _connectionString;

    public TouristDb()
    {
        _connectionString =
            Environment.GetEnvironmentVariable("TRAVELGUIDE_SQLSERVER")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=TravelGuideDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
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
                          """;
        await cmd.ExecuteNonQueryAsync();
        await EnsureTouristUserColumnsAsync(connection);
        await EnsurePremiumClaimTableAsync(connection);
        await EnsureTouristPoiUnlockTableAsync(connection);
        await EnsureTouristPoiQrScanLogTableAsync(connection);
        await SeedDefaultPremiumClaimAsync(connection);
    }

    public async Task<bool> CreateUserAsync(string username, string password, string displayName, string? accountTier = null)
    {
        var existing = await GetUserByUsernameAsync(username);
        if (existing is not null) return false;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO TouristUser(Username, PasswordHash, DisplayName, AccountTier)
                          VALUES(@username, @passwordHash, @displayName, @accountTier);
                          """;
        cmd.Parameters.AddWithValue("@username", NormalizeUsername(username));
        cmd.Parameters.AddWithValue("@passwordHash", PasswordTools.Hash(password.Trim()));
        cmd.Parameters.AddWithValue("@displayName", displayName.Trim());
        cmd.Parameters.AddWithValue("@accountTier", NormalizeAccountTier(accountTier));
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<TouristUser?> GetUserByUsernameAsync(string? username)
    {
        var normalized = NormalizeUsername(username);
        if (normalized.Length == 0) return null;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          SELECT Id, Username, PasswordHash, DisplayName, AccountTier
                          FROM TouristUser
                          WHERE LOWER(LTRIM(RTRIM(Username))) = LOWER(@username);
                          """;
        cmd.Parameters.AddWithValue("@username", normalized);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new TouristUser(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            NormalizeAccountTier(reader.GetString(4)));
    }

    public async Task<TouristUser?> GetUserByIdAsync(int userId)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          SELECT Id, Username, PasswordHash, DisplayName, AccountTier
                          FROM TouristUser
                          WHERE Id = @id;
                          """;
        cmd.Parameters.AddWithValue("@id", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new TouristUser(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            NormalizeAccountTier(reader.GetString(4)));
    }

    public static string NormalizeUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return string.Empty;
        return username.Trim();
    }

    public static string NormalizeAccountTier(string? tier)
    {
        var t = (tier ?? "free").Trim().ToLowerInvariant();
        return t == "premium" ? "premium" : "free";
    }

    private static async Task EnsureTouristUserColumnsAsync(SqlConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COL_LENGTH('dbo.TouristUser','AccountTier')";
        var colLen = await cmd.ExecuteScalarAsync();
        if (colLen is null || colLen == DBNull.Value)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE dbo.TouristUser ADD AccountTier NVARCHAR(20) NOT NULL DEFAULT N'free';";
            await alter.ExecuteNonQueryAsync();
        }
    }

    private static async Task EnsurePremiumClaimTableAsync(SqlConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          IF OBJECT_ID(N'dbo.PremiumClaimCode', N'U') IS NULL
                          BEGIN
                            CREATE TABLE dbo.PremiumClaimCode(
                              Code NVARCHAR(40) NOT NULL PRIMARY KEY,
                              IsUsed BIT NOT NULL DEFAULT 0,
                              UsedByUserId INT NULL,
                              UsedAtUtc DATETIME2(0) NULL
                            );
                          END;
                          """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task EnsureTouristPoiUnlockTableAsync(SqlConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          IF OBJECT_ID(N'dbo.TouristPoiUnlock', N'U') IS NULL
                          BEGIN
                            CREATE TABLE dbo.TouristPoiUnlock(
                              TouristUserId INT NOT NULL,
                              PoiId INT NOT NULL,
                              UnlockedAtUtc DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
                              AmountVnd DECIMAL(18,2) NOT NULL DEFAULT 0,
                              CONSTRAINT PK_TouristPoiUnlock PRIMARY KEY (TouristUserId, PoiId)
                            );
                          END;
                          """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task EnsureTouristPoiQrScanLogTableAsync(SqlConnection connection)
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

    /// <summary>Ghi nhật kỳ quét QR mở POI (admin xem lịch sử + doanh thu).</summary>
    public async Task InsertPoiQrScanLogAsync(
        int touristUserId,
        string username,
        int poiId,
        string? poiNameVi,
        string eventType,
        decimal amountVnd,
        string? deviceId,
        string? deviceModel,
        string? appPlatform)
    {
        var et = (eventType ?? "poi_qr_access").Trim();
        if (et.Length > 40) et = et[..40];
        var name = (poiNameVi ?? "").Trim();
        if (name.Length > 300) name = name[..300];

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO dbo.TouristPoiQrScanLog(
                            TouristUserId, Username, PoiId, PoiNameVi, EventType, AmountVnd, DeviceId, DeviceModel, AppPlatform)
                          VALUES(@uid, @user, @poiId, @poiName, @evt, @amt, @devId, @devModel, @plat);
                          """;
        cmd.Parameters.AddWithValue("@uid", touristUserId);
        cmd.Parameters.AddWithValue("@user", (username ?? "").Trim());
        cmd.Parameters.AddWithValue("@poiId", poiId);
        cmd.Parameters.AddWithValue("@poiName", string.IsNullOrEmpty(name) ? "" : name);
        cmd.Parameters.AddWithValue("@evt", et);
        cmd.Parameters.AddWithValue("@amt", amountVnd < 0 ? 0 : amountVnd);
        cmd.Parameters.AddWithValue("@devId", string.IsNullOrWhiteSpace(deviceId) ? (object)DBNull.Value : deviceId.Trim());
        cmd.Parameters.AddWithValue("@devModel", string.IsNullOrWhiteSpace(deviceModel) ? (object)DBNull.Value : deviceModel.Trim());
        cmd.Parameters.AddWithValue("@plat", string.IsNullOrWhiteSpace(appPlatform) ? (object)DBNull.Value : appPlatform.Trim());
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Danh sách POI đã quét (mỗi POI một dòng — lần quét gần nhất).</summary>
    public async Task<List<MyPoiScanHistoryItemDto>> GetMyPoiScanHistoryAsync(int touristUserId, int take = 200)
    {
        var list = new List<MyPoiScanHistoryItemDto>();
        var top = take < 1 ? 200 : (take > 500 ? 500 : take);
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                            WITH ranked AS (
                                SELECT PoiId, PoiNameVi, EventType, AmountVnd, CreatedAtUtc,
                                       ROW_NUMBER() OVER (PARTITION BY PoiId ORDER BY CreatedAtUtc DESC) AS rn
                                FROM dbo.TouristPoiQrScanLog
                                WHERE TouristUserId = @uid
                            )
                            SELECT TOP (@take) PoiId, PoiNameVi, EventType, AmountVnd, CreatedAtUtc AS LastScannedAtUtc
                            FROM ranked
                            WHERE rn = 1
                            ORDER BY CreatedAtUtc DESC;
                            """;
        cmd.Parameters.AddWithValue("@uid", touristUserId);
        cmd.Parameters.AddWithValue("@take", top);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var poiId = reader.GetInt32(0);
            var nameVi = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var evt = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var amt = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3);
            var at = reader.GetDateTime(4);
            list.Add(new MyPoiScanHistoryItemDto(poiId, nameVi, evt, amt, at));
        }

        return list;
    }

    private static async Task SeedDefaultPremiumClaimAsync(SqlConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          IF NOT EXISTS (SELECT 1 FROM dbo.PremiumClaimCode WHERE Code = N'DEMO2024')
                            INSERT INTO dbo.PremiumClaimCode(Code) VALUES (N'DEMO2024');
                          """;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Kích hoạt Premium bằng mã một lần (QR). Trả về message lỗi nếu thất bại.</summary>
    public async Task<(bool Ok, string Message)> RedeemPremiumClaimAsync(int userId, string claimCode)
    {
        var code = (claimCode ?? string.Empty).Trim();
        if (code.Length < 4 || code.Length > 40)
            return (false, "Mã không hợp lệ.");

        var user = await GetUserByIdAsync(userId);
        if (user is null) return (false, "Không tìm thấy tài khoản.");

        if (string.Equals(user.AccountTier, "premium", StringComparison.OrdinalIgnoreCase))
            return (true, "Tài khoản đã là Premium.");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        using var tx = connection.BeginTransaction();

        try
        {
            await using (var sel = connection.CreateCommand())
            {
                sel.Transaction = tx;
                sel.CommandText = """
                                    SELECT IsUsed FROM dbo.PremiumClaimCode WITH (UPDLOCK, ROWLOCK)
                                    WHERE Code = @code;
                                    """;
                sel.Parameters.AddWithValue("@code", code);
                var scalar = await sel.ExecuteScalarAsync();
                if (scalar is null || scalar == DBNull.Value)
                {
                    tx.Rollback();
                    return (false, "Mã không tồn tại.");
                }

                var isUsed = scalar is bool usedBit
                    ? usedBit
                    : Convert.ToInt32(scalar) != 0;
                if (isUsed)
                {
                    tx.Rollback();
                    return (false, "Mã đã được sử dụng.");
                }
            }

            await using (var upUser = connection.CreateCommand())
            {
                upUser.Transaction = tx;
                upUser.CommandText = """
                                     UPDATE dbo.TouristUser
                                     SET AccountTier = N'premium'
                                     WHERE Id = @id;
                                     """;
                upUser.Parameters.AddWithValue("@id", userId);
                if (await upUser.ExecuteNonQueryAsync() == 0)
                {
                    tx.Rollback();
                    return (false, "Không cập nhật được tài khoản.");
                }
            }

            await using (var mark = connection.CreateCommand())
            {
                mark.Transaction = tx;
                mark.CommandText = """
                                     UPDATE dbo.PremiumClaimCode
                                     SET IsUsed = 1, UsedByUserId = @uid, UsedAtUtc = SYSUTCDATETIME()
                                     WHERE Code = @code AND IsUsed = 0;
                                     """;
                mark.Parameters.AddWithValue("@uid", userId);
                mark.Parameters.AddWithValue("@code", code);
                if (await mark.ExecuteNonQueryAsync() == 0)
                {
                    tx.Rollback();
                    return (false, "Mã không còn hiệu lực.");
                }
            }

            tx.Commit();
            return (true, "Đã kích hoạt Premium.");
        }
        catch
        {
            tx.Rollback();
            return (false, "Lỗi hệ thống khi kích hoạt.");
        }
    }

    public async Task<bool> IsPoiUnlockedAsync(int userId, int poiId)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          SELECT 1 FROM dbo.TouristPoiUnlock
                          WHERE TouristUserId = @uid AND PoiId = @poi;
                          """;
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@poi", poiId);
        var o = await cmd.ExecuteScalarAsync();
        return o is not null && o != DBNull.Value;
    }

    /// <summary>Ghi nhận thanh toán mô phỏng (nút xác nhận) để mở nội dung POI.</summary>
    public async Task<(bool Ok, string Message)> ConfirmPoiUnlockAsync(int userId, int poiId, decimal amountVnd)
    {
        if (poiId <= 0) return (false, "POI không hợp lệ.");

        var user = await GetUserByIdAsync(userId);
        if (user is null) return (false, "Không tìm thấy tài khoản.");

        if (string.Equals(user.AccountTier, "premium", StringComparison.OrdinalIgnoreCase))
            return (true, "Premium — không cần thanh toán.");

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                          IF NOT EXISTS (SELECT 1 FROM dbo.TouristPoiUnlock WHERE TouristUserId = @uid AND PoiId = @poi)
                          BEGIN
                            INSERT INTO dbo.TouristPoiUnlock(TouristUserId, PoiId, AmountVnd)
                            VALUES (@uid, @poi, @amt);
                          END
                          """;
        cmd.Parameters.AddWithValue("@uid", userId);
        cmd.Parameters.AddWithValue("@poi", poiId);
        cmd.Parameters.AddWithValue("@amt", amountVnd < 0 ? 0 : amountVnd);
        try
        {
            await cmd.ExecuteNonQueryAsync();
            return (true, "Đã ghi nhận thanh toán.");
        }
        catch
        {
            return (false, "Không ghi nhận được thanh toán.");
        }
    }
}
