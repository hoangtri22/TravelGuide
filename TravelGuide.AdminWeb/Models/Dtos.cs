namespace TravelGuide.AdminWeb.Models;

/// <summary>Thông tin user đã xác thực gắn với Bearer token trong bộ nhớ.</summary>
public record AuthPrincipal(int UserId, string Username, string Role);

/// <summary>Body đăng nhập: username + mật khẩu thô (so hash trên server).</summary>
public record LoginRequest(string? Username, string? Password);

/// <summary>Đăng ký chủ quán (role <c>owner</c> cố định phía server).</summary>
public record RegisterRequest(string? Username, string? Password, string? DisplayName);

/// <summary>Yêu cầu tạo tài khoản mới (chỉ admin).</summary>
public record CreateAccountRequest(string Username, string Password, string DisplayName, string Role);

/// <summary>Cập nhật hiển thị/vai trò và tùy chọn đổi mật khẩu.</summary>
public record UpdateAccountRequest(string DisplayName, string Role, string? NewPassword);

/// <summary>Ghi đè toàn bộ field dịch ngoài tiếng Việt cho một POI.</summary>
public record TranslationUpdateRequest(string NameEn, string NameJa, string NameKo, string NameZh, string DescEn, string DescJa, string DescKo, string DescZh);

/// <summary>Lý do từ chối POI (admin).</summary>
public record RejectPoiRequest(string? Reason);

/// <summary>Bản ghi user trong SQLite. <see cref="IsLocked"/> = khóa; chủ quán tự đăng ký có <see cref="RegistrationApproved"/> = false cho đến khi admin duyệt.</summary>
public record UserAccount(int Id, string Username, string PasswordHash, string DisplayName, string Role, bool IsLocked = false, bool RegistrationApproved = true);

/// <summary>Admin bật/tắt khóa tài khoản (không áp dụng cho user <c>admin</c>).</summary>
public record SetAccountLockRequest(bool Locked);

/// <summary>Dòng export JSON cho app offline (extra_places): thêm audio, ưu tiên, link bản đồ.</summary>
public record ExportPoi(
    string NameVi,
    string DescVi,
    double Latitude,
    double Longitude,
    double Radius,
    string ImagePath,
    string AudioUrl,
    string QrImagePath,
    int Priority,
    string MapLink,
    decimal Price,
    string Tag);

/// <summary>POI đầy đủ: đa ngôn ngữ, vị trí, trạng thái duyệt, chủ sở hữu, ưu tiên geofence, link bản đồ ngoài.</summary>
public record PoiDto(
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
    string ImagePath = "",
    string AudioUrl = "",
    string? QrImagePath = null,
    string Status = "",
    string RejectReason = "",
    int OwnerUserId = 0,
    int Priority = 0,
    string MapLink = "",
    decimal Price = 1000,
    string Tag = "Địa Điểm Du Lịch");

public record TouristUserDto(int Id, string Username, string DisplayName, string AccountTier, DateTime CreatedAtUtc, int VisitCount);
public record UpdateTouristTierRequest(string? AccountTier);
public record TouristRefreshTokenDto(long Id, int TouristUserId, string Username, string DeviceId, DateTime ExpiresAtUtc, DateTime? RevokedAtUtc, DateTime CreatedAtUtc);
public record TouristFavoriteDto(long Id, int TouristUserId, string Username, int PoiId, string PoiNameVi, DateTime CreatedAtUtc);
public record TouristVisitHistoryDto(long Id, int TouristUserId, string Username, int PoiId, string PoiNameVi, string EventType, int PlaybackSeconds, decimal WatchedPercent, DateTime OccurredAtUtc);
public record PaymentTransactionDto(long Id, int TouristUserId, string Username, string Provider, string ProviderRef, string PlanCode, string Currency, decimal Amount, string Status, DateTime CreatedAtUtc);

/// <summary>Một dòng lịch sử quét QR mở POI (app → API ghi DB).</summary>
public record PoiQrScanLogDto(
    long Id,
    int TouristUserId,
    string Username,
    int PoiId,
    string PoiNameVi,
    string EventType,
    decimal AmountVnd,
    string? DeviceId,
    string? DeviceModel,
    string? AppPlatform,
    DateTime CreatedAtUtc);

/// <summary>Tổng doanh thu (AmountVnd) theo POI từ log quét.</summary>
public record PoiQrScanRevenueDto(int PoiId, string PoiNameVi, decimal TotalVnd, int ScanCount);

/// <summary>Yêu cầu lọc/phân trang bình luận cho màn quản trị.</summary>
public record CommentQueryRequest(string? Status, string? Search, int Page = 1, int PageSize = 20);

/// <summary>Dòng bình luận du khách để admin duyệt.</summary>
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

/// <summary>Tổng quan nhanh trạng thái bình luận.</summary>
public record CommentStatsDto(int Total, int Pending, int Approved, int Rejected, int Hidden);

/// <summary>Response danh sách bình luận có phân trang.</summary>
public record CommentListResponseDto(
    IReadOnlyList<TouristCommentDto> Items,
    CommentStatsDto Stats,
    int Page,
    int PageSize,
    int TotalItems);

/// <summary>Cập nhật trạng thái bình luận (pending/approved/rejected/hidden).</summary>
public record UpdateCommentStatusRequest(string? Status, string? Reason);

/// <summary>Admin trả lời một bình luận.</summary>
public record ReplyCommentRequest(string? Reply);

/// <summary>Tạo bình luận mới (hỗ trợ seed/test backend).</summary>
public record CreateCommentRequest(
    int? TouristUserId,
    string? Username,
    int PoiId,
    string? PoiNameVi,
    int Rating,
    string? Content,
    string? Status);
