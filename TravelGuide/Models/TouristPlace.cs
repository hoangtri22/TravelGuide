using SQLite;
namespace TravelGuide.Models;

public class TouristPlace
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

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

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Radius { get; set; } = 50;
    public string? ImagePath { get; set; }

    [Ignore]
    public string? ImageSource => string.IsNullOrEmpty(ImagePath)
        ? "placeholder.png" : ImagePath;

    [Ignore]
    public string Name => GetCurrentName();
    [Ignore]
    public string Description => GetCurrentDescription();
    [Ignore]
    public string Summary => Description?.Length > 100
        ? Description.Substring(0, 97) + "..." : Description ?? "";

    private string GetCurrentName()
    {
        return AppLanguage.Current switch
        {
            "en" => !string.IsNullOrEmpty(NameEn) ? NameEn : NameVi,
            "ja" => !string.IsNullOrEmpty(NameJa) ? NameJa : NameVi,
            "ko" => !string.IsNullOrEmpty(NameKo) ? NameKo : NameVi,
            "zh" => !string.IsNullOrEmpty(NameZh) ? NameZh : NameVi,
            _ => NameVi
        };
    }

    private string GetCurrentDescription()
    {
        return AppLanguage.Current switch
        {
            "en" => !string.IsNullOrEmpty(DescEn) ? DescEn : DescVi,
            "ja" => !string.IsNullOrEmpty(DescJa) ? DescJa : DescVi,
            "ko" => !string.IsNullOrEmpty(DescKo) ? DescKo : DescVi,
            "zh" => !string.IsNullOrEmpty(DescZh) ? DescZh : DescVi,
            _ => DescVi
        };
    }
}