using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using SolapiSender.Core.Models;

namespace SolapiSender.Core.Excel;

/// <summary>Bảng dữ liệu đọc từ file: dòng tiêu đề + các dòng giá trị (đã chuyển thành chuỗi).</summary>
public sealed class SheetData
{
    public required IReadOnlyList<string> Headers { get; init; }
    public required IReadOnlyList<string[]> Rows { get; init; }

    /// <summary>Số dòng thực tế trong file của Rows[i] (để báo lỗi cho người dùng).</summary>
    public required IReadOnlyList<int> RowNumbers { get; init; }
}

/// <summary>Cách ghép cột → người nhận. Chỉ số cột bắt đầu từ 0.</summary>
public sealed class ColumnMapping
{
    public required int PhoneColumn { get; init; }
    public int? NameColumn { get; init; }

    /// <summary>Tên biến (dùng trong #{tên}) → chỉ số cột.</summary>
    public IReadOnlyDictionary<string, int> VariableColumns { get; init; } = new Dictionary<string, int>();
}

public sealed class ExcelService
{
    public const int MaxRows = 200_000;

    private static readonly Encoding Utf8Strict = new UTF8Encoding(false, throwOnInvalidBytes: true);

    static ExcelService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static bool IsCsv(string path) => Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> GetSheetNames(string path)
    {
        if (IsCsv(path)) return [Path.GetFileNameWithoutExtension(path)];
        using var wb = OpenWorkbook(path);
        return wb.Worksheets.Select(w => w.Name).ToList();
    }

    /// <param name="hasHeader">false → tiêu đề tự đặt "A", "B", "C"…</param>
    public SheetData Read(string path, string? sheetName = null, bool hasHeader = true)
    {
        var raw = IsCsv(path) ? ReadCsvRows(path) : ReadXlsxRows(path, sheetName);

        var width = raw.Count == 0 ? 0 : raw.Max(r => r.Cells.Length);
        string[] headers;
        IEnumerable<(int Row, string[] Cells)> body;

        if (hasHeader && raw.Count > 0)
        {
            headers = Enumerable.Range(0, width)
                .Select(i => i < raw[0].Cells.Length && !string.IsNullOrWhiteSpace(raw[0].Cells[i]) ? raw[0].Cells[i].Trim() : ColumnLetter(i))
                .ToArray();
            body = raw.Skip(1);
        }
        else
        {
            headers = Enumerable.Range(0, width).Select(ColumnLetter).ToArray();
            body = raw;
        }

        var rows = new List<string[]>();
        var numbers = new List<int>();
        foreach (var (row, cells) in body)
        {
            if (cells.All(string.IsNullOrWhiteSpace)) continue;
            var padded = new string[width];
            for (var i = 0; i < width; i++) padded[i] = i < cells.Length ? cells[i] : "";
            rows.Add(padded);
            numbers.Add(row);
            if (rows.Count >= MaxRows) break;
        }

        return new SheetData { Headers = headers, Rows = rows, RowNumbers = numbers };
    }

    public static List<RawRecipient> ToRawRecipients(SheetData data, ColumnMapping map)
    {
        var list = new List<RawRecipient>(data.Rows.Count);
        for (var i = 0; i < data.Rows.Count; i++)
        {
            var cells = data.Rows[i];
            string Cell(int col) => col >= 0 && col < cells.Length ? cells[col].Trim() : "";

            var vars = map.VariableColumns.ToDictionary(kv => kv.Key, kv => Cell(kv.Value));
            list.Add(new RawRecipient(
                Cell(map.PhoneColumn),
                map.NameColumn is { } n ? Cell(n) : null,
                vars,
                data.RowNumbers[i]));
        }
        return list;
    }

