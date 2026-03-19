using SQLite;

namespace TravelGuide.Models;

public class TouristPlace
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // Tên theo 2 ngôn ngữ
    public string NameVi { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;

    // Mô tả theo 2 ngôn ngữ
    public string DescVi { get; set; } = string.Empty;
    public string DescEn { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Radius { get; set; } = 200;
    public string? ImageUrl { get; set; }

    // --- CÁC THUỘC TÍNH THÔNG MINH (Chỉ dùng để hiển thị, không lưu DB) ---

    [Ignore]
    public string Name => GetCurrentName();

    [Ignore]
    public string Description => GetCurrentDescription();

    [Ignore]
    public string Summary => Description?.Length > 100
        ? Description.Substring(0, 97) + "..."
        : Description ?? "";

    // Hàm bổ trợ để lấy đúng ngôn ngữ hiện tại
    private string GetCurrentName()
    {
        // Lấy 2 chữ cái đầu của ngôn ngữ đang chạy (ví dụ: "vi" hoặc "en")
        var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return culture == "vi" ? NameVi : NameEn;
    }

    private string GetCurrentDescription()
    {
        var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return culture == "vi" ? DescVi : DescEn;
    }
}