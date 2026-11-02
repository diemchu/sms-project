using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SolapiSender.Core.Models.Firebase;

namespace SolapiSender.Core.Firebase;

/// <summary>Ghi nhật ký hoạt động lên Firestore để Admin xem. Lỗi ghi log không làm hỏng luồng chính.</summary>
public sealed class ActivityLogService
{
    private readonly FirestoreService _db;
    private readonly ILogger<ActivityLogService> _logger;

    public ActivityLogService(FirestoreService db, ILogger<ActivityLogService>? logger = null)
    {
        _db = db;
        _logger = logger ?? NullLogger<ActivityLogService>.Instance;
    }

    public async Task LogAsync(string uid, string machineId, string type, string message,
        Dictionary<string, object?>? meta = null, CancellationToken ct = default)
    {
        try
        {
            await _db.CreateAsync($"users/{uid}/logs", new ActivityLog
            {
                Type = type,
                Message = message,
                Meta = meta ?? [],
                MachineId = machineId,
                CreatedAt = DateTimeOffset.UtcNow,
            }, ct: ct);
        }
        catch (Exception ex) when (ex is FirestoreException or FirebaseAuthException)
        {
            _logger.LogWarning(ex, "Không ghi được activity log {Type}", type);
        }
    }
}
