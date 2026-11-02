using System.Net;

namespace SolapiSender.Core.Solapi;

/// <summary>Nhóm lỗi để UI hiển thị thông báo thân thiện (chuỗi bản địa hóa nằm ở UserApp).</summary>
public enum SolapiErrorKind
{
    Unknown,
    Network,
    Timeout,
    InvalidCredentials,
    ClockSkew,
    Forbidden,
    InsufficientBalance,
    SenderNotRegistered,
    InvalidRequest,
    RateLimited,
    Server,
}

public sealed class SolapiException : Exception
{
    public SolapiErrorKind Kind { get; }
    public HttpStatusCode? StatusCode { get; }
    public string? ErrorCode { get; }
    public string? ServerMessage { get; }

    public SolapiException(SolapiErrorKind kind, string message, HttpStatusCode? statusCode = null,
        string? errorCode = null, string? serverMessage = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ServerMessage = serverMessage;
    }

    /// <summary>Lỗi khiến các lô tiếp theo cũng sẽ thất bại → nên dừng gửi.</summary>
    public bool IsFatalForBatch => Kind is SolapiErrorKind.InvalidCredentials or SolapiErrorKind.ClockSkew
        or SolapiErrorKind.Forbidden or SolapiErrorKind.InsufficientBalance or SolapiErrorKind.SenderNotRegistered;
}

public static class SolapiErrorClassifier
{
    public static SolapiErrorKind Classify(HttpStatusCode status, string? errorCode, string? errorMessage)
    {
        var code = errorCode ?? string.Empty;
        var text = code + " " + (errorMessage ?? string.Empty);

        if (Has(text, "Balance", "잔액", "NotEnoughCash", "InsufficientFunds")) return SolapiErrorKind.InsufficientBalance;
        if (Has(text, "SenderNumber", "SenderId", "발신번호")) return SolapiErrorKind.SenderNotRegistered;
        if (Has(code, "RequestTimeTooSkewed", "TimeTooSkewed", "InvalidDate")) return SolapiErrorKind.ClockSkew;
        if (Has(code, "InvalidApiKey", "SignatureDoesNotMatch", "InvalidSignature", "Unauthorized", "InvalidAuth")
            || status == HttpStatusCode.Unauthorized) return SolapiErrorKind.InvalidCredentials;
        if (status == HttpStatusCode.Forbidden) return SolapiErrorKind.Forbidden;
        if (status == HttpStatusCode.TooManyRequests || Has(code, "TooManyRequests", "RateLimit")) return SolapiErrorKind.RateLimited;
        if ((int)status >= 500) return SolapiErrorKind.Server;
        if ((int)status >= 400) return SolapiErrorKind.InvalidRequest;
        return SolapiErrorKind.Unknown;
    }

    private static bool Has(string source, params string[] needles) =>
        needles.Any(n => source.Contains(n, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Mã trạng thái từng tin (https://solapi.com/developers/api/msgstatus).</summary>
public static class MessageStatusCodes
{
    public const string Accepted = "2000";
    public const string DeliveredToCarrier = "3000";
    public const string Received = "4000";

    public static bool IsSuccess(string? code) => code == Received;
    public static bool IsPending(string? code) => code is Accepted or DeliveredToCarrier;
    public static bool IsFailure(string? code) => !string.IsNullOrEmpty(code) && !IsSuccess(code) && !IsPending(code);
}
