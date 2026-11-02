using System.Text.RegularExpressions;
using SolapiSender.Core.Models;

namespace SolapiSender.Core.Messaging;

public enum MessageType
{
    SMS,
    LMS,
    MMS,
}

public sealed class AdOptions
{
    /// <summary>Tên người gửi hiển thị sau "(광고)" — bắt buộc theo 정보통신망법 (thường là tên cửa hàng).</summary>
    public string? SenderName { get; init; }

    /// <summary>Số 080 từ chối miễn phí, vd "0801234567".</summary>
    public required string OptOutNumber { get; init; }
}

public static partial class MessageComposer
{
    public const int SmsMaxBytes = 90;
    public const int LmsMaxBytes = 2000;
    public const int SubjectMaxBytes = 40;
    public const int MmsImageMaxBytes = 200 * 1024;

    public const string AdPrefix = "(광고)";
    public const string NameVariable = "이름";

    [GeneratedRegex(@"#\{([^{}\r\n]{1,30})\}")]
    private static partial Regex VariableRegex();

    public static string NormalizeNewLines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    /// <summary>Danh sách tên biến xuất hiện trong mẫu, theo thứ tự, không trùng.</summary>
    public static IReadOnlyList<string> ExtractVariables(string? template)
    {
        if (string.IsNullOrEmpty(template)) return [];
        return VariableRegex().Matches(template).Select(m => m.Groups[1].Value.Trim()).Distinct().ToList();
    }

    /// <summary>Thay #{이름} bằng tên người nhận, #{biến} bằng giá trị cột tương ứng. Biến thiếu → chuỗi rỗng.</summary>
    public static string ApplyVariables(string template, Recipient recipient)
    {
        return VariableRegex().Replace(template, m =>
        {
            var key = m.Groups[1].Value.Trim();
            if (key == NameVariable) return recipient.Name ?? string.Empty;
            return recipient.Variables.TryGetValue(key, out var v) ? v : string.Empty;
        });
    }

    /// <summary>Thêm "(광고){tên}" đầu tin và "무료거부 080…" cuối tin (không thêm lặp nếu đã có).</summary>
    public static string ApplyAdFormat(string body, AdOptions ad)
    {
        body = NormalizeNewLines(body).Trim();
        if (body.StartsWith(AdPrefix, StringComparison.Ordinal))
            body = body[AdPrefix.Length..].TrimStart();

        var header = AdPrefix + (string.IsNullOrWhiteSpace(ad.SenderName) ? string.Empty : ad.SenderName.Trim());
        var optOut = $"무료거부 {PhoneNumberHelper.DigitsOnly(ad.OptOutNumber)}";
        if (body.EndsWith(optOut, StringComparison.Ordinal))
            body = body[..^optOut.Length].TrimEnd();

        return $"{header}\n{body}\n{optOut}";
    }

    /// <summary>Soạn nội dung cuối cùng cho một người nhận.</summary>
    public static string Compose(string template, Recipient recipient, AdOptions? ad)
    {
        var text = NormalizeNewLines(ApplyVariables(template, recipient));
        return ad is null ? text : ApplyAdFormat(text, ad);
    }

    public static MessageType DetectType(int bytes, bool hasImage) =>
        hasImage ? MessageType.MMS : bytes <= SmsMaxBytes ? MessageType.SMS : MessageType.LMS;

    public static bool IsValidMmsImage(string path, long sizeBytes, out string? error)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg"))
        {
            error = "format";
            return false;
        }
        if (sizeBytes > MmsImageMaxBytes)
        {
            error = "size";
            return false;
        }
        error = null;
        return true;
    }
}
