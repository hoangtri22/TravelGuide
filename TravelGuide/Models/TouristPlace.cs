using SQLite;

namespace TravelGuide.Models;

public class TouristPlace
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Radius { get; set; } = 200;
    public string? ImageUrl { get; set; }

    [Ignore] // Thuộc tính này không lưu vào DB
    public string Summary => Description?.Length > 100
        ? Description.Substring(0, 97) + "..."
        : Description ?? "";
}