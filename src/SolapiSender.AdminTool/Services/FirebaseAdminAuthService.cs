using System.Collections.Concurrent;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;
using SolapiSender.Core.Security;

namespace SolapiSender.AdminTool.Services;

/// <summary>
/// Tài khoản admin lưu ở admins/{username}: passwordHash (PBKDF2), mustChangePassword…
/// Lần đầu (collection trống) tự tạo admin/admin và bắt buộc đổi mật khẩu.
/// </summary>
public sealed class FirebaseAdminAuthService : IAdminAuthService
{
    public const string DefaultUsername = "admin";
    private const string DefaultPassword = "admin";
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockoutTime = TimeSpan.FromMinutes(5);

    private readonly FirebaseAdminContext _ctx;
    private readonly ILogger<FirebaseAdminAuthService> _logger;
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset Until)> _failures = new();

    public FirebaseAdminAuthService(FirebaseAdminContext ctx, ILogger<FirebaseAdminAuthService> logger)
    {
        _ctx = ctx;
        _logger = logger;
    }

    public string? CurrentAdmin { get; private set; }

    private CollectionReference Admins => _ctx.Db.Collection("admins");

    public async Task<AdminLoginResult> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var key = Normalize(username);
        if (key.Length == 0 || key.Contains('/')) return new AdminLoginResult(AdminLoginStatus.Invalid);
        if (_failures.TryGetValue(key, out var f) && f.Count >= MaxFailures && f.Until > DateTimeOffset.UtcNow)
            return new AdminLoginResult(AdminLoginStatus.LockedOut, f.Until - DateTimeOffset.UtcNow);

        await EnsureBootstrapAsync(ct);

        var snap = await Admins.Document(key).GetSnapshotAsync(ct);
        var stored = snap.Exists && snap.TryGetValue<string>("passwordHash", out var h) ? h : null;
        // Vẫn băm khi user không tồn tại để không lộ thông tin qua thời gian phản hồi.
        var ok = await Task.Run(() => PasswordHasher.Verify(password, stored ?? DummyHash.Value), ct) && stored is not null;

        if (!ok)
        {
            var count = (_failures.TryGetValue(key, out var prev) && prev.Until > DateTimeOffset.UtcNow ? prev.Count : 0) + 1;
            _failures[key] = (count, DateTimeOffset.UtcNow.Add(LockoutTime));
            _logger.LogWarning("Đăng nhập admin thất bại cho {User} (lần {Count})", key, count);
            return new AdminLoginResult(count >= MaxFailures ? AdminLoginStatus.LockedOut : AdminLoginStatus.Invalid,
                count >= MaxFailures ? LockoutTime : null);
        }

        _failures.TryRemove(key, out _);
        var updates = new Dictionary<string, object> { ["lastLoginAt"] = FieldValue.ServerTimestamp };
        if (PasswordHasher.NeedsRehash(stored!))
            updates["passwordHash"] = await Task.Run(() => PasswordHasher.Hash(password), ct);
        await snap.Reference.UpdateAsync(updates, cancellationToken: ct);

        CurrentAdmin = key;
        _logger.LogInformation("Admin {User} đăng nhập", key);
        var mustChange = snap.TryGetValue<bool>("mustChangePassword", out var m) && m;
        return new AdminLoginResult(mustChange ? AdminLoginStatus.MustChangePassword : AdminLoginStatus.Ok);
    }

    public async Task ChangePasswordAsync(string username, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var key = Normalize(username);
        var snap = await Admins.Document(key).GetSnapshotAsync(ct);
        if (!snap.Exists || !snap.TryGetValue<string>("passwordHash", out var stored)
            || !await Task.Run(() => PasswordHasher.Verify(currentPassword, stored), ct))
            throw new AdminException("Mật khẩu hiện tại không đúng.");

        if (PasswordHasher.ValidatePolicy(newPassword, key) is { } reason) throw new AdminException(reason);
        if (newPassword == currentPassword) throw new AdminException("Mật khẩu mới phải khác mật khẩu hiện tại.");

        var hash = await Task.Run(() => PasswordHasher.Hash(newPassword), ct);
        await snap.Reference.UpdateAsync(new Dictionary<string, object>
        {
            ["passwordHash"] = hash,
            ["mustChangePassword"] = false,
            ["updatedAt"] = FieldValue.ServerTimestamp,
        }, cancellationToken: ct);
        _logger.LogInformation("Admin {User} đã đổi mật khẩu", key);
    }

    public void Logout() => CurrentAdmin = null;

    /// <summary>Tạo admin/admin nếu collection admins còn trống (CreateAsync thất bại nếu đã tồn tại → an toàn khi chạy song song).</summary>
    private async Task EnsureBootstrapAsync(CancellationToken ct)
    {
        var any = await Admins.Limit(1).GetSnapshotAsync(ct);
        if (any.Count > 0) return;
        try
        {
            await Admins.Document(DefaultUsername).CreateAsync(new Dictionary<string, object>
            {
                ["passwordHash"] = await Task.Run(() => PasswordHasher.Hash(DefaultPassword), ct),
                ["mustChangePassword"] = true,
                ["createdAt"] = FieldValue.ServerTimestamp,
                ["updatedAt"] = FieldValue.ServerTimestamp,
            }, ct);
            _logger.LogWarning("Đã tạo tài khoản admin mặc định (bắt buộc đổi mật khẩu)");
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.AlreadyExists)
        {
        }
    }

    private static string Normalize(string username) => username.Trim().ToLowerInvariant();

    private static readonly Lazy<string> DummyHash = new(() => PasswordHasher.Hash(Guid.NewGuid().ToString()));
}
