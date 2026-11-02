using System.Text;
using System.Text.RegularExpressions;

namespace SolapiSender.Core;

/// <summary>Chuẩn hóa và kiểm tra số di động Hàn Quốc.</summary>
public static partial class PhoneNumberHelper
{
    // 010 luôn 11 số; các đầu số cũ 011/016/017/018/019 có thể 10 hoặc 11 số.
    [GeneratedRegex(@"^(010\d{8}|01[16789]\d{7,8})$")]
    private static partial Regex MobileRegex();

    /// <summary>
    /// Trả về số dạng chỉ chữ số (vd "01012345678") hoặc null nếu không phải số di động hợp lệ.
    /// Chấp nhận: "010-1234-5678", "010 1234 5678", "+82 10-1234-5678", "0082...",
    /// "1012345678" (Excel làm mất số 0 đầu), chữ số full-width.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (c is >= '0' and <= '9') sb.Append(c);
            else if (c is >= '０' and <= '９') sb.Append((char)('0' + (c - '０')));
        }

        var digits = sb.ToString();
        if (digits.StartsWith("0082", StringComparison.Ordinal)) digits = digits[4..];
        else if (digits.StartsWith("82", StringComparison.Ordinal) && digits.Length >= 11) digits = digits[2..];

        if (!digits.StartsWith('0') && digits.StartsWith('1')) digits = "0" + digits;

        return MobileRegex().IsMatch(digits) ? digits : null;
    }

    public static bool IsValidMobile(string? digits) => digits is not null && MobileRegex().IsMatch(digits);

    /// <summary>Định dạng hiển thị: 010-1234-5678 / 011-123-4567.</summary>
    public static string Format(string digits)
    {
        if (!IsValidMobile(digits)) return digits;
        return digits.Length == 11
            ? $"{digits[..3]}-{digits[3..7]}-{digits[7..]}"
            : $"{digits[..3]}-{digits[3..6]}-{digits[6..]}";
    }

    /// <summary>Số gửi (발신번호) có thể là số bàn, 1588..., 070...: chỉ bỏ ký tự không phải số.</summary>
    public static string DigitsOnly(string? raw) =>
        raw is null ? string.Empty : new string(raw.Where(char.IsAsciiDigit).ToArray());
}
