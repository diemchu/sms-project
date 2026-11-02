using SolapiSender.Core.Models.Firebase;

namespace SolapiSender.AdminTool.Services;

/// <summary>Người dùng (chủ quán) = Firebase Auth user + document users/{uid}.</summary>
public sealed class AdminUser
{
    public required string Uid { get; init; }
    public string Email { get; set; } = "";
    public string StoreName { get; set; } = "";
    public string Phone { get; set; } = "";
    public UserStatus Status { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int MaxDevices { get; set; } = 1;
    public List<string> DeviceIds { get; set; } = [];
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? LastSignInAt { get; set; }

    /// <summary>Có trong Firebase Auth nhưng thiếu document users/{uid} (hoặc ngược lại).</summary>
    public bool IsOrphan { get; set; }

    public int DaysLeft => (int)Math.Ceiling((ExpiresAt - DateTimeOffset.UtcNow).TotalDays);
    public bool IsExpired => ExpiresAt <= DateTimeOffset.UtcNow;
    public bool IsExpiringSoon => !IsExpired && DaysLeft <= 7;

    public string StateKey => IsOrphan || Status == UserStatus.Locked ? "error"
        : IsExpired ? "error"
        : IsExpiringSoon ? "warn"
        : "ok";

    public string StatusText => IsOrphan ? "Thiếu dữ liệu"
        : Status == UserStatus.Locked ? "Đã khóa"
        : IsExpired ? "Hết hạn"
        : IsExpiringSoon ? "Sắp hết hạn"
        : "Hoạt động";

    public string DevicesText => $"{DeviceIds.Count} / {MaxDevices}";
    public string PhoneText => Core.PhoneNumberHelper.Format(Phone);
    public string ExpiresText => $"{ExpiresAt.ToLocalTime():yyyy-MM-dd} ({(IsExpired ? "đã hết" : $"còn {DaysLeft} ngày")})";
}

/// <summary>Dữ liệu từ form thêm/sửa.</summary>
public sealed class UserEditModel
{
    public string Email { get; set; } = "";

    /// <summary>Bắt buộc khi tạo mới; null/rỗng khi sửa = giữ nguyên mật khẩu.</summary>
    public string? Password { get; set; }

    public string StoreName { get; set; } = "";
    public string Phone { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;
    public int MaxDevices { get; set; } = 1;
}

public sealed record AdminDevice(string MachineId, string MachineName, string AppVersion, DateTimeOffset? FirstSeenAt, DateTimeOffset? LastSeenAt);

public sealed record AdminLog(
    string Uid,
    string UserLabel,
    string Type,
    string Message,
    string MachineId,
    DateTimeOffset CreatedAt,
    string MetaText);

public enum AdminLoginStatus
{
    Ok,
    MustChangePassword,
    Invalid,
    LockedOut,
}

public sealed record AdminLoginResult(AdminLoginStatus Status, TimeSpan? RetryAfter = null);

public sealed record ServiceAccountInfo(string ProjectId, string ClientEmail);

/// <summary>Lỗi nghiệp vụ hiển thị thẳng cho admin (đã là tiếng Việt).</summary>
public sealed class AdminException(string message, Exception? inner = null) : Exception(message, inner);
