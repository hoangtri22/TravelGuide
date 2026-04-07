using SQLite;

namespace TravelGuide.Models;

public class TouristPlace
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // ===== MULTI LANGUAGE =====
    public string NameVi { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string DescVi { get; set; } = string.Empty;
    public string DescEn { get; set; } = string.Empty;

    public string? NameJa { get; set; }
    public string? NameKo { get; set; }
    public string? NameZh { get; set; }
    public string? DescJa { get; set; }
    public string? DescKo { get; set; }
    public string? DescZh { get; set; }

    // ===== LOCATION =====
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Radius { get; set; } = 50;

    // ===== UI =====
    public string? ImagePath { get; set; }

    [Ignore]
    public string ImageSource => string.IsNullOrEmpty(ImagePath)
        ? "placeholder.png"
        : ImagePath;

    [Ignore]
    public string Name => GetByLanguage(NameVi, NameEn, NameJa, NameKo, NameZh);

    [Ignore]
    public string Description => GetByLanguage(DescVi, DescEn, DescJa, DescKo, DescZh);

    [Ignore]
    public string Summary =>
        !string.IsNullOrEmpty(Description) && Description.Length > 100
            ? Description.Substring(0, 97) + "..."
            : Description ?? "";

    private static string GetByLanguage(
        string vi,
        string? en,
        string? ja,
        string? ko,
        string? zh)
    {
        return AppLanguage.Current switch
        {
            "en" => en ?? vi,
            "ja" => ja ?? vi,
            "ko" => ko ?? vi,
            "zh" => zh ?? vi,
            _ => vi
        };
    }
}