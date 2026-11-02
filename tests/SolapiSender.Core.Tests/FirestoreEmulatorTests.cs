using System.Text;
using System.Text.Json;
using SolapiSender.Core.Firebase;
using SolapiSender.Core.Models.Firebase;

namespace SolapiSender.Core.Tests;

/// <summary>
/// Kiểm thử tích hợp: REST client C# + firestore.rules trên Firestore Emulator.
/// Chỉ chạy khi có biến môi trường FIRESTORE_EMULATOR_HOST (firebase emulators:exec tự đặt):
///   cd firebase && firebase emulators:exec --only firestore --project demo-solapisender "dotnet test ../tests/SolapiSender.Core.Tests --filter Category=Emulator"
/// </summary>
[Trait("Category", "Emulator")]
public sealed class FirestoreEmulatorTests
{
    private const string ProjectId = "demo-solapisender";
    private static readonly string? Host = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");

    private sealed class StaticToken(string token) : IIdTokenProvider
    {
        public Task<string> GetIdTokenAsync(CancellationToken ct = default) => Task.FromResult(token);
    }

    /// <summary>Emulator chấp nhận JWT không ký (alg=none) làm ID token của người dùng.</summary>
    private static string FakeIdToken(string uid)
    {
        static string B64(object o) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(o))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header = B64(new { alg = "none", typ = "JWT" });
        var payload = B64(new Dictionary<string, object>
        {
            ["iss"] = $"https://securetoken.google.com/{ProjectId}",
            ["aud"] = ProjectId,
            ["sub"] = uid,
            ["user_id"] = uid,
            ["iat"] = now,
            ["exp"] = now + 3600,
            ["auth_time"] = now,
            ["firebase"] = new { sign_in_provider = "password" },
        });
        return $"{header}.{payload}.";
    }

    private static FirestoreService Db(string token) => new(new HttpClient(),
        new FirebaseOptions { ProjectId = ProjectId, FirestoreEmulatorHost = Host }, new StaticToken(token));

    /// <summary>"owner" = quyền admin của emulator (bỏ qua rules), dùng để tạo dữ liệu mẫu như AdminTool.</summary>
    private static FirestoreService Admin => Db("owner");

    private static async Task<string> SeedUserAsync(int maxDevices = 1, UserStatus status = UserStatus.Active, int expiresInDays = 30)
    {
        var uid = "u" + Guid.NewGuid().ToString("N")[..10];
        await Admin.SetAsync($"users/{uid}", new UserProfile
        {
            Email = $"{uid}@shop.kr",
            StoreName = "테스트",
            Status = status,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(expiresInDays),
            MaxDevices = maxDevices,
            DeviceIds = [],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        return uid;
    }

    [SkippableFact]
    public async Task Register_device_then_limit_is_enforced_by_client_and_rules()
    {
        Skip.If(Host is null, "FIRESTORE_EMULATOR_HOST chưa đặt");
        var uid = await SeedUserAsync(maxDevices: 1);
        var license = new LicenseService(Db(FakeIdToken(uid)));

        var first = await license.CheckAndRegisterDeviceAsync(uid, "pcA", "PC-A", "1.0.0");
        Assert.Equal(LicenseStatus.Ok, first.Status);

        var again = await license.CheckAndRegisterDeviceAsync(uid, "pcA", "PC-A", "1.0.1");
        Assert.Equal(LicenseStatus.Ok, again.Status);
        Assert.Equal(["pcA"], again.Profile!.DeviceIds);

        var second = await license.CheckAndRegisterDeviceAsync(uid, "pcB", "PC-B", "1.0.0");
        Assert.Equal(LicenseStatus.DeviceLimitReached, second.Status);

        // Bỏ qua kiểm tra phía client → rules vẫn chặn.
        var ex = await Assert.ThrowsAsync<FirestoreException>(() => Db(FakeIdToken(uid)).CommitAsync(
        [
            FirestoreWrite.Create($"users/{uid}/devices/pcB", new DeviceInfo { MachineName = "PC-B", AppVersion = "1" }),
            FirestoreWrite.ArrayUnion($"users/{uid}", "deviceIds", "pcB"),
        ]));
        Assert.True(ex.IsPermissionDenied);
    }

    [SkippableFact]
    public async Task Templates_settings_and_logs_roundtrip_through_rules()
    {
        Skip.If(Host is null, "FIRESTORE_EMULATOR_HOST chưa đặt");
        var uid = await SeedUserAsync();
        var db = Db(FakeIdToken(uid));

        var id = await db.CreateAsync($"users/{uid}/templates", new MessageTemplate
        {
            Name = "신모델", Content = "#{이름}님 안녕하세요", IsAd = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        var list = await db.ListAllAsync<MessageTemplate>($"users/{uid}/templates", "updatedAt desc");
        Assert.Equal(id, Assert.Single(list).Id);
        Assert.True(list[0].IsAd);

        await db.SetAsync($"users/{uid}/settings/app", new UserCloudSettings { DefaultSender = "0215881234", UpdatedAt = DateTimeOffset.UtcNow });
        Assert.Equal("0215881234", (await db.GetAsync<UserCloudSettings>($"users/{uid}/settings/app"))!.DefaultSender);

        await new ActivityLogService(db).LogAsync(uid, "pcA", ActivityLogTypes.Send, "6/6",
            new Dictionary<string, object?> { ["groupIds"] = new List<string> { "G1" }, ["accepted"] = 6, ["estimatedCost"] = 270m });
        var logs = await Admin.ListAllAsync<ActivityLog>($"users/{uid}/logs");
        Assert.Equal("send", Assert.Single(logs).Type);

        // Client không đọc được log.
        await Assert.ThrowsAsync<FirestoreException>(() => db.ListAllAsync<ActivityLog>($"users/{uid}/logs"));
    }

    [SkippableFact]
    public async Task Locked_or_expired_user_is_blocked_but_can_read_own_profile()
    {
        Skip.If(Host is null, "FIRESTORE_EMULATOR_HOST chưa đặt");
        foreach (var uid in new[] { await SeedUserAsync(status: UserStatus.Locked), await SeedUserAsync(expiresInDays: -1) })
        {
            var db = Db(FakeIdToken(uid));
            var license = new LicenseService(db);

            var r = await license.CheckAndRegisterDeviceAsync(uid, "pcA", "PC-A", "1.0.0");
            Assert.NotEqual(LicenseStatus.Ok, r.Status);
            Assert.NotNull(r.Profile);

            var ex = await Assert.ThrowsAsync<FirestoreException>(() => db.CreateAsync($"users/{uid}/templates",
                new MessageTemplate { Name = "x", Content = "y" }));
            Assert.True(ex.IsPermissionDenied);
        }
    }

    [SkippableFact]
    public async Task User_cannot_touch_other_user_or_admins()
    {
        Skip.If(Host is null, "FIRESTORE_EMULATOR_HOST chưa đặt");
        var alice = await SeedUserAsync();
        var bob = await SeedUserAsync();
        var db = Db(FakeIdToken(alice));

        await Assert.ThrowsAsync<FirestoreException>(() => db.GetAsync<UserProfile>($"users/{bob}"));
        await Assert.ThrowsAsync<FirestoreException>(() => db.UpdateAsync($"users/{alice}",
            new Dictionary<string, object?> { ["maxDevices"] = 99 }));
        await Assert.ThrowsAsync<FirestoreException>(() => db.GetAsync<UserProfile>("admins/admin"));
    }
}
