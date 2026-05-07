using Microsoft.Data.SqlClient;
using TravelGuide.API.Models;

namespace TravelGuide.API.Data;

/// <summary>Đọc POI đã <c>published</c> từ cùng CSDL với Admin Web (<c>dbo.Poi</c>).</summary>
public sealed class PoiPublicReader
{
    private readonly string _connectionString;

    public PoiPublicReader()
    {
        _connectionString =
            Environment.GetEnvironmentVariable("TRAVELGUIDE_SQLSERVER")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=TravelGuideDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
    }

    public async Task<List<PublicPoiDto>> GetPublishedAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<PublicPoiDto>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                            SELECT Id, NameVi, NameEn, NameJa, NameKo, NameZh,
                                   DescVi, DescEn, DescJa, DescKo, DescZh,
                                   Latitude, Longitude, Radius, ImagePath, AudioUrl,
                                   Priority, MapLink, Price, Tag, QrImagePath
                            FROM dbo.Poi
                            WHERE Status = N'published'
                            ORDER BY Id;
                            """;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new PublicPoiDto(
                reader.GetInt32(0),
                S(reader, 1),
                S(reader, 2),
                S(reader, 3),
                S(reader, 4),
                S(reader, 5),
                S(reader, 6),
                S(reader, 7),
                S(reader, 8),
                S(reader, 9),
                S(reader, 10),
                reader.GetDouble(11),
                reader.GetDouble(12),
                reader.GetDouble(13),
                S(reader, 14),
                S(reader, 15),
                reader.GetInt32(16),
                S(reader, 17),
                reader.IsDBNull(18) ? 0 : Convert.ToDecimal(reader.GetValue(18)),
                S(reader, 19),
                S(reader, 20)
            ));
        }

        return list;
    }

    /// <summary>Một POI đã publish theo Id (null nếu không tồn tại / không publish).</summary>
    public async Task<PublicPoiDto?> GetPublishedByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
                            SELECT Id, NameVi, NameEn, NameJa, NameKo, NameZh,
                                   DescVi, DescEn, DescJa, DescKo, DescZh,
                                   Latitude, Longitude, Radius, ImagePath, AudioUrl,
                                   Priority, MapLink, Price, Tag, QrImagePath
                            FROM dbo.Poi
                            WHERE Id = @id AND Status = N'published';
                            """;
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new PublicPoiDto(
            reader.GetInt32(0),
            S(reader, 1),
            S(reader, 2),
            S(reader, 3),
            S(reader, 4),
            S(reader, 5),
            S(reader, 6),
            S(reader, 7),
            S(reader, 8),
            S(reader, 9),
            S(reader, 10),
            reader.GetDouble(11),
            reader.GetDouble(12),
            reader.GetDouble(13),
            S(reader, 14),
            S(reader, 15),
            reader.GetInt32(16),
            S(reader, 17),
            reader.IsDBNull(18) ? 0 : Convert.ToDecimal(reader.GetValue(18)),
            S(reader, 19),
            S(reader, 20)
        );
    }

    private static string S(SqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);
}
