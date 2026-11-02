using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolapiSender.AdminTool.Services;
using SolapiSender.Core.Excel;
using SolapiSender.Core.Security;

namespace SolapiSender.AdminTool.ViewModels;

public sealed record UserOption(string? Uid, string Label);

public sealed partial class LogsViewModel : ObservableObject
{
    private readonly IUserAdminService _users;
    private readonly ExcelService _excel;
    private readonly UiService _ui;
    private List<AdminLog> _all = [];
    private bool _loaded;

    public LogsViewModel(IUserAdminService users, ExcelService excel, UiService ui)
    {
        _users = users;
        _excel = excel;
        _ui = ui;
        _selectedUser = UserOptions[0];
        _typeFilter = TypeOptions[0];
    }

    public ObservableCollection<UserOption> UserOptions { get; } = [new(null, "Tất cả người dùng")];

    public IReadOnlyList<FilterOption> TypeOptions { get; } =
    [
        new("", "Tất cả loại"),
        new("login", "Đăng nhập"),
        new("logout", "Đăng xuất"),
        new("send", "Gửi tin"),
        new("schedule", "Hẹn giờ"),
        new("connect_api", "Kết nối API"),
        new("error", "Lỗi"),
    ];

    public IReadOnlyList<int> Limits { get; } = [100, 300, 1000];

    [ObservableProperty] private UserOption _selectedUser;
    [ObservableProperty] private FilterOption _typeFilter;
    [ObservableProperty] private DateTime _fromDate = DateTime.Today.AddDays(-7);
    [ObservableProperty] private DateTime _toDate = DateTime.Today;
    [ObservableProperty] private int _limit = 300;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _countText = "";

    public ObservableCollection<AdminLog> Logs { get; } = [];

    public async Task OnShownAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await LoadUserOptionsAsync();
        await SearchAsync();
    }

    /// <summary>Mở nhật ký của một người dùng (từ trang Người dùng).</summary>
    public async Task ShowForAsync(AdminUser user)
    {
        _loaded = true;
        await LoadUserOptionsAsync();
        SelectedUser = UserOptions.FirstOrDefault(o => o.Uid == user.Uid) ?? UserOptions[0];
        await SearchAsync();
    }

    private async Task LoadUserOptionsAsync()
    {
        try
        {
            var users = await _users.ListUsersAsync();
            var current = SelectedUser?.Uid;
            UserOptions.Clear();
            UserOptions.Add(new UserOption(null, "Tất cả người dùng"));
            foreach (var u in users) UserOptions.Add(new UserOption(u.Uid, $"{u.StoreName} · {u.Email}"));
            SelectedUser = UserOptions.FirstOrDefault(o => o.Uid == current) ?? UserOptions[0];
        }
        catch (Exception ex)
        {
            ErrorMessage = UiService.Describe(ex);
        }
    }

    partial void OnTypeFilterChanged(FilterOption value) => ApplyType();

    [RelayCommand]
    private async Task SearchAsync()
    {
        ErrorMessage = null;
        if (ToDate < FromDate)
        {
            ErrorMessage = "Ngày kết thúc sớm hơn ngày bắt đầu.";
            return;
        }
        try
        {
            var from = new DateTimeOffset(FromDate.Date);
            var to = new DateTimeOffset(ToDate.Date.AddDays(1).AddTicks(-1));
            _all = (await _users.ListLogsAsync(SelectedUser?.Uid, from, to, Limit)).ToList();
            ApplyType();
        }
        catch (Exception ex)
        {
            ErrorMessage = UiService.Describe(ex);
        }
    }

    private void ApplyType()
    {
        Logs.Clear();
        foreach (var l in _all.Where(l => TypeFilter.Key.Length == 0 || l.Type == TypeFilter.Key)) Logs.Add(l);
        CountText = _all.Count >= Limit
            ? $"{Logs.Count} dòng (đã đạt giới hạn {Limit}, thu hẹp khoảng ngày để xem thêm)"
            : $"{Logs.Count} dòng";
    }

    [RelayCommand]
    private void Export()
    {
        if (Logs.Count == 0) return;
        var path = UiService.PickSaveFile("File Excel|*.xlsx", $"admin-logs-{FromDate:yyyyMMdd}-{ToDate:yyyyMMdd}.xlsx");
        if (path is null) return;
        _excel.Export(path, "Nhật ký", ["Thời gian", "Cửa hàng", "UID", "Loại", "Nội dung", "Máy", "Chi tiết"],
            Logs.Select(l => (IReadOnlyList<object?>)[l.CreatedAt, l.UserLabel, l.Uid, l.Type, l.Message, l.MachineId, l.MetaText]));
        _ui.Success("Đã xuất file Excel.");
    }
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IUserAdminService _users;
    private readonly IAdminAuthService _auth;
    private readonly AdminFlow _flow;
    private readonly UiService _ui;

    public SettingsViewModel(IUserAdminService users, IAdminAuthService auth, AdminFlow flow, UiService ui)
    {
        _users = users;
        _auth = auth;
        _flow = flow;
        _ui = ui;
    }

    public string ProjectId => _users.Account.ProjectId;
    public string ClientEmail => _users.Account.ClientEmail;
    public string AdminName => _auth.CurrentAdmin ?? "";

    [RelayCommand]
    private void ChangePassword() => _flow.ChangePasswordOptional();

    [RelayCommand]
    private async Task SwitchServiceAccountAsync()
    {
        if (await _ui.ConfirmAsync("Đổi service account",
                "Xóa service account đã lưu trên máy này và chọn file khác?", "Đổi", danger: true))
            _flow.SwitchServiceAccount();
    }

    [RelayCommand]
    private void Logout() => _flow.Logout();

    [RelayCommand]
    private static void OpenLogs() =>
        Process.Start(new ProcessStartInfo(System.IO.Path.Combine(AppPaths.Root, "admin-logs")) { UseShellExecute = true });
}
