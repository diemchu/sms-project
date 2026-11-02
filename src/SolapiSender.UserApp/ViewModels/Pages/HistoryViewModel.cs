using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolapiSender.Core.Excel;
using SolapiSender.Core.Messaging;
using SolapiSender.Core.Models.Solapi;
using SolapiSender.Core.Solapi;
using SolapiSender.UserApp.Localization;
using SolapiSender.UserApp.Services;

namespace SolapiSender.UserApp.ViewModels.Pages;

public sealed record MessageRow(StoredMessage M)
{
    public string To => SenderNumberService.Display(M.To ?? "");
    public string From => SenderNumberService.Display(M.From ?? "");
    public string State => MessageStatusCodes.IsSuccess(M.StatusCode) ? "ok" : MessageStatusCodes.IsPending(M.StatusCode) ? "warn" : "error";

    public string StatusText => State switch
    {
        "ok" => Loc.T("History_Status_Success"),
        "warn" => Loc.T("History_Status_Pending"),
        _ => Loc.T("History_Status_Failed"),
    };
}

public sealed record StatusFilterOption(string Key, string Label);

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly SolapiClient _solapi;
    private readonly ExcelService _excel;
    private readonly UiService _ui;
    private string? _messagesNextKey;
    private string? _groupsNextKey;
    private bool _initialized;

    public HistoryViewModel(SolapiClient solapi, ExcelService excel, UiService ui)
    {
        _solapi = solapi;
        _excel = excel;
        _ui = ui;
        StatusOptions =
        [
            new("all", Loc.T("History_Status_All")),
            new("success", Loc.T("History_Status_Success")),
            new("failed", Loc.T("History_Status_Failed")),
            new("pending", Loc.T("History_Status_Pending")),
        ];
        _statusFilter = StatusOptions[0];
    }

    public IReadOnlyList<StatusFilterOption> StatusOptions { get; }

    [ObservableProperty] private DateTime _startDate = DateTime.Today.AddDays(-7);
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private StatusFilterOption _statusFilter;
    [ObservableProperty] private string _phoneFilter = "";
    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasMoreMessages;
    [ObservableProperty] private bool _hasMoreGroups;

    public ObservableCollection<MessageRow> Messages { get; } = [];
    public ObservableCollection<GroupRow> Groups { get; } = [];

    public async Task OnShownAsync()
    {
        if (_initialized) return;
        _initialized = true;
        await SearchAsync();
    }

    private DateTimeOffset RangeStart => new(StartDate.Date, AdPolicy.KoreaTimeZone.BaseUtcOffset);
    private DateTimeOffset RangeEnd => new(EndDate.Date.AddDays(1).AddTicks(-1), AdPolicy.KoreaTimeZone.BaseUtcOffset);

    [RelayCommand]
    private async Task SearchAsync()
    {
        ErrorMessage = null;
        if (EndDate < StartDate)
        {
            ErrorMessage = Loc.T("History_Err_Range");
            return;
        }
        Messages.Clear();
        Groups.Clear();
        _messagesNextKey = null;
        _groupsNextKey = null;
        try
        {
            await Task.WhenAll(LoadMessagesPageAsync(), LoadGroupsPageAsync());
        }
        catch (SolapiException ex)
        {
            ErrorMessage = ErrorText.Describe(ex);
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        try
        {
            if (SelectedTab == 0 && HasMoreMessages) await LoadMessagesPageAsync();
            if (SelectedTab == 1 && HasMoreGroups) await LoadGroupsPageAsync();
        }
        catch (SolapiException ex)
        {
            ErrorMessage = ErrorText.Describe(ex);
        }
    }

    private async Task LoadMessagesPageAsync()
    {
        var res = await _solapi.GetMessagesAsync(new MessageListQuery
        {
            StartDate = RangeStart,
            EndDate = RangeEnd,
            To = string.IsNullOrWhiteSpace(PhoneFilter) ? null : Core.PhoneNumberHelper.DigitsOnly(PhoneFilter),
            // Chỉ "thành công" lọc được phía server bằng mã 4000; các trạng thái khác lọc phía client.
            StatusCode = StatusFilter.Key == "success" ? MessageStatusCodes.Received : null,
            Limit = 200,
            StartKey = _messagesNextKey,
        });
        foreach (var m in res.MessageList.Values.OrderByDescending(m => m.DateCreated))
        {
            var row = new MessageRow(m);
            if (Matches(row)) Messages.Add(row);
        }
        _messagesNextKey = res.NextKey;
        HasMoreMessages = !string.IsNullOrEmpty(res.NextKey);
    }

    private bool Matches(MessageRow row) => StatusFilter.Key switch
    {
        "failed" => row.State == "error",
        "pending" => row.State == "warn",
        _ => true,
    };

    private async Task LoadGroupsPageAsync()
    {
        var res = await _solapi.GetGroupsAsync(new GroupListQuery
        {
            StartDate = RangeStart,
            EndDate = RangeEnd,
            Limit = 50,
            StartKey = _groupsNextKey,
        });
        foreach (var g in res.GroupList.Values.OrderByDescending(g => g.DateCreated)) Groups.Add(GroupRow.From(g));
        _groupsNextKey = res.NextKey;
        HasMoreGroups = !string.IsNullOrEmpty(res.NextKey);
    }

    [RelayCommand]
    private void Export()
    {
        var path = UiService.PickSaveFile(Loc.T("File_XlsxFilter"), $"history-{StartDate:yyyyMMdd}-{EndDate:yyyyMMdd}.xlsx");
        if (path is null) return;

        if (SelectedTab == 0)
        {
            _excel.Export(path, Loc.T("Nav_History"),
                [Loc.T("Col_Date"), Loc.T("Col_Phone"), Loc.T("Col_From"), Loc.T("Col_Type"), Loc.T("Col_Status"), Loc.T("Col_Code"), Loc.T("Col_Reason"), Loc.T("Col_Message"), "messageId", "groupId"],
                Messages.Select(r => (IReadOnlyList<object?>)[r.M.DateCreated, r.To, r.From, r.M.Type, r.StatusText, r.M.StatusCode, r.M.Reason, r.M.Text, r.M.MessageId, r.M.GroupId]));
        }
        else
        {
            _excel.Export(path, Loc.T("History_Tab_Groups"),
                [Loc.T("Col_Date"), "groupId", Loc.T("Col_Status"), Loc.T("Col_Total"), Loc.T("Col_Success"), Loc.T("Col_Failed"), Loc.T("Col_Cost")],
                Groups.Select(g => (IReadOnlyList<object?>)[g.Date, g.GroupId, g.StatusText, g.Total, g.Success, g.Failed, g.Cost]));
        }
        _ui.Success(Loc.T("Common_Exported"));
    }
}
