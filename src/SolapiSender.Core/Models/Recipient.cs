namespace SolapiSender.Core.Models;

public enum RecipientStatus
{
    Valid,
    Invalid,
    Duplicate,
}

/// <summary>Dòng dữ liệu thô trước khi chuẩn hóa (từ Excel hoặc dán).</summary>
public sealed record RawRecipient(
    string RawPhone,
    string? Name,
    IReadOnlyDictionary<string, string>? Variables,
    int RowNumber);

/// <summary>Người nhận sau khi chuẩn hóa số và kiểm tra trùng.</summary>
public sealed class Recipient
{
    public required int RowNumber { get; init; }
    public required string RawPhone { get; init; }

    /// <summary>Số đã chuẩn hóa (chỉ chữ số, dạng 010xxxxxxxx). Null nếu không hợp lệ.</summary>
    public string? Phone { get; init; }

    public string? Name { get; init; }
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();
    public RecipientStatus Status { get; init; }

    public string DisplayPhone => Phone is null ? RawPhone : PhoneNumberHelper.Format(Phone);
}
