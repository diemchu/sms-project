using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SolapiSender.Core.Models.Solapi;
using SolapiSender.Core.Solapi;

namespace SolapiSender.Core.Messaging;

/// <summary>Một tin đã soạn xong cho một người nhận.</summary>
public sealed record OutgoingMessage(int Id, string To, string Text, MessageType Type, string? Subject = null);

public sealed class BulkSendRequest
{
    public required string From { get; init; }
    public required IReadOnlyList<OutgoingMessage> Messages { get; init; }
    public string? ImageId { get; init; }

    /// <summary>Null = gửi ngay.</summary>
    public DateTimeOffset? ScheduledAt { get; init; }
}

public enum RecipientSendState
{
    Accepted,
    Rejected,
    NotSent,
}

public sealed record RecipientSendResult(
    int Id,
    string To,
    RecipientSendState State,
    string? MessageId,
    string? StatusCode,
    string? StatusMessage);

public sealed record SendProgress(int CompletedBatches, int TotalBatches, int Processed, int Total);

public sealed class BulkSendResult
{
    public required IReadOnlyList<RecipientSendResult> Results { get; init; }
    public required IReadOnlyList<string> GroupIds { get; init; }

    /// <summary>Lỗi khiến việc gửi dừng giữa chừng (nếu có).</summary>
    public SolapiException? StopError { get; init; }

    public bool Cancelled { get; init; }

    public int AcceptedCount => Results.Count(r => r.State == RecipientSendState.Accepted);
    public int RejectedCount => Results.Count(r => r.State == RecipientSendState.Rejected);
    public int NotSentCount => Results.Count(r => r.State == RecipientSendState.NotSent);
}

/// <summary>Chia tin thành các lô ≤10.000 và gửi qua send-many/detail.</summary>
public sealed class BulkSender
{
    public const int MaxBatchSize = 10_000;
    private const string IdField = "rid";

    private readonly SolapiClient _client;
    private readonly ILogger<BulkSender> _logger;

    public BulkSender(SolapiClient client, ILogger<BulkSender>? logger = null)
    {
        _client = client;
        _logger = logger ?? NullLogger<BulkSender>.Instance;
    }

    public int BatchSize { get; init; } = MaxBatchSize;

    public async Task<BulkSendResult> SendAsync(BulkSendRequest request, IProgress<SendProgress>? progress = null,
        CancellationToken ct = default)
    {
        var batches = request.Messages.Chunk(Math.Clamp(BatchSize, 1, MaxBatchSize)).ToList();
        var results = new List<RecipientSendResult>(request.Messages.Count);
        var groupIds = new List<string>();
        var scheduled = request.ScheduledAt is { } at ? SolapiSigner.FormatDate(at) : null;
        var from = PhoneNumberHelper.DigitsOnly(request.From);
        SolapiException? stopError = null;
        var cancelled = false;

        progress?.Report(new SendProgress(0, batches.Count, 0, request.Messages.Count));

        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];

            if (stopError is not null || ct.IsCancellationRequested)
            {
                cancelled |= ct.IsCancellationRequested;
                results.AddRange(batch.Select(m => NotSent(m, stopError?.Message ?? "Cancelled")));
                continue;
            }

            var payload = new SendManyRequest
            {
                Messages = batch.Select(m => new SendMessageItem
                {
                    To = m.To,
                    From = from,
                    Text = m.Text,
                    Type = m.Type.ToString(),
                    Subject = m.Type == MessageType.SMS ? null : m.Subject,
                    ImageId = m.Type == MessageType.MMS ? request.ImageId : null,
                    CustomFields = new Dictionary<string, string> { [IdField] = m.Id.ToString() },
                }).ToList(),
                AllowDuplicates = false,
                ShowMessageList = true,
                ScheduledDate = scheduled,
                Agent = new SendAgent { SdkVersion = "solapisender/1.0", OsPlatform = Environment.OSVersion.VersionString },
            };

            try
            {
                var res = await _client.SendManyDetailAsync(payload, ct);
                if (res.GroupInfo?.GroupId is { Length: > 0 } gid) groupIds.Add(gid);
                results.AddRange(MapBatchResult(batch, res));
                _logger.LogInformation("Lô {Batch}/{Total}: group {GroupId}, {Count} tin, {Failed} bị từ chối",
                    i + 1, batches.Count, res.GroupInfo?.GroupId, batch.Length, res.FailedMessageList.Count);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Huỷ khi đang chờ phản hồi: không biết server đã nhận hay chưa.
                cancelled = true;
                results.AddRange(batch.Select(m => NotSent(m, "Cancelled (unknown)")));
            }
            catch (SolapiException ex)
            {
                _logger.LogError(ex, "Lô {Batch}/{Total} thất bại ({Kind})", i + 1, batches.Count, ex.Kind);
                results.AddRange(batch.Select(m => new RecipientSendResult(m.Id, m.To, RecipientSendState.Rejected,
                    null, ex.ErrorCode, ex.Message)));
                if (ex.IsFatalForBatch) stopError = ex;
            }

            progress?.Report(new SendProgress(i + 1, batches.Count, results.Count, request.Messages.Count));
        }

        return new BulkSendResult { Results = results, GroupIds = groupIds, StopError = stopError, Cancelled = cancelled };
    }

    internal static IEnumerable<RecipientSendResult> MapBatchResult(IReadOnlyList<OutgoingMessage> batch, SendManyResponse res)
    {
        var failed = new Dictionary<int, FailedMessage>();
        foreach (var f in res.FailedMessageList)
            if (TryGetId(f.CustomFields, out var id)) failed[id] = f;

        var accepted = new Dictionary<int, AcceptedMessage>();
        foreach (var a in res.MessageList ?? [])
            if (TryGetId(a.CustomFields, out var id)) accepted[id] = a;

        foreach (var m in batch)
        {
            if (failed.TryGetValue(m.Id, out var f))
                yield return new RecipientSendResult(m.Id, m.To, RecipientSendState.Rejected, f.MessageId, f.StatusCode, f.StatusMessage);
            else if (accepted.TryGetValue(m.Id, out var a))
                yield return new RecipientSendResult(m.Id, m.To, RecipientSendState.Accepted, a.MessageId, a.StatusCode, a.StatusMessage);
            else
                // Không có trong failedMessageList → server đã nhận.
                yield return new RecipientSendResult(m.Id, m.To, RecipientSendState.Accepted, null, MessageStatusCodes.Accepted, null);
        }
    }

    private static bool TryGetId(Dictionary<string, string>? fields, out int id)
    {
        id = 0;
        return fields is not null && fields.TryGetValue(IdField, out var s) && int.TryParse(s, out id);
    }

    private static RecipientSendResult NotSent(OutgoingMessage m, string reason) =>
        new(m.Id, m.To, RecipientSendState.NotSent, null, null, reason);
}
