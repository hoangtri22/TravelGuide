namespace TravelGuide;

/// <summary>Giá mặc định (đồng VND), đồng bộ với <c>TouristPricing</c> trong <c>TravelGuide.API/appsettings.json</c>.</summary>
public static class TouristPricing
{
    public const decimal PremiumActivationVnd = 60_000m;
    /// <summary>Du khách Free: phí mở nội dung qua quét QR mỗi quán (đồng bộ API <c>TouristPricing:DefaultPoiUnlockVnd</c>).</summary>
    public const decimal DefaultPoiUnlockVnd = 1_000m;
    /// <summary>Chủ quán: phí duy trì mỗi POI / tháng (tham chiếu kinh doanh; thu ngoài app).</summary>
    public const decimal OwnerMonthlyFeePerPoiVnd = 100_000m;
}
