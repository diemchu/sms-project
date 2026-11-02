using System.Net;
using System.Text.Json;
using SolapiSender.Core.Firebase;
using SolapiSender.Core.Models.Firebase;

namespace SolapiSender.Core.Tests;

public class FirestoreMapperTests
{
    [Fact]
    public void Roundtrip_user_profile()
    {
        var src = new UserProfile
        {
            Uid = "ignored",
            Email = "a@b.com",
            StoreName = "행복폰",
            Status = UserStatus.Locked,
            ExpiresAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            MaxDevices = 2,
            DeviceIds = ["PC1"],
        };

        var fields = FirestoreMapper.ToFields(src);
        Assert.Null(fields["uid"]);
        Assert.Equal("locked", fields["status"]!["stringValue"]!.GetValue<string>());
        Assert.Equal("2", fields["maxDevices"]!["integerValue"]!.GetValue<string>());
        Assert.Equal("2027-01-01T00:00:00.000000Z", fields["expiresAt"]!["timestampValue"]!.GetValue<string>());

        var doc = JsonDocument.Parse($$"""{"name":"projects/p/databases/(default)/documents/users/U1","fields":{{fields.ToJsonString()}}}""");
        var back = FirestoreMapper.FromDocument<UserProfile>(doc.RootElement);

        Assert.Equal("U1", back.Uid);
        Assert.Equal("행복폰", back.StoreName);
        Assert.Equal(UserStatus.Locked, back.Status);
        Assert.Equal(src.ExpiresAt, back.ExpiresAt);
        Assert.Equal(2, back.MaxDevices);
    }

    [Fact]
    public void Roundtrip_nested_map_and_decimal()
    {
        var log = new ActivityLog
        {
            Type = "send",
            Meta = new() { ["count"] = 3, ["groupId"] = "G1", ["ok"] = true, ["none"] = null },
        };
        var settings = new UserCloudSettings { LowBalanceThreshold = 5000.5m };

        var logBack = FirestoreMapper.FromFields<ActivityLog>(Parse(FirestoreMapper.ToFields(log).ToJsonString()));
        Assert.Equal(3L, logBack.Meta["count"]);
        Assert.Equal("G1", logBack.Meta["groupId"]);
        Assert.Equal(true, logBack.Meta["ok"]);
        Assert.Null(logBack.Meta["none"]);

        var settingsBack = FirestoreMapper.FromFields<UserCloudSettings>(Parse(FirestoreMapper.ToFields(settings).ToJsonString()));
        Assert.Equal(5000.5m, settingsBack.LowBalanceThreshold);
        Assert.Null(settingsBack.DefaultSender);
    }

    [Fact]
    public void Missing_fields_keep_defaults()
    {
        var p = FirestoreMapper.FromFields<UserProfile>(Parse("""{"email":{"stringValue":"x@y.z"}}"""));
        Assert.Equal("x@y.z", p.Email);
        Assert.Equal(1, p.MaxDevices);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;
}

public class FirestoreServiceTests
{
    private sealed class Token : IIdTokenProvider
    {
        public Task<string> GetIdTokenAsync(CancellationToken ct = default) => Task.FromResult("TOKEN");
    }

    [Fact]
    public async Task Commit_builds_create_and_increment_writes()
    {
        var handler = new FakeHandler((_, _) => FakeHandler.Json(HttpStatusCode.OK, "{}"));
        var db = new FirestoreService(new HttpClient(handler), new FirebaseOptions { ProjectId = "proj", WebApiKey = "k" }, new Token());

        await db.CommitAsync([
            FirestoreWrite.Create("users/U/devices/D", new DeviceInfo { MachineName = "PC" }),
            FirestoreWrite.ArrayUnion("users/U", "deviceIds", "D"),
        ]);

        var (req, body) = handler.Requests.Single();
        Assert.Equal("https://firestore.googleapis.com/v1/projects/proj/databases/(default)/documents:commit", req.RequestUri!.ToString());
        Assert.Equal("Bearer TOKEN", req.Headers.Authorization!.ToString());
        using var json = JsonDocument.Parse(body!);
        var writes = json.RootElement.GetProperty("writes");
        Assert.Equal("projects/proj/databases/(default)/documents/users/U/devices/D",
            writes[0].GetProperty("update").GetProperty("name").GetString());
        Assert.False(writes[0].GetProperty("currentDocument").GetProperty("exists").GetBoolean());
        var transform = writes[1].GetProperty("transform").GetProperty("fieldTransforms")[0];
        Assert.Equal("deviceIds", transform.GetProperty("fieldPath").GetString());
        Assert.Equal("D", transform.GetProperty("appendMissingElements").GetProperty("values")[0]
            .GetProperty("stringValue").GetString());
    }

    [Fact]
    public async Task Get_returns_null_on_404_and_throws_permission_denied()
    {
        var handler = new FakeHandler((_, n) => n == 1
            ? FakeHandler.Json(HttpStatusCode.NotFound, """{"error":{"code":404,"status":"NOT_FOUND"}}""")
            : FakeHandler.Json(HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"Missing or insufficient permissions.","status":"PERMISSION_DENIED"}}"""));
        var db = new FirestoreService(new HttpClient(handler), new FirebaseOptions { ProjectId = "p" }, new Token());

        Assert.Null(await db.GetAsync<UserProfile>("users/U"));
        var ex = await Assert.ThrowsAsync<FirestoreException>(() => db.GetAsync<UserProfile>("users/X"));
        Assert.True(ex.IsPermissionDenied);
    }
}

public class LicenseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_status_and_expiry()
    {
        Assert.Equal(LicenseStatus.NotProvisioned, LicenseService.Evaluate(null, Now));
        Assert.Equal(LicenseStatus.Locked, LicenseService.Evaluate(new UserProfile { Status = UserStatus.Locked, ExpiresAt = Now.AddDays(1) }, Now));
        Assert.Equal(LicenseStatus.Expired, LicenseService.Evaluate(new UserProfile { ExpiresAt = Now }, Now));
        Assert.Equal(LicenseStatus.Ok, LicenseService.Evaluate(new UserProfile { ExpiresAt = Now.AddSeconds(1) }, Now));
    }

    [Theory]
    [InlineData("INVALID_LOGIN_CREDENTIALS", FirebaseAuthErrorKind.InvalidCredentials)]
    [InlineData("USER_DISABLED", FirebaseAuthErrorKind.UserDisabled)]
    [InlineData("TOO_MANY_ATTEMPTS_TRY_LATER : Access disabled", FirebaseAuthErrorKind.TooManyAttempts)]
    [InlineData("TOKEN_EXPIRED", FirebaseAuthErrorKind.SessionExpired)]
    public void Auth_error_mapping(string code, FirebaseAuthErrorKind kind) =>
        Assert.Equal(kind, FirebaseAuthService.MapError(code));
}
