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

    // ✅ Thêm 3 ngôn ngữ mới — nullable vì chưa dịch ngay
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
            "ja" => NameJa ?? NameEn,
            "ko" => NameKo ?? NameEn,
            "zh" => NameZh ?? NameEn,
            "vi" => NameVi,
            _ => NameEn
        };
    }

    private string GetCurrentDescription()
    {
        return AppLanguage.Current switch
        {
            "ja" => DescJa ?? DescEn,
            "ko" => DescKo ?? DescEn,
            "zh" => DescZh ?? DescEn,
            "vi" => DescVi,
            _ => DescEn
        };
    }
}