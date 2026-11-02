using SolapiSender.AdminTool.Services;
using SolapiSender.Core.Models.Firebase;
using SolapiSender.Core.Security;

namespace SolapiSender.AdminTool.Preview;

/// <summary>
/// Backend giả lập trong bộ nhớ cho chế độ xem trước giao diện (--preview, chỉ bản Debug).
/// Hành vi giống backend thật (bootstrap admin/admin, bắt buộc đổi mật khẩu, khóa sau 5 lần sai…).
/// </summary>
public sealed class InMemoryAdminBackend : IAdminAuthService, IUserAdminService
{
    private readonly Dictionary<string, (string Hash, bool MustChange)> _admins = new()
    {
        ["admin"] = (PasswordHasher.Hash("admin", 1000), true),
    };
    private readonly List<AdminUser> _users = [];
    private readonly Dictionary<string, List<AdminDevice>> _devices = [];
    private readonly List<AdminLog> _logs = [];
    private int _failures;

    public InMemoryAdminBackend()
    {
        var now = DateTimeOffset.UtcNow;
        (string Store, string Email, int Days, UserStatus Status, int Max, int Dev)[] seed =
        [
            ("행복폰 강남점", "gangnam@shop.kr", 45, UserStatus.Active, 2, 1),
            ("스마트폰마트 홍대", "hongdae@shop.kr", 5, UserStatus.Active, 1, 1),
            ("굿모바일 부산", "busan@shop.kr", -3, UserStatus.Active, 1, 1),
            ("폰나라 인천", "incheon@shop.kr", 120, UserStatus.Locked, 3, 2),
            ("모바일킹 대전", "daejeon@shop.kr", 300, UserStatus.Active, 2, 0),
        ];
        var i = 0;
        foreach (var s in seed)
        {
            var uid = $"uid{++i:D3}PREVIEW";
            var devices = Enumerable.Range(1, s.Dev).Select(n =>
                new AdminDevice($"{uid[..6]}pc{n}", $"POS-PC-{n}", "1.0.0", now.AddDays(-30 * n), now.AddHours(-n * 7))).ToList();
            _devices[uid] = devices;
            _users.Add(new AdminUser
            {
                Uid = uid, Email = s.Email, StoreName = s.Store, Phone = $"0101234{i:D4}", Status = s.Status,
                ExpiresAt = now.AddDays(s.Days), MaxDevices = s.Max, DeviceIds = devices.Select(d => d.MachineId).ToList(),
                CreatedAt = now.AddDays(-200 + i * 20), LastSignInAt = now.AddHours(-i * 5),
            });
            string[] types = ["login", "send", "schedule", "send", "error", "connect_api"];
            for (var k = 0; k < 6; k++)
            {
                _logs.Add(new AdminLog(uid, s.Store, types[k], types[k] switch
                {
                    "send" => $"{120 - k * 7}/{124 - k * 7}",
                    "login" => "POS-PC-1 v1.0.0",
                    "error" => "NotEnoughBalance",
                    _ => "OK",
                }, $"{uid[..6]}pc1", now.AddHours(-(i * 9 + k * 13)),
                types[k] == "send" ? """{"groupIds":["G4V2025PREVIEW"],"accepted":120,"rejected":4,"estimatedCost":2160}""" : ""));
            }
        }
    }

    public ServiceAccountInfo Account { get; } = new("demo-solapisender", "firebase-adminsdk@preview.iam.gserviceaccount.com");

    // ------------------------------------------------------------------ admin auth

    public string? CurrentAdmin { get; private set; }

