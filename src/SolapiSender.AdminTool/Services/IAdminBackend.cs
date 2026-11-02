namespace SolapiSender.AdminTool.Services;

/// <summary>Đăng nhập AdminTool (tài khoản lưu ở collection "admins", hash PBKDF2).</summary>
public interface IAdminAuthService
{
    string? CurrentAdmin { get; }

    Task<AdminLoginResult> LoginAsync(string username, string password, CancellationToken ct = default);

    /// <summary>Đổi mật khẩu; ném <see cref="AdminException"/> nếu mật khẩu hiện tại sai hoặc không đạt chính sách.</summary>
    Task ChangePasswordAsync(string username, string currentPassword, string newPassword, CancellationToken ct = default);

    void Logout();
}

/// <summary>Quản lý chủ quán: Firebase Auth + Firestore users/{uid}.</summary>
public interface IUserAdminService
{
    ServiceAccountInfo Account { get; }

    Task<IReadOnlyList<AdminUser>> ListUsersAsync(CancellationToken ct = default);
    Task<AdminUser> CreateUserAsync(UserEditModel model, CancellationToken ct = default);
    Task UpdateUserAsync(string uid, UserEditModel model, CancellationToken ct = default);

    /// <summary>Khóa = disable trên Auth + thu hồi refresh token + status "locked" trên Firestore.</summary>
    Task SetLockedAsync(string uid, bool locked, CancellationToken ct = default);

    /// <summary>Xóa Auth user, users/{uid} và mọi dữ liệu con.</summary>
    Task DeleteUserAsync(string uid, CancellationToken ct = default);

    Task<IReadOnlyList<AdminDevice>> ListDevicesAsync(string uid, CancellationToken ct = default);
    Task RemoveDeviceAsync(string uid, string machineId, CancellationToken ct = default);
    Task ResetDevicesAsync(string uid, CancellationToken ct = default);

    /// <param name="uid">null = mọi người dùng.</param>
    Task<IReadOnlyList<AdminLog>> ListLogsAsync(string? uid, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct = default);
}
