using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SolapiSender.Core.Models.Firebase;

namespace SolapiSender.Core.Firebase;

public enum LicenseStatus
{
    Ok,
    NotProvisioned,
    Locked,
    Expired,
    DeviceLimitReached,
}

public sealed record LicenseCheckResult(LicenseStatus Status, UserProfile? Profile);

/// <summary>
/// Kiểm tra quyền dùng app sau đăng nhập: trạng thái, hạn dùng, giới hạn thiết bị.
/// Việc đăng ký thiết bị mới dùng batch commit (tạo device + deviceCount +1) để
/// Security Rules kiểm soát được số lượng phía server.
/// </summary>
public sealed class LicenseService
{
    private readonly FirestoreService _db;
    private readonly ILogger<LicenseService> _logger;

    public LicenseService(FirestoreService db, ILogger<LicenseService>? logger = null)
    {
        _db = db;
        _logger = logger ?? NullLogger<LicenseService>.Instance;
    }

    public static LicenseStatus Evaluate(UserProfile? profile, DateTimeOffset now) => profile switch
    {
        null => LicenseStatus.NotProvisioned,
        { Status: UserStatus.Locked } => LicenseStatus.Locked,
        _ when profile.IsExpired(now) => LicenseStatus.Expired,
        _ => LicenseStatus.Ok,
    };

    /// <summary>Chỉ kiểm tra trạng thái/hạn (dùng cho kiểm tra định kỳ).</summary>
    public async Task<LicenseCheckResult> CheckStatusAsync(string uid, CancellationToken ct = default)
    {
        var profile = await _db.GetAsync<UserProfile>($"users/{uid}", ct);
        return new LicenseCheckResult(Evaluate(profile, DateTimeOffset.UtcNow), profile);
    }

    /// <summary>Kiểm tra đầy đủ khi đăng nhập, kể cả đăng ký máy hiện tại.</summary>
    public async Task<LicenseCheckResult> CheckAndRegisterDeviceAsync(string uid, string machineId, string machineName,
        string appVersion, CancellationToken ct = default)
    {
        var (status, profile) = await CheckStatusAsync(uid, ct);
        if (status != LicenseStatus.Ok) return new LicenseCheckResult(status, profile);

        var devicePath = $"users/{uid}/devices/{machineId}";
        var now = DateTimeOffset.UtcNow;
        var existing = await _db.GetAsync<DeviceInfo>(devicePath, ct);

        if (existing is null && profile!.DeviceIds.Contains(machineId))
        {
            // Máy đã có trong danh sách nhưng thiếu document (dữ liệu cũ/sửa tay): vẫn cho dùng,
            // không tạo lại vì rules chỉ cho tạo khi đồng thời thêm machineId mới.
            _logger.LogWarning("Thiết bị {Machine} có trong deviceIds nhưng thiếu document", machineId);
            return new LicenseCheckResult(LicenseStatus.Ok, profile);
        }

        if (existing is not null)
        {
            await _db.UpdateAsync(devicePath, new Dictionary<string, object?>
            {
                ["lastSeenAt"] = now,
                ["appVersion"] = appVersion,
                ["machineName"] = machineName,
            }, ct);
            return new LicenseCheckResult(LicenseStatus.Ok, profile);
        }

        if (profile!.DeviceIds.Count >= profile.MaxDevices)
            return new LicenseCheckResult(LicenseStatus.DeviceLimitReached, profile);

        try
        {
            await _db.CommitAsync(
            [
                FirestoreWrite.Create(devicePath, new DeviceInfo
                {
                    MachineName = machineName,
                    AppVersion = appVersion,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                }),
                FirestoreWrite.ArrayUnion($"users/{uid}", "deviceIds", machineId),
            ], ct);
            _logger.LogInformation("Đã đăng ký thiết bị mới {Machine} cho {Uid}", machineName, uid);
        }
        catch (FirestoreException ex) when (ex.IsPermissionDenied || ex.Status is "FAILED_PRECONDITION" or "ALREADY_EXISTS")
        {
            // Rules từ chối: đã vượt giới hạn (vd. máy khác đăng ký cùng lúc).
            _logger.LogWarning(ex, "Đăng ký thiết bị bị từ chối cho {Uid}", uid);
            return new LicenseCheckResult(LicenseStatus.DeviceLimitReached, profile);
        }

        return new LicenseCheckResult(LicenseStatus.Ok, profile);
    }
}
