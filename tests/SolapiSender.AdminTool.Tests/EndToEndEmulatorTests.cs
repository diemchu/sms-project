using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SolapiSender.AdminTool.Services;
using SolapiSender.Core.Firebase;
using SolapiSender.Core.Models.Firebase;

namespace SolapiSender.AdminTool.Tests;

/// <summary>
/// Kiểm thử đầu-cuối trên Firebase Emulator (Auth + Firestore + firestore.rules):
/// AdminTool (Admin SDK) quản lý user ⇄ UserApp (REST client của Core) đăng nhập và dùng dữ liệu.
///   cd firebase && firebase emulators:exec --only auth,firestore --project demo-solapisender "dotnet test ../tests/SolapiSender.AdminTool.Tests"
/// </summary>
[Trait("Category", "Emulator")]
public sealed class EndToEndEmulatorTests : IDisposable
{
    private const string ProjectId = "demo-solapisender";
    private static readonly string? FirestoreHost = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
    private static readonly string? AuthHost = Environment.GetEnvironmentVariable("FIREBASE_AUTH_EMULATOR_HOST");
    private static bool Ready => FirestoreHost is not null && AuthHost is not null;

    private readonly FirebaseAdminContext _ctx = new();
    private readonly FirebaseUserAdminService _admin;
    private readonly FirebaseAdminAuthService _adminAuth;

    public EndToEndEmulatorTests()
    {
        if (Ready) _ctx.InitializeForEmulator(ProjectId);
        _admin = new FirebaseUserAdminService(_ctx, NullLogger<FirebaseUserAdminService>.Instance);
        _adminAuth = new FirebaseAdminAuthService(_ctx, NullLogger<FirebaseAdminAuthService>.Instance);
    }

    public void Dispose() => _ctx.Dispose();

    /// <summary>Phía UserApp: đăng nhập REST rồi dùng LicenseService / FirestoreService của Core.</summary>
    private static (FirebaseAuthService Auth, FirestoreService Db) Client()
    {
        var options = new FirebaseOptions
        {
            WebApiKey = "fake-api-key",
            ProjectId = ProjectId,
            AuthEmulatorHost = AuthHost,
            FirestoreEmulatorHost = FirestoreHost,
        };
        var auth = new FirebaseAuthService(new HttpClient(), options);
        return (auth, new FirestoreService(new HttpClient(), options, auth));
    }

    private static UserEditModel NewUser(int maxDevices = 1) => new()
    {
        Email = $"shop{Guid.NewGuid():N}"[..14] + "@test.kr",
        Password = "Passw0rd!",
        StoreName = "행복폰 테스트점",
        Phone = "01012345678",
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        MaxDevices = maxDevices,
    };

    [SkippableFact]
    public async Task Admin_creates_user_then_userapp_logs_in_and_registers_device()
    {
        Skip.IfNot(Ready, "Cần FIRESTORE_EMULATOR_HOST và FIREBASE_AUTH_EMULATOR_HOST");
        var model = NewUser(maxDevices: 1);
        var created = await _admin.CreateUserAsync(model);

        var (auth, db) = Client();
        var session = await auth.SignInAsync(model.Email, model.Password!);
        Assert.Equal(created.Uid, session.Uid);

        var license = new LicenseService(db);
        var r = await license.CheckAndRegisterDeviceAsync(session.Uid, "pcA", "POS-1", "1.0.0");
        Assert.Equal(LicenseStatus.Ok, r.Status);
        Assert.Equal("행복폰 테스트점", r.Profile!.StoreName);
        Assert.Equal(UserStatus.Active, r.Profile.Status);

        // PC thứ 2 vượt giới hạn.
        Assert.Equal(LicenseStatus.DeviceLimitReached,
            (await license.CheckAndRegisterDeviceAsync(session.Uid, "pcB", "POS-2", "1.0.0")).Status);

        // Admin thấy thiết bị, gỡ đi → máy mới đăng ký được.
        var devices = await _admin.ListDevicesAsync(created.Uid);
        Assert.Equal("pcA", Assert.Single(devices).MachineId);
        await _admin.RemoveDeviceAsync(created.Uid, "pcA");
        Assert.Equal(LicenseStatus.Ok, (await license.CheckAndRegisterDeviceAsync(session.Uid, "pcB", "POS-2", "1.0.0")).Status);

        var listed = (await _admin.ListUsersAsync()).Single(u => u.Uid == created.Uid);
        Assert.Equal(["pcB"], listed.DeviceIds);
        Assert.False(listed.IsOrphan);
    }