    public async Task<AdminLoginResult> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        await Task.Delay(300, ct);
        if (_failures >= 5) return new AdminLoginResult(AdminLoginStatus.LockedOut, TimeSpan.FromMinutes(5));
        var key = username.Trim().ToLowerInvariant();
        if (!_admins.TryGetValue(key, out var a) || !PasswordHasher.Verify(password, a.Hash))
        {
            _failures++;
            return new AdminLoginResult(_failures >= 5 ? AdminLoginStatus.LockedOut : AdminLoginStatus.Invalid);
        }
        _failures = 0;
        CurrentAdmin = key;
        return new AdminLoginResult(a.MustChange ? AdminLoginStatus.MustChangePassword : AdminLoginStatus.Ok);
    }

    public async Task ChangePasswordAsync(string username, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        await Task.Delay(300, ct);
        var key = username.Trim().ToLowerInvariant();
        if (!_admins.TryGetValue(key, out var a) || !PasswordHasher.Verify(currentPassword, a.Hash))
            throw new AdminException("Mật khẩu hiện tại không đúng.");
        if (PasswordHasher.ValidatePolicy(newPassword, key) is { } reason) throw new AdminException(reason);
        _admins[key] = (PasswordHasher.Hash(newPassword, 1000), false);
    }

    public void Logout() => CurrentAdmin = null;

    // ------------------------------------------------------------------ users

    public async Task<IReadOnlyList<AdminUser>> ListUsersAsync(CancellationToken ct = default)
    {
        await Task.Delay(300, ct);
        return _users.OrderBy(u => u.StoreName).ToList();
    }

    public async Task<AdminUser> CreateUserAsync(UserEditModel m, CancellationToken ct = default)
    {
        await Task.Delay(300, ct);
        if (_users.Any(u => u.Email.Equals(m.Email.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new AdminException("Email này đã được dùng cho tài khoản khác.");
        var user = new AdminUser
        {
            Uid = "uid" + Guid.NewGuid().ToString("N")[..8], Email = m.Email.Trim(), StoreName = m.StoreName.Trim(),
            Phone = m.Phone, Status = m.Status, ExpiresAt = m.ExpiresAt, MaxDevices = m.MaxDevices, CreatedAt = DateTimeOffset.UtcNow,
        };
        _users.Add(user);
        _devices[user.Uid] = [];
        return user;
    }

    public async Task UpdateUserAsync(string uid, UserEditModel m, CancellationToken ct = default)
    {
        await Task.Delay(300, ct);
        var u = Find(uid);
        u.Email = m.Email.Trim();
        u.StoreName = m.StoreName.Trim();
        u.Phone = m.Phone;
        u.Status = m.Status;
        u.ExpiresAt = m.ExpiresAt;
        u.MaxDevices = m.MaxDevices;
    }

    public async Task SetLockedAsync(string uid, bool locked, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);
        Find(uid).Status = locked ? UserStatus.Locked : UserStatus.Active;
    }

    public async Task DeleteUserAsync(string uid, CancellationToken ct = default)
    {
        await Task.Delay(300, ct);
        _users.Remove(Find(uid));
        _devices.Remove(uid);
        _logs.RemoveAll(l => l.Uid == uid);
    }

    public async Task<IReadOnlyList<AdminDevice>> ListDevicesAsync(string uid, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);
        return _devices.TryGetValue(uid, out var d) ? d.ToList() : [];
    }

    public async Task RemoveDeviceAsync(string uid, string machineId, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);
        _devices[uid].RemoveAll(d => d.MachineId == machineId);
        Find(uid).DeviceIds.Remove(machineId);
    }

    public async Task ResetDevicesAsync(string uid, CancellationToken ct = default)
    {
        await Task.Delay(200, ct);
        _devices[uid].Clear();
        Find(uid).DeviceIds.Clear();
    }

    public async Task<IReadOnlyList<AdminLog>> ListLogsAsync(string? uid, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct = default)
    {
        await Task.Delay(300, ct);
        return _logs.Where(l => (uid is null || l.Uid == uid) && l.CreatedAt >= from && l.CreatedAt <= to)
            .OrderByDescending(l => l.CreatedAt).Take(limit).ToList();
    }

    private AdminUser Find(string uid) => _users.FirstOrDefault(u => u.Uid == uid) ?? throw new AdminException("Không tìm thấy người dùng.");
}