    /// <summary>Xuất bảng ra .xlsx: tiêu đề in đậm, cố định dòng đầu, tự giãn cột.</summary>
    public void Export(string path, string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(SafeSheetName(sheetName));

        for (var c = 0; c < headers.Count; c++) ws.Cell(1, c + 1).Value = headers[c];

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Count; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (row[c])
                {
                    case null: break;
                    case DateTimeOffset dto:
                        cell.Value = dto.LocalDateTime;
                        cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                        break;
                    case DateTime dt:
                        cell.Value = dt;
                        cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                        break;
                    case int or long or double or decimal:
                        cell.Value = Convert.ToDouble(row[c], CultureInfo.InvariantCulture);
                        break;
                    case bool b:
                        cell.Value = b;
                        break;
                    default:
                        // Giữ số điện thoại dạng text để không mất số 0 đầu.
                        cell.SetValue(Convert.ToString(row[c], CultureInfo.InvariantCulture) ?? "");
                        cell.Style.NumberFormat.Format = "@";
                        break;
                }
            }
            r++;
        }

        var header = ws.Range(1, 1, 1, Math.Max(1, headers.Count));
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2F7");
        ws.SheetView.FreezeRows(1);
        if (r > 2) ws.Range(1, 1, r - 1, headers.Count).SetAutoFilter();
        ws.Columns().AdjustToContents(1, Math.Min(r, 500), 8, 60);

        wb.SaveAs(path);
    }

    // ------------------------------------------------------------------ xlsx

    private static XLWorkbook OpenWorkbook(string path)
    {
        // FileShare.ReadWrite: đọc được cả khi file đang mở trong Excel.
        // Chép vào bộ nhớ rồi đóng file ngay (XLWorkbook không tự đóng stream được truyền vào).
        var memory = new MemoryStream();
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            file.CopyTo(memory);
        memory.Position = 0;
        return new XLWorkbook(memory);
    }

    private static List<(int Row, string[] Cells)> ReadXlsxRows(string path, string? sheetName)
    {
        using var wb = OpenWorkbook(path);
        var ws = sheetName is null ? wb.Worksheet(1) : wb.Worksheet(sheetName);
        var used = ws.RangeUsed();
        var result = new List<(int, string[])>();
        if (used is null) return result;

        var firstCol = used.FirstColumn().ColumnNumber();
        var lastCol = used.LastColumn().ColumnNumber();
        foreach (var row in used.Rows())
        {
            var cells = new string[lastCol - firstCol + 1];
            for (var c = firstCol; c <= lastCol; c++)
                cells[c - firstCol] = CellToString(row.Worksheet.Cell(row.RowNumber(), c));
            result.Add((row.RowNumber(), cells));
            if (result.Count > MaxRows) break;
        }
        return result;
    }

    internal static string CellToString(IXLCell cell)
    {
        var v = cell.Value;
        if (v.IsBlank) return "";
        if (v.IsNumber)
        {
            var n = v.GetNumber();
            // Số nguyên lớn (số điện thoại bị lưu dạng số) → không dùng định dạng khoa học.
            return n == Math.Floor(n) && Math.Abs(n) < 1e15
                ? ((long)n).ToString(CultureInfo.InvariantCulture)
                : n.ToString(CultureInfo.InvariantCulture);
        }
        if (v.IsDateTime) return v.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return cell.GetFormattedString();
    }

    // ------------------------------------------------------------------ csv

    private static List<(int Row, string[] Cells)> ReadCsvRows(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var text = DecodeText(bytes);
        var rows = ParseCsv(text);
        return rows.Select((cells, i) => (i + 1, cells)).ToList();
    }

    /// <summary>UTF-8 (có/không BOM) hoặc CP949 (Excel tiếng Hàn lưu CSV mặc định).</summary>
    internal static string DecodeText(byte[] bytes)
    {
        if (bytes is [0xEF, 0xBB, 0xBF, ..]) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes is [0xFF, 0xFE, ..]) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        try
        {
            return Utf8Strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(949).GetString(bytes);
        }
    }

    /// <summary>Parser CSV theo RFC 4180 (dấu ngoặc kép, xuống dòng trong ô). Tự nhận dấu phân cách , hoặc tab hoặc ;</summary>
    internal static List<string[]> ParseCsv(string text)
    {
        var delimiter = DetectDelimiter(text);
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            if (c == '"' && field.Length == 0) inQuotes = true;
            else if (c == delimiter) { row.Add(field.ToString()); field.Clear(); }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row.ToArray());
                row.Clear();
            }
            else field.Append(c);
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }
        return rows;
    }

    private static char DetectDelimiter(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        var firstLine = end < 0 ? text : text[..end];
        char[] candidates = [',', '\t', ';'];
        return candidates.OrderByDescending(c => firstLine.Count(x => x == c)).First();
    }

    private static string ColumnLetter(int index)
    {
        var s = "";
        index++;
        while (index > 0)
        {
            var m = (index - 1) % 26;
            s = (char)('A' + m) + s;
            index = (index - 1) / 26;
        }
        return s;
    }

    private static string SafeSheetName(string name)
    {
        var invalid = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "Sheet1" : clean[..Math.Min(31, clean.Length)];
    }
}
