using SolapiSender.Core.Firebase;

namespace SolapiSender.Core.Models.Firebase;

public enum UserStatus
{
    Active,
    Locked,
}

/// <summary>
/// users/{uid} — chỉ Admin ghi. Client chỉ đọc, ngoại lệ duy nhất: thêm đúng 1 machineId vào
/// deviceIds cùng batch với việc tạo devices/{machineId} (Security Rules kiểm tra).
/// </summary>
public sealed class UserProfile
{
    [FirestoreDocumentId] public string Uid { get; set; } = "";
    public string Email { get; set; } = "";
    public string StoreName { get; set; } = "";
    public string Phone { get; set; } = "";
    public UserStatus Status { get; set; } = UserStatus.Active;
    public DateTimeOffset ExpiresAt { get; set; }
    public int MaxDevices { get; set; } = 1;

    /// <summary>machineId của các PC đã đăng ký. Dùng danh sách (không dùng bộ đếm) để rules chặn được việc đăng ký nhiều máy trong một batch.</summary>
    public List<string> DeviceIds { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;
}

/// <summary>users/{uid}/devices/{machineId}</summary>
public sealed class DeviceInfo
{
    [FirestoreDocumentId] public string MachineId { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

/// <summary>users/{uid}/templates/{templateId}</summary>
public sealed class MessageTemplate
{
    [FirestoreDocumentId] public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
    public string? Subject { get; set; }
    public bool IsAd { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class ActivityLogTypes
{
    public const string Login = "login";
    public const string Logout = "logout";
    public const string ConnectApi = "connect_api";
    public const string Send = "send";
    public const string Schedule = "schedule";
    public const string Error = "error";
}

/// <summary>users/{uid}/logs/{logId} — client chỉ được tạo, không sửa/xóa.</summary>
public sealed class ActivityLog
{
    [FirestoreDocumentId] public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
    public Dictionary<string, object?> Meta { get; set; } = [];
    public string MachineId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>users/{uid}/settings/app — cài đặt đồng bộ giữa các máy.</summary>
public sealed class UserCloudSettings
{
    public string? DefaultSender { get; set; }
    public decimal LowBalanceThreshold { get; set; } = 10_000m;
    public string? OptOutNumber { get; set; }
    public decimal PriceSms { get; set; } = 18m;
    public decimal PriceLms { get; set; } = 45m;
    public decimal PriceMms { get; set; } = 110m;
    public DateTimeOffset UpdatedAt { get; set; }
}
