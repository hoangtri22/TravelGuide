namespace TravelGuide.API.Models;

public record TouristAuthPrincipal(int UserId, string Username);

public record TouristRegisterRequest(string? Username, string? Password, string? DisplayName, string? AccountTier);

public record TouristLoginRequest(string? Username, string? Password);

public record TouristDeviceLoginRequest(string? DeviceId, string? DeviceName, string? AppPlatform);

public record PremiumRedeemRequest(string? ClaimCode, decimal? AmountVnd);

public record PoiUnlockConfirmRequest(decimal? AmountVnd);

public record TouristPoiScanLogRequest(
    int PoiId,
    string? PoiNameVi,
    string? EventType,
    decimal AmountVnd,
    string? DeviceId,
    string? DeviceModel,
    string? AppPlatform);

public record TouristCommentCreateRequest(
    int PoiId,
    string? PoiNameVi,
    int Rating,
    string? Content);

public record TouristCommentDto(
    long Id,
    int? TouristUserId,
    string Username,
    int PoiId,
    string PoiNameVi,
    int Rating,
    string Content,
    string Status,
    string AdminReply,
    string RejectReason,
    DateTime CreatedAtUtc,
    DateTime? AdminReplyAtUtc,
    DateTime UpdatedAtUtc);

/// <summary>Một dòng trong lịch quét: POI mới nhất theo thời gian quét.</summary>
public record MyPoiScanHistoryItemDto(int PoiId, string PoiNameVi, string EventType, decimal AmountVnd, DateTime LastScannedAtUtc);

public record TouristUser(int Id, string Username, string PasswordHash, string DisplayName, string AccountTier);

public record PublicPoiDto(
    int Id,
    string NameVi,
    string NameEn,
    string NameJa,
    string NameKo,
    string NameZh,
    string DescVi,
    string DescEn,
    string DescJa,
    string DescKo,
    string DescZh,
    double Latitude,
    double Longitude,
    double Radius,
    string ImagePath,
    string AudioUrl,
    int Priority,
    string MapLink,
    decimal Price,
    string Tag,
    string QrImagePath);
