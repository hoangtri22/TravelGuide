namespace TravelGuide.Models;

public class TouristPlace
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Radius { get; set; } = 200;
    public string? ImageUrl { get; set; }
    public string Summary => Description?.Length > 100
        ? Description.Substring(0, 97) + "..."
        : Description ?? "";
}