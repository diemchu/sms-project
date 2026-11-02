using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolapiSender.Core.Messaging;
using SolapiSender.Core.Models.Solapi;
using SolapiSender.Core.Solapi;
using SolapiSender.UserApp.Localization;
using SolapiSender.UserApp.Services;

namespace SolapiSender.UserApp.ViewModels.Pages;

public sealed record GroupRow(string GroupId, DateTimeOffset? Date, int Total, int Success, int Failed, string Status, decimal Cost)
{
    public string State => Status switch
    {
        "COMPLETE" when Failed == 0 => "ok",
        "COMPLETE" => "warn",
        "SCHEDULED" or "SENDING" or "PENDING" => "neutral",
        _ => "error",
    };

    public string StatusText => Loc.T("GroupStatus_" + Status) is var t && !t.StartsWith('[') ? t : Status;

    public static GroupRow From(GroupInfo g) => new(g.GroupId, g.ScheduledDate ?? g.DateCreated, g.Count.Total,
        g.Count.SentSuccess, g.Count.SentFailed, g.Status ?? "", g.Balance?.Sum ?? 0);
}

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly SolapiClient _solapi;
    private readonly SessionService _session;
    private DateTimeOffset _lastLoaded = DateTimeOffset.MinValue;

    public DashboardViewModel(SolapiClient solapi, SessionService session)
    {
        _solapi = solapi;
        _session = session;
    }

    [ObservableProperty] private decimal _balance;
    [ObservableProperty] private decimal _point;
    [ObservableProperty] private int _todayTotal;
    [ObservableProperty] private int _todayFailed;
    [ObservableProperty] private int _monthTotal;
    [ObservableProperty] private double _monthSuccessRate;
    [ObservableProperty] private bool _isLowBalance;
    [ObservableProperty] private string? _lowBalanceMessage;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _hasLoaded;
    [ObservableProperty] private string _greeting = "";

    public ObservableCollection<GroupRow> RecentGroups { get; } = [];

    public async Task OnShownAsync()
    {
        Greeting = Loc.F("Dashboard_Greeting", _session.Profile?.StoreName);
        if (DateTimeOffset.UtcNow - _lastLoaded > TimeSpan.FromMinutes(1))
            await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        ErrorMessage = null;
        try
        {
            var nowKst = AdPolicy.ToKoreaTime(DateTimeOffset.UtcNow);
            var todayStart = new DateTimeOffset(nowKst.Date, nowKst.Offset);
            var monthStart = new DateTimeOffset(new DateTime(nowKst.Year, nowKst.Month, 1), nowKst.Offset);

            var balanceTask = _solapi.GetBalanceAsync();
            var todayTask = _solapi.GetStatisticsAsync(todayStart, nowKst);
            var monthTask = _solapi.GetStatisticsAsync(monthStart, nowKst);
            var groupsTask = _solapi.GetGroupsAsync(new GroupListQuery { Limit = 6 });
            await Task.WhenAll(balanceTask, todayTask, monthTask, groupsTask);

            Balance = balanceTask.Result.Balance;
            Point = balanceTask.Result.Point;
            TodayTotal = todayTask.Result.Total.Total;
            TodayFailed = todayTask.Result.Failed.Total;
            MonthTotal = monthTask.Result.Total.Total;
            MonthSuccessRate = monthTask.Result.SuccessRate;

            var threshold = _session.CloudSettings.LowBalanceThreshold;
            IsLowBalance = Balance + Point < threshold;
            LowBalanceMessage = IsLowBalance ? Loc.F("Dashboard_LowBalance", threshold) : null;

            RecentGroups.Clear();
            foreach (var g in groupsTask.Result.GroupList.Values.OrderByDescending(g => g.DateCreated))
                RecentGroups.Add(GroupRow.From(g));

            _lastLoaded = DateTimeOffset.UtcNow;
            HasLoaded = true;
        }
        catch (SolapiException ex)
        {
            ErrorMessage = ErrorText.Describe(ex);
        }
    }

    [RelayCommand]
    private static void OpenCharge() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://console.solapi.com/cash/charge") { UseShellExecute = true });
}
