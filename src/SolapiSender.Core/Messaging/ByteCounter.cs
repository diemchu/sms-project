using System.Text;

namespace SolapiSender.Core.Messaging;

/// <param name="Bytes">Số byte theo EUC-KR (ký tự không mã hóa được tính 2 byte).</param>
/// <param name="UnsupportedChars">Các ký tự không biểu diễn được bằng EUC-KR (emoji…), có thể hiển thị lỗi trên máy nhận.</param>
public sealed record ByteCountResult(int Bytes, IReadOnlyList<string> UnsupportedChars);

/// <summary>Đếm byte tin nhắn theo chuẩn EUC-KR mà nhà mạng Hàn dùng để tính SMS/LMS.</summary>
public static class ByteCounter
{
    private static readonly Encoding EucKr;

    static ByteCounter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        EucKr = Encoding.GetEncoding(51949, new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback);
    }

    public static int CountBytes(string? text) => Count(text).Bytes;

    public static ByteCountResult Count(string? text)
    {
        if (string.IsNullOrEmpty(text)) return new ByteCountResult(0, []);

        text = MessageComposer.NormalizeNewLines(text);
        var bytes = 0;
        List<string>? unsupported = null;

        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value < 0x80)
            {
                bytes += 1;
                continue;
            }

            var s = rune.ToString();
            var encoded = EucKr.GetBytes(s);
            if (EucKr.GetString(encoded) != s)
            {
                bytes += 2;
                unsupported ??= [];
                if (!unsupported.Contains(s)) unsupported.Add(s);
            }
            else
            {
                bytes += encoded.Length;
            }
        }

        return new ByteCountResult(bytes, (IReadOnlyList<string>?)unsupported ?? []);
    }
}
