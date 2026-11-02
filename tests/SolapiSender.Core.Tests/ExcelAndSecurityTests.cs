using System.Text;
using SolapiSender.Core.Excel;
using SolapiSender.Core.Messaging;
using SolapiSender.Core.Models;
using SolapiSender.Core.Security;

namespace SolapiSender.Core.Tests;

public sealed class ExcelServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "solapisender-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ExcelService _excel = new();

    public ExcelServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Csv_parser_handles_quotes_and_newlines()
    {
        var rows = ExcelService.ParseCsv("phone,name\r\n010-1111-2222,\"Kim, \"\"A\"\"\"\n01033334444,\"line1\nline2\"");
        Assert.Equal(3, rows.Count);
        Assert.Equal("Kim, \"A\"", rows[1][1]);
        Assert.Equal("line1\nline2", rows[2][1]);
    }

    [Fact]
    public void Cp949_csv_is_decoded()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var path = Path.Combine(_dir, "kr.csv");
        File.WriteAllBytes(path, Encoding.GetEncoding(949).GetBytes("전화번호,이름\n01012345678,홍길동\n"));

        var data = _excel.Read(path);

        Assert.Equal(["전화번호", "이름"], data.Headers);
        Assert.Equal("홍길동", data.Rows[0][1]);
        Assert.Equal(2, data.RowNumbers[0]);
    }

    [Fact]
    public void Xlsx_export_then_import_keeps_phone_and_maps_columns()
    {
        var path = Path.Combine(_dir, "list.xlsx");
        _excel.Export(path, "수신자", ["전화번호", "이름", "쿠폰"],
        [
            ["01012345678", "홍길동", "A1"],
            ["010-9999-8888", "김영희", "B2"],
        ]);

        var data = _excel.Read(path);
        Assert.Equal(["전화번호", "이름", "쿠폰"], data.Headers);
        Assert.Equal("01012345678", data.Rows[0][0]);

        var raws = ExcelService.ToRawRecipients(data, new ColumnMapping
        {
            PhoneColumn = 0,
            NameColumn = 1,
            VariableColumns = new Dictionary<string, int> { ["쿠폰"] = 2 },
        });
        var list = RecipientListBuilder.Build(raws);
        Assert.All(list, r => Assert.Equal(RecipientStatus.Valid, r.Status));
        Assert.Equal("B2", list[1].Variables["쿠폰"]);
        Assert.Equal("김영희님 B2", MessageComposer.ApplyVariables("#{이름}님 #{쿠폰}", list[1]));
    }

    [Fact]
    public void Numeric_phone_cell_without_leading_zero_is_recovered()
    {
        var path = Path.Combine(_dir, "num.xlsx");
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.Worksheets.Add("S");
            ws.Cell(1, 1).Value = "phone";
            ws.Cell(2, 1).Value = 1012345678d;
            wb.SaveAs(path);
        }

        var data = _excel.Read(path);
        Assert.Equal("1012345678", data.Rows[0][0]);
        Assert.Equal("01012345678", PhoneNumberHelper.Normalize(data.Rows[0][0]));
    }

    [Fact]
    public void Dpapi_store_roundtrip_and_ciphertext_is_not_plain()
    {
        var store = new DpapiStore(_dir);
        store.Save("creds", new { ApiKey = "MYKEY", ApiSecret = "MYSECRET" });

        var raw = File.ReadAllBytes(Path.Combine(_dir, "creds.bin"));
        Assert.DoesNotContain("MYSECRET", Encoding.UTF8.GetString(raw));

        var back = store.Load<Dictionary<string, string>>("creds");
        Assert.Equal("MYSECRET", back!["ApiSecret"]);

        store.Delete("creds");
        Assert.Null(store.Load<Dictionary<string, string>>("creds"));
    }

    [Fact]
    public void Machine_id_is_stable_hex()
    {
        Assert.Matches("^[0-9a-f]{32}$", MachineId.Current);
        Assert.Equal(MachineId.Current, MachineId.Current);
    }
}
