using SQLite;

namespace TravelGuide.Models;

/// <summary>Đánh giá cục bộ theo tài khoản du khách cho từng POI.</summary>
public class TouristPlaceReview
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int PoiId { get; set; }

    [Indexed]
    public string Username { get; set; } = string.Empty;

    public int Rating { get; set; } = 5;

    public string Content { get; set; } = string.Empty;

    [Indexed]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
