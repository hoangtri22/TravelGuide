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
    int Priority,
    string MapLink);

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
    string Status = "",
    string RejectReason = "",
    int OwnerUserId = 0,
    int Priority = 0,
    string MapLink = "");
