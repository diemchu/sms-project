using System.Text.Json;
using FirebaseAdmin.Auth;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;
using SolapiSender.Core.Models.Firebase;

namespace SolapiSender.AdminTool.Services;

/// <summary>
/// Quản lý chủ quán bằng Admin SDK (bỏ qua Security Rules).
/// Tên/kiểu field phải khớp <see cref="UserProfile"/> mà UserApp đọc qua FirestoreMapper:
/// camelCase, status "active"/"locked", thời gian là Timestamp, deviceIds là mảng chuỗi.
/// </summary>
public sealed class FirebaseUserAdminService : IUserAdminService
{
    private static readonly string[] SubCollections = ["devices", "templates", "settings", "logs"];

    private readonly FirebaseAdminContext _ctx;
    private readonly ILogger<FirebaseUserAdminService> _logger;

    public FirebaseUserAdminService(FirebaseAdminContext ctx, ILogger<FirebaseUserAdminService> logger)
    {
        _ctx = ctx;
        _logger = logger;
    }

    public ServiceAccountInfo Account => _ctx.Info;

    private FirebaseAuth Auth => _ctx.Auth;
    private CollectionReference Users => _ctx.Db.Collection("users");

    // ------------------------------------------------------------------ list

    public async Task<IReadOnlyList<AdminUser>> ListUsersAsync(CancellationToken ct = default)
    {
        var docs = await Users.GetSnapshotAsync(ct);
        var result = docs.Documents.ToDictionary(d => d.Id, FromSnapshot);

        // Ghép thông tin đăng nhập; phát hiện user Auth thiếu document.
        await foreach (var u in Auth.ListUsersAsync(null).WithCancellation(ct))
        {
            if (result.TryGetValue(u.Uid, out var user))
            {
                user.LastSignInAt = u.UserMetaData?.LastSignInTimestamp is { } t ? new DateTimeOffset(t, TimeSpan.Zero) : null;
            }
            else
            {
                result[u.Uid] = new AdminUser
                {
                    Uid = u.Uid,
                    Email = u.Email ?? "",
                    StoreName = u.DisplayName ?? "",
                    Status = u.Disabled ? UserStatus.Locked : UserStatus.Active,
                    IsOrphan = true,
                };
            }
        }

        return result.Values.OrderBy(u => u.StoreName, StringComparer.CurrentCulture).ToList();
    }

    private static AdminUser FromSnapshot(DocumentSnapshot d)
    {
        var data = d.ToDictionary();
        string Str(string k) => data.TryGetValue(k, out var v) && v is string s ? s : "";
        DateTimeOffset? Time(string k) => data.TryGetValue(k, out var v) && v is Timestamp t ? t.ToDateTimeOffset() : null;

        return new AdminUser
        {
            Uid = d.Id,
            Email = Str("email"),
            StoreName = Str("storeName"),
            Phone = Str("phone"),
            Status = Str("status") == "locked" ? UserStatus.Locked : UserStatus.Active,
            ExpiresAt = Time("expiresAt") ?? DateTimeOffset.MinValue,
            MaxDevices = data.TryGetValue("maxDevices", out var m) ? Convert.ToInt32(m) : 1,
            DeviceIds = data.TryGetValue("deviceIds", out var ids) && ids is IEnumerable<object> list
                ? list.OfType<string>().ToList() : [],
            CreatedAt = Time("createdAt"),
        };
    }

    // ------------------------------------------------------------------ create / update