    [SkippableFact]
    public async Task Locking_user_blocks_login_refresh_and_data()
    {
        Skip.IfNot(Ready, "Cần FIRESTORE_EMULATOR_HOST và FIREBASE_AUTH_EMULATOR_HOST");
        var model = NewUser();
        var created = await _admin.CreateUserAsync(model);
        var (auth, db) = Client();
        var session = await auth.SignInAsync(model.Email, model.Password!);
        await new LicenseService(db).CheckAndRegisterDeviceAsync(session.Uid, "pcA", "POS-1", "1.0.0");

        await _admin.SetLockedAsync(created.Uid, locked: true);

        // Đăng nhập mới bị từ chối.
        var (auth2, _) = Client();
        var ex = await Assert.ThrowsAsync<FirebaseAuthException>(() => auth2.SignInAsync(model.Email, model.Password!));
        Assert.Equal(FirebaseAuthErrorKind.UserDisabled, ex.Kind);

        // Làm mới token của phiên cũ bị từ chối (dùng đối tượng riêng vì thất bại sẽ tự đăng xuất phiên đó).
        var (authRefresh, _) = Client();
        await Assert.ThrowsAsync<FirebaseAuthException>(() => authRefresh.RestoreAsync(session.RefreshToken));
        Assert.Null(authRefresh.Session);

        // Phiên cũ (ID token còn hạn) vẫn bị rules chặn ghi dữ liệu, nhưng đọc được trạng thái để báo "đã khóa".
        var license = new LicenseService(db);
        Assert.Equal(LicenseStatus.Locked, (await license.CheckStatusAsync(session.Uid)).Status);
        await Assert.ThrowsAsync<FirestoreException>(() => db.CreateAsync($"users/{session.Uid}/templates",
            new MessageTemplate { Name = "x", Content = "y" }));

        // Mở khóa → đăng nhập lại được.
        await _admin.SetLockedAsync(created.Uid, locked: false);
        var (auth3, _) = Client();
        Assert.Equal(created.Uid, (await auth3.SignInAsync(model.Email, model.Password!)).Uid);
    }

    [SkippableFact]
    public async Task Update_extends_expiry_and_changes_password_delete_removes_everything()
    {
        Skip.IfNot(Ready, "Cần FIRESTORE_EMULATOR_HOST và FIREBASE_AUTH_EMULATOR_HOST");
        var model = NewUser();
        model.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        var created = await _admin.CreateUserAsync(model);
        var (auth, db) = Client();
        var session = await auth.SignInAsync(model.Email, model.Password!);
        Assert.Equal(LicenseStatus.Expired, (await new LicenseService(db).CheckStatusAsync(session.Uid)).Status);

        model.ExpiresAt = DateTimeOffset.UtcNow.AddMonths(3);
        model.Password = "NewPassw0rd";
        await _admin.UpdateUserAsync(created.Uid, model);

        var (auth2, db2) = Client();
        var s2 = await auth2.SignInAsync(model.Email, "NewPassw0rd");
        Assert.Equal(LicenseStatus.Ok, (await new LicenseService(db2).CheckStatusAsync(s2.Uid)).Status);
        await new ActivityLogService(db2).LogAsync(s2.Uid, "pcA", ActivityLogTypes.Send, "3/3");

        var logs = await _admin.ListLogsAsync(created.Uid, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), 50);
        Assert.Equal("send", Assert.Single(logs).Type);
        Assert.Equal("행복폰 테스트점", logs[0].UserLabel);
        var allLogs = await _admin.ListLogsAsync(null, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1), 500);
        Assert.Contains(allLogs, l => l.Uid == created.Uid && l.Type == "send");

        await _admin.DeleteUserAsync(created.Uid);
        Assert.DoesNotContain(await _admin.ListUsersAsync(), u => u.Uid == created.Uid);
        var (auth3, _) = Client();
        await Assert.ThrowsAsync<FirebaseAuthException>(() => auth3.SignInAsync(model.Email, "NewPassw0rd"));
    }

    [SkippableFact]
    public async Task Duplicate_email_is_rejected_with_friendly_message()
    {
        Skip.IfNot(Ready, "Cần FIRESTORE_EMULATOR_HOST và FIREBASE_AUTH_EMULATOR_HOST");
        var model = NewUser();
        await _admin.CreateUserAsync(model);
        var ex = await Assert.ThrowsAsync<AdminException>(() => _admin.CreateUserAsync(model));
        Assert.Contains("đã được dùng", ex.Message);
    }

    [SkippableFact]
    public async Task Admin_bootstrap_forces_password_change_and_locks_out_after_failures()
    {
        Skip.IfNot(Ready, "Cần FIRESTORE_EMULATOR_HOST và FIREBASE_AUTH_EMULATOR_HOST");
        // Dọn admins để mô phỏng lần chạy đầu.
        foreach (var d in (await _ctx.Db.Collection("admins").GetSnapshotAsync()).Documents) await d.Reference.DeleteAsync();

        Assert.Equal(AdminLoginStatus.MustChangePassword, (await _adminAuth.LoginAsync("admin", "admin")).Status);

        await Assert.ThrowsAsync<AdminException>(() => _adminAuth.ChangePasswordAsync("admin", "admin", "admin12345"));
        await _adminAuth.ChangePasswordAsync("admin", "admin", "Shop2026secure");

        Assert.Equal(AdminLoginStatus.Invalid, (await _adminAuth.LoginAsync("admin", "admin")).Status);
        Assert.Equal(AdminLoginStatus.Ok, (await _adminAuth.LoginAsync("ADMIN ", "Shop2026secure")).Status);

        for (var i = 0; i < 4; i++) await _adminAuth.LoginAsync("admin", "wrong");
        Assert.Equal(AdminLoginStatus.LockedOut, (await _adminAuth.LoginAsync("admin", "wrong")).Status);
        Assert.Equal(AdminLoginStatus.LockedOut, (await _adminAuth.LoginAsync("admin", "Shop2026secure")).Status);

        var stored = await _ctx.Db.Collection("admins").Document("admin").GetSnapshotAsync();
        Assert.StartsWith("pbkdf2-sha256$", stored.GetValue<string>("passwordHash"));
        Assert.False(stored.GetValue<bool>("mustChangePassword"));
    }
}
