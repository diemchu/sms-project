using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SolapiSender.Core;
using SolapiSender.Core.Excel;
using SolapiSender.Core.Messaging;
using SolapiSender.Core.Models;
using SolapiSender.UserApp.Localization;
using Wpf.Ui.Controls;

namespace SolapiSender.UserApp.Views;

/// <summary>Chọn sheet, cột SĐT, cột tên và các cột biến khi nhập Excel/CSV.</summary>
public partial class ImportDialog : FluentWindow
{
    private const int PreviewRows = 50;

    private static readonly string[] PhoneHints = ["전화", "휴대", "핸드폰", "연락처", "번호", "phone", "mobile", "tel", "sđt", "số"];
    private static readonly string[] NameHints = ["이름", "성명", "고객명", "name", "tên", "họ"];

    private readonly string _path;
    private readonly ExcelService _excel;
    private SheetData? _data;
    private bool _loading;

    public List<RawRecipient>? Result { get; private set; }

    public ImportDialog(string path, ExcelService excel)
    {
        _path = path;
        _excel = excel;
        InitializeComponent();
        FileNameText.Text = Path.GetFileName(path);

        _loading = true;
        foreach (var s in excel.GetSheetNames(path)) SheetCombo.Items.Add(s);
        SheetCombo.SelectedIndex = 0;
        SheetCombo.IsEnabled = SheetCombo.Items.Count > 1;
        _loading = false;
        LoadSheet();
    }

    private void OnSheetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) LoadSheet();
    }

    private void OnHeaderChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading && IsInitialized) LoadSheet();
    }

    private void OnMappingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) UpdateStats();
    }

    private void LoadSheet()
    {
        _loading = true;
        try
        {
            var sheet = ExcelService.IsCsv(_path) ? null : SheetCombo.SelectedItem as string;
            _data = _excel.Read(_path, sheet, HeaderCheck.IsChecked == true);

            PhoneCombo.Items.Clear();
            NameCombo.Items.Clear();
            NameCombo.Items.Add(Loc.T("Import_None"));
            VariablePanel.Children.Clear();
            foreach (var h in _data.Headers)
            {
                PhoneCombo.Items.Add(h);
                NameCombo.Items.Add(h);
                VariablePanel.Children.Add(new CheckBox { Content = h, Margin = new Thickness(0, 2, 0, 2) });
            }

            PhoneCombo.SelectedIndex = GuessPhoneColumn(_data);
            var nameIdx = FindByHint(_data.Headers, NameHints);
            NameCombo.SelectedIndex = nameIdx < 0 ? 0 : nameIdx + 1;

            var table = new DataTable();
            foreach (var h in _data.Headers)
            {
                // Ký tự . / [ ] ( ) làm hỏng đường dẫn binding của DataGrid tự sinh cột.
                var name = System.Text.RegularExpressions.Regex.Replace(h, @"[./\[\]()^]", "_");
                while (table.Columns.Contains(name)) name += "_";
                table.Columns.Add(name);
            }
            foreach (var row in _data.Rows.Take(PreviewRows)) table.Rows.Add(row.Cast<object>().ToArray());
            PreviewGrid.ItemsSource = table.DefaultView;
        }
        finally
        {
            _loading = false;
        }
        UpdateStats();
    }

    private static int FindByHint(IReadOnlyList<string> headers, string[] hints)
    {
        for (var i = 0; i < headers.Count; i++)
            if (hints.Any(h => headers[i].Contains(h, StringComparison.OrdinalIgnoreCase))) return i;
        return -1;
    }

    /// <summary>Ưu tiên tiêu đề gợi ý; nếu không có, chọn cột có nhiều số di động hợp lệ nhất.</summary>
    private static int GuessPhoneColumn(SheetData data)
    {
        var byHint = FindByHint(data.Headers, PhoneHints);
        if (byHint >= 0) return byHint;
        if (data.Headers.Count == 0) return -1;

        var sample = data.Rows.Take(100).ToList();
        return Enumerable.Range(0, data.Headers.Count)
            .OrderByDescending(c => sample.Count(r => PhoneNumberHelper.Normalize(r[c]) is not null))
            .First();
    }

    private ColumnMapping? BuildMapping()
    {
        if (PhoneCombo.SelectedIndex < 0 || _data is null) return null;
        var vars = new Dictionary<string, int>();
        for (var i = 0; i < VariablePanel.Children.Count; i++)
        {
            if (VariablePanel.Children[i] is CheckBox { IsChecked: true } cb && i != PhoneCombo.SelectedIndex)
                vars[(string)cb.Content] = i;
        }
        return new ColumnMapping
        {
            PhoneColumn = PhoneCombo.SelectedIndex,
            NameColumn = NameCombo.SelectedIndex > 0 ? NameCombo.SelectedIndex - 1 : null,
            VariableColumns = vars,
        };
    }

    private void UpdateStats()
    {
        var map = BuildMapping();
        if (map is null || _data is null)
        {
            StatsText.Text = "";
            OkButton.IsEnabled = false;
            return;
        }
        var summary = RecipientListBuilder.Summarize(RecipientListBuilder.Build(ExcelService.ToRawRecipients(_data, map)));
        StatsText.Text = Loc.F("Import_Stats", summary.Total, summary.Valid, summary.Invalid, summary.Duplicate);
        OkButton.IsEnabled = summary.Valid > 0;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var map = BuildMapping();
        if (map is null || _data is null) return;
        Result = ExcelService.ToRawRecipients(_data, map);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