    public async Task<AdminUser> CreateUserAsync(UserEditModel m, CancellationToken ct = default)
    {
        Validate(m, isNew: true);
        UserRecord record;
        try
        {
            record = await Auth.CreateUserAsync(new UserRecordArgs
            {
                Email = m.Email.Trim(),
                Password = m.Password,
                DisplayName = m.StoreName.Trim(),
                Disabled = m.Status == UserStatus.Locked,
            }, ct);
        }
        catch (FirebaseAuthException ex)
        {
            throw Translate(ex);
        }

        var data = ProfileFields(m);
        data["deviceIds"] = new List<string>();
        data["createdAt"] = FieldValue.ServerTimestamp;
        try
        {
            await Users.Document(record.Uid).CreateAsync(data, ct);
        }
        catch (Exception ex)
        {
            // Không để lại tài khoản Auth thiếu hồ sơ.
            _logger.LogError(ex, "Ghi Firestore thất bại, hoàn tác user Auth {Uid}", record.Uid);
            await Auth.DeleteUserAsync(record.Uid, CancellationToken.None);
            throw new AdminException("Không ghi được hồ sơ người dùng lên Firestore: " + ex.Message, ex);
        }

        _logger.LogInformation("Tạo user {Uid} ({Email})", record.Uid, m.Email);
        return new AdminUser
        {
            Uid = record.Uid, Email = m.Email.Trim(), StoreName = m.StoreName.Trim(), Phone = m.Phone.Trim(),
            Status = m.Status, ExpiresAt = m.ExpiresAt, MaxDevices = m.MaxDevices, CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public async Task UpdateUserAsync(string uid, UserEditModel m, CancellationToken ct = default)
    {
        Validate(m, isNew: false);
        var args = new UserRecordArgs
        {
            Uid = uid,
            Email = m.Email.Trim(),
            DisplayName = m.StoreName.Trim(),
            Disabled = m.Status == UserStatus.Locked,
        };
        if (!string.IsNullOrEmpty(m.Password)) args.Password = m.Password;

        try
        {
            await Auth.UpdateUserAsync(args, ct);
            if (m.Status == UserStatus.Locked || !string.IsNullOrEmpty(m.Password))
                await Auth.RevokeRefreshTokensAsync(uid, ct);
        }
        catch (FirebaseAuthException ex)
        {
            throw Translate(ex);
        }

        // SetAsync + MergeFields: tạo lại hồ sơ nếu bị thiếu (user "mồ côi"), không đụng deviceIds/createdAt.
        var data = ProfileFields(m);
        var doc = Users.Document(uid);
        var snap = await doc.GetSnapshotAsync(ct);
        if (!snap.Exists)
        {
            data["deviceIds"] = new List<string>();
            data["createdAt"] = FieldValue.ServerTimestamp;
            await doc.SetAsync(data, cancellationToken: ct);
        }
        else
        {
            await doc.UpdateAsync(data, cancellationToken: ct);
        }
        _logger.LogInformation("Cập nhật user {Uid}", uid);
    }

    private static Dictionary<string, object> ProfileFields(UserEditModel m) => new()
    {
        ["email"] = m.Email.Trim(),
        ["storeName"] = m.StoreName.Trim(),
        ["phone"] = m.Phone.Trim(),
        ["status"] = m.Status == UserStatus.Locked ? "locked" : "active",
        ["expiresAt"] = Timestamp.FromDateTimeOffset(m.ExpiresAt),
        ["maxDevices"] = m.MaxDevices,
        ["updatedAt"] = FieldValue.ServerTimestamp,
    };

    private static void Validate(UserEditModel m, bool isNew)
    {
        if (!m.Email.Contains('@') || m.Email.Trim().Length < 5) throw new AdminException("Email không hợp lệ.");
        if (isNew && (m.Password is null || m.Password.Length < 6)) throw new AdminException("Mật khẩu phải có ít nhất 6 ký tự.");
        if (!isNew && !string.IsNullOrEmpty(m.Password) && m.Password.Length < 6) throw new AdminException("Mật khẩu phải có ít nhất 6 ký tự.");
        if (string.IsNullOrWhiteSpace(m.StoreName)) throw new AdminException("Vui lòng nhập tên cửa hàng.");
        if (m.MaxDevices is < 1 or > 20) throw new AdminException("Số thiết bị tối đa phải từ 1 đến 20.");
    }

    // ------------------------------------------------------------------ lock / delete

    public async Task SetLockedAsync(string uid, bool locked, CancellationToken ct = default)
    {
        try
        {
            await Auth.UpdateUserAsync(new UserRecordArgs { Uid = uid, Disabled = locked }, ct);
            if (locked) await Auth.RevokeRefreshTokensAsync(uid, ct);
        }
        catch (FirebaseAuthException ex)
        {
            throw Translate(ex);
        }

        await Users.Document(uid).SetAsync(new Dictionary<string, object>
        {
            ["status"] = locked ? "locked" : "active",
            ["updatedAt"] = FieldValue.ServerTimestamp,
        }, SetOptions.MergeAll, ct);
        _logger.LogInformation("{Action} user {Uid}", locked ? "Khóa" : "Mở khóa", uid);
    }

    public async Task DeleteUserAsync(string uid, CancellationToken ct = default)
    {
        var doc = Users.Document(uid);
        foreach (var name in SubCollections) await DeleteCollectionAsync(doc.Collection(name), ct);
        await doc.DeleteAsync(cancellationToken: ct);
        try
        {
            await Auth.DeleteUserAsync(uid, ct);
        }
        catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.UserNotFound)
        {
        }
        _logger.LogWarning("Đã xóa user {Uid}", uid);
    }

    private static async Task DeleteCollectionAsync(CollectionReference col, CancellationToken ct)
    {
        while (true)
        {
            var page = await col.Limit(400).GetSnapshotAsync(ct);
            if (page.Count == 0) return;
            var batch = col.Database.StartBatch();
            foreach (var d in page.Documents) batch.Delete(d.Reference);
            await batch.CommitAsync(ct);
        }
    }

    // ------------------------------------------------------------------ devices

    public async Task<IReadOnlyList<AdminDevice>> ListDevicesAsync(string uid, CancellationToken ct = default)
    {
        var docs = await Users.Document(uid).Collection("devices").GetSnapshotAsync(ct);
        return docs.Documents.Select(d =>
        {
            var x = d.ToDictionary();
            DateTimeOffset? T(string k) => x.TryGetValue(k, out var v) && v is Timestamp t ? t.ToDateTimeOffset() : null;
            return new AdminDevice(d.Id, x.GetValueOrDefault("machineName") as string ?? "",
                x.GetValueOrDefault("appVersion") as string ?? "", T("firstSeenAt"), T("lastSeenAt"));
        }).OrderByDescending(d => d.LastSeenAt).ToList();
    }

    public async Task RemoveDeviceAsync(string uid, string machineId, CancellationToken ct = default)
    {
        var user = Users.Document(uid);
        var batch = _ctx.Db.StartBatch();
        batch.Delete(user.Collection("devices").Document(machineId));
        batch.Update(user, new Dictionary<string, object> { ["deviceIds"] = FieldValue.ArrayRemove(machineId) });
        await batch.CommitAsync(ct);
        _logger.LogInformation("Gỡ thiết bị {Machine} khỏi user {Uid}", machineId, uid);
    }

    public async Task ResetDevicesAsync(string uid, CancellationToken ct = default)
    {
        var user = Users.Document(uid);
        await DeleteCollectionAsync(user.Collection("devices"), ct);
        await user.UpdateAsync(new Dictionary<string, object> { ["deviceIds"] = new List<string>() }, cancellationToken: ct);
        _logger.LogInformation("Gỡ toàn bộ thiết bị của user {Uid}", uid);
    }

    // ------------------------------------------------------------------ logs

    public async Task<IReadOnlyList<AdminLog>> ListLogsAsync(string? uid, DateTimeOffset from, DateTimeOffset to, int limit,
        CancellationToken ct = default)
    {
        Query q = uid is null ? _ctx.Db.CollectionGroup("logs") : Users.Document(uid).Collection("logs");
        q = q.WhereGreaterThanOrEqualTo("createdAt", Timestamp.FromDateTimeOffset(from))
             .WhereLessThanOrEqualTo("createdAt", Timestamp.FromDateTimeOffset(to))
             .OrderByDescending("createdAt")
             .Limit(limit);
        var snap = await q.GetSnapshotAsync(ct);

        var labels = new Dictionary<string, string>();
        var result = new List<AdminLog>(snap.Count);
        foreach (var d in snap.Documents)
        {
            var ownerUid = d.Reference.Parent.Parent?.Id ?? "";
            if (!labels.TryGetValue(ownerUid, out var label))
            {
                var p = await Users.Document(ownerUid).GetSnapshotAsync(ct);
                label = p.Exists && p.TryGetValue<string>("storeName", out var s) && s.Length > 0 ? s
                    : p.Exists && p.TryGetValue<string>("email", out var e) ? e : ownerUid;
                labels[ownerUid] = label;
            }

            var x = d.ToDictionary();
            result.Add(new AdminLog(
                ownerUid,
                label,
                x.GetValueOrDefault("type") as string ?? "",
                x.GetValueOrDefault("message") as string ?? "",
                x.GetValueOrDefault("machineId") as string ?? "",
                x.GetValueOrDefault("createdAt") is Timestamp t ? t.ToDateTimeOffset() : DateTimeOffset.MinValue,
                x.GetValueOrDefault("meta") is { } meta ? JsonSerializer.Serialize(Plain(meta)) : ""));
        }
        return result;
    }

    /// <summary>Chuyển giá trị Firestore (Timestamp…) sang dạng serialize được.</summary>
    private static object? Plain(object? v) => v switch
    {
        Timestamp t => t.ToDateTimeOffset().ToString("o"),
        IDictionary<string, object> d => d.ToDictionary(kv => kv.Key, kv => Plain(kv.Value)),
        IEnumerable<object> l when v is not string => l.Select(Plain).ToList(),
        _ => v,
    };

    // ------------------------------------------------------------------ errors

    private static AdminException Translate(FirebaseAuthException ex) => new(ex.AuthErrorCode switch
    {
        AuthErrorCode.EmailAlreadyExists => "Email này đã được dùng cho tài khoản khác.",
        AuthErrorCode.UserNotFound => "Không tìm thấy người dùng trên Firebase Auth.",
        AuthErrorCode.PhoneNumberAlreadyExists => "Số điện thoại đã được dùng.",
        _ => "Firebase Auth báo lỗi: " + ex.Message,
    }, ex);
}
