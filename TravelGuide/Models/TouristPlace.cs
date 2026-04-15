using SQLite;
using TravelGuide;

namespace TravelGuide.Models;

/// <summary>
/// Một điểm POI: tọa độ, bán kính, đa ngôn ngữ, ảnh, URL audio (ưu tiên hơn TTS), mức ưu tiên geofence, link bản đồ ngoài.
/// </summary>
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

    /// <summary>Số càng lớn càng ưu tiên khi nhiều POI chồng vùng (geofence).</summary>
    public int Priority { get; set; }

    public string? ImagePath { get; set; }

    /// <summary>URL file audio (http/https); nếu có và tải được thì phát trước, không thì TTS.</summary>
    public string? AudioUrl { get; set; }

    /// <summary>Link Google Maps / Mapbox / web mở bằng trình duyệt (tuỳ chọn).</summary>
    public string? MapLink { get; set; }

    /// <summary>Đường dẫn/tên file ảnh QR của quán/POI.</summary>
    public string? QrImagePath { get; set; }

    /// <summary>Giá tham khảo (VND); 0 nếu chưa có.</summary>
    public decimal Price { get; set; }

    /// <summary>Nhóm POI: quan an, quan nuoc, dia diem du lich, di tich lich su.</summary>
    public string Tag { get; set; } = "dia diem du lich";

    /// <summary>Đánh dấu người dùng đã đến POI này.</summary>
    public bool IsVisited { get; set; }

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
        static string Pick(string fallback, string? candidate) =>
            string.IsNullOrWhiteSpace(candidate) ? fallback : candidate;

        return AppLanguage.Current switch
        {
            "en" => Pick(vi, en),
            "ja" => Pick(vi, ja),
            "ko" => Pick(vi, ko),
            "zh" => Pick(vi, zh),
            _ => vi
        };
    }
}
