using SolapiSender.Core.Models;

namespace SolapiSender.Core.Messaging;

public sealed record RecipientListSummary(int Total, int Valid, int Invalid, int Duplicate);

public static class RecipientListBuilder
{
    private static readonly char[] Separators = [',', '\t', ';'];

    /// <summary>
    /// Phân tích danh sách dán: mỗi dòng "số" hoặc "số,tên" (chấp nhận cả tab, dấu chấm phẩy).
    /// Dòng trống bị bỏ qua.
    /// </summary>
    public static List<RawRecipient> ParsePasted(string? text)
    {
        var result = new List<RawRecipient>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;

            var parts = line.Split(Separators, 2, StringSplitOptions.TrimEntries);
            var name = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : null;
            result.Add(new RawRecipient(parts[0], name, null, i + 1));
        }
        return result;
    }

    /// <summary>Chuẩn hóa số, đánh dấu không hợp lệ và trùng (giữ dòng xuất hiện đầu tiên).</summary>
    public static List<Recipient> Build(IEnumerable<RawRecipient> raws)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<Recipient>();

        foreach (var raw in raws)
        {
            var phone = PhoneNumberHelper.Normalize(raw.RawPhone);
            var status = phone is null
                ? RecipientStatus.Invalid
                : seen.Add(phone) ? RecipientStatus.Valid : RecipientStatus.Duplicate;

            list.Add(new Recipient
            {
                RowNumber = raw.RowNumber,
                RawPhone = raw.RawPhone,
                Phone = phone,
                Name = string.IsNullOrWhiteSpace(raw.Name) ? null : raw.Name.Trim(),
                Variables = raw.Variables ?? new Dictionary<string, string>(),
                Status = status,
            });
        }
        return list;
    }

    public static RecipientListSummary Summarize(IReadOnlyCollection<Recipient> list) => new(
        list.Count,
        list.Count(r => r.Status == RecipientStatus.Valid),
        list.Count(r => r.Status == RecipientStatus.Invalid),
        list.Count(r => r.Status == RecipientStatus.Duplicate));
}
