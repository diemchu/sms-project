using System.Text.Json;
using System.Text.Json.Serialization;

namespace SolapiSender.Core.Models.Solapi;

// Schema đối chiếu với SDK chính thức solapi-nodejs (src/models/**).
// Chỉ khai báo các trường app dùng; trường thừa trong JSON bị bỏ qua.

public sealed record SolapiCredentials(string ApiKey, string ApiSecret);

/// <summary>GET /cash/v1/balance</summary>
public sealed class BalanceResponse
{
    public decimal Balance { get; set; }
    public decimal Point { get; set; }
    public decimal? Deposit { get; set; }
}

// ---------- POST /messages/v4/send-many/detail ----------

public sealed class SendManyRequest
{
    public required List<SendMessageItem> Messages { get; init; }
    public bool AllowDuplicates { get; init; }
    public bool ShowMessageList { get; init; } = true;

    /// <summary>ISO 8601. Null = gửi ngay.</summary>
    public string? ScheduledDate { get; init; }

    public SendAgent? Agent { get; init; }
}

public sealed class SendAgent
{
    public string? SdkVersion { get; init; }
    public string? OsPlatform { get; init; }
}

public sealed class SendMessageItem
{
    public required string To { get; init; }
    public required string From { get; init; }
    public required string Text { get; init; }

    /// <summary>"SMS" | "LMS" | "MMS"</summary>
    public string? Type { get; init; }

    public string? Subject { get; init; }
    public string? ImageId { get; init; }
    public Dictionary<string, string>? CustomFields { get; init; }
}

public sealed class SendManyResponse
{
    public GroupInfo? GroupInfo { get; set; }
    public List<FailedMessage> FailedMessageList { get; set; } = [];
    public List<AcceptedMessage>? MessageList { get; set; }
}

public sealed class FailedMessage
{
    public string? To { get; set; }
    public string? From { get; set; }
    public string? Type { get; set; }
    public string? MessageId { get; set; }
    public string? StatusCode { get; set; }
    public string? StatusMessage { get; set; }
    public Dictionary<string, string>? CustomFields { get; set; }
}

public sealed class AcceptedMessage
{
    public string? MessageId { get; set; }
    public string? StatusCode { get; set; }
    public string? StatusMessage { get; set; }
    public Dictionary<string, string>? CustomFields { get; set; }
}

public sealed class GroupCount
{
    public int Total { get; set; }
    public int SentTotal { get; set; }
    public int SentFailed { get; set; }
    public int SentSuccess { get; set; }
    public int SentPending { get; set; }
    public int SentReplacement { get; set; }
    public int Refund { get; set; }
    public int RegisteredFailed { get; set; }
    public int RegisteredSuccess { get; set; }
}

public sealed class CashSummary
{
    public decimal Requested { get; set; }
    public decimal Replacement { get; set; }
    public decimal Refund { get; set; }
    public decimal Sum { get; set; }
}

public sealed class GroupInfo
{
    public string GroupId { get; set; } = "";
    public string? Status { get; set; }
    public GroupCount Count { get; set; } = new();
    public CashSummary? Balance { get; set; }
    public CashSummary? Point { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public DateTimeOffset? ScheduledDate { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public DateTimeOffset? DateCompleted { get; set; }
}

// ---------- POST /storage/v1/files ----------

public sealed class FileUploadRequest
{
    /// <summary>Base64 của file.</summary>
    public required string File { get; init; }

    /// <summary>"MMS" cho ảnh tin nhắn.</summary>
    public required string Type { get; init; }

    public string? Name { get; init; }
}

public sealed class FileUploadResponse
{
    public string FileId { get; set; } = "";
    public string? Type { get; set; }
}

// ---------- GET /messages/v4/list ----------

public sealed class MessageListQuery
{
    public DateTimeOffset? StartDate { get; init; }
    public DateTimeOffset? EndDate { get; init; }
    public string? GroupId { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
    public string? Type { get; init; }
    public string? StatusCode { get; init; }
    public int Limit { get; init; } = 100;
    public string? StartKey { get; init; }
}

public sealed class MessageListResponse
{
    public string? StartKey { get; set; }
    public string? NextKey { get; set; }
    public int Limit { get; set; }

    /// <summary>Server trả về object có key là messageId (không phải mảng).</summary>
    public Dictionary<string, StoredMessage> MessageList { get; set; } = [];
}

public sealed class StoredMessage
{
    public string MessageId { get; set; } = "";
    public string? GroupId { get; set; }
    public string? To { get; set; }
    public string? From { get; set; }
    public string? Type { get; set; }
    public string? Text { get; set; }
    public string? Subject { get; set; }

    /// <summary>PENDING | SENDING | COMPLETE</summary>
    public string? Status { get; set; }

    public string? StatusCode { get; set; }
    public string? Reason { get; set; }
    public string? NetworkName { get; set; }
    public DateTimeOffset? DateCreated { get; set; }
    public DateTimeOffset? DateProcessed { get; set; }
    public DateTimeOffset? DateReported { get; set; }
    public DateTimeOffset? DateReceived { get; set; }
}

// ---------- GET /messages/v4/groups ----------

public sealed class GroupListQuery
{
    public DateTimeOffset? StartDate { get; init; }
    public DateTimeOffset? EndDate { get; init; }
    public int Limit { get; init; } = 50;
    public string? StartKey { get; init; }
}

public sealed class GroupListResponse
{
    public string? StartKey { get; set; }
    public string? NextKey { get; set; }
    public int Limit { get; set; }
    public Dictionary<string, GroupInfo> GroupList { get; set; } = [];
}

// ---------- GET /messages/v4/statistics ----------

public sealed class StatisticsCount
{
    public int Total { get; set; }
    public int Sms { get; set; }
    public int Lms { get; set; }
    public int Mms { get; set; }
}

public sealed class StatisticsResponse
{
    public decimal Balance { get; set; }
    public decimal Point { get; set; }
    public StatisticsCount Total { get; set; } = new();
    public StatisticsCount Successed { get; set; } = new();
    public StatisticsCount Failed { get; set; } = new();

    [JsonIgnore]
    public double SuccessRate =>
        Successed.Total + Failed.Total == 0 ? 0 : (double)Successed.Total / (Successed.Total + Failed.Total);
}

/// <summary>Body lỗi chuẩn của SOLAPI: { "errorCode": "...", "errorMessage": "..." }</summary>
internal sealed class SolapiErrorBody
{
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
