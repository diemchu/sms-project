using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SolapiSender.AdminTool.Services;
using SolapiSender.AdminTool.Views;
using SolapiSender.Core.Models.Firebase;

namespace SolapiSender.AdminTool.ViewModels;

public sealed record FilterOption(string Key, string Label);

public sealed partial class UsersViewModel : ObservableObject
{
    private readonly IUserAdminService _users;
    private readonly UiService _ui;
    private readonly IServiceProvider _sp;
    private List<AdminUser> _all = [];

    public UsersViewModel(IUserAdminService users, UiService ui, IServiceProvider sp)
    {
        _users = users;
        _ui = ui;
        _sp = sp;
        _filter = Filters[0];
    }

    public IReadOnlyList<FilterOption> Filters { get; } =
    [
        new("all", "Tất cả"),
        new("active", "Đang hoạt động"),
        new("soon", "Sắp hết hạn (≤ 7 ngày)"),
        new("expired", "Đã hết hạn"),
        new("locked", "Đã khóa"),
    ];

    public ObservableCollection<AdminUser> Users { get; } = [];

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private FilterOption _filter;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _activeCount;
    [ObservableProperty] private int _soonCount;
    [ObservableProperty] private int _expiredCount;
    [ObservableProperty] private int _lockedCount;

    /// <summary>Trang Nhật ký đăng ký để mở log của một user.</summary>
    public event Action<AdminUser>? ShowLogsRequested;

    private bool _loaded;

    public async Task OnShownAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await RefreshAsync();
    }

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnFilterChanged(FilterOption value) => ApplyFilter();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        ErrorMessage = null;
        try
        {
            _all = (await _users.ListUsersAsync()).ToList();
            TotalCount = _all.Count;
            ActiveCount = _all.Count(u => u.StateKey == "ok");
            SoonCount = _all.Count(u => u.Status == UserStatus.Active && u.IsExpiringSoon);
            ExpiredCount = _all.Count(u => u.Status == UserStatus.Active && u.IsExpired);
            LockedCount = _all.Count(u => u.Status == UserStatus.Locked);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = UiService.Describe(ex);
        }
    }

    private void ApplyFilter()
    {
        var q = Search.Trim();
        IEnumerable<AdminUser> list = _all;
        if (q.Length > 0)
            list = list.Where(u => u.StoreName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                   || u.Email.Contains(q, StringComparison.OrdinalIgnoreCase)
                                   || u.Phone.Contains(q, StringComparison.Ordinal));
        list = Filter.Key switch
        {
            "active" => list.Where(u => u.StateKey == "ok"),
            "soon" => list.Where(u => u.Status == UserStatus.Active && u.IsExpiringSoon),
            "expired" => list.Where(u => u.Status == UserStatus.Active && u.IsExpired),
            "locked" => list.Where(u => u.Status == UserStatus.Locked),
            _ => list,
        };
        Users.Clear();
        foreach (var u in list) Users.Add(u);
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (OpenEditor(null)) await RefreshAsync();
    }

    [RelayCommand]
    private async Task EditAsync(AdminUser user)
    {
        if (OpenEditor(user)) await RefreshAsync();
    }

    private bool OpenEditor(AdminUser? user)
    {
        var w = _sp.GetRequiredService<UserEditWindow>();
        w.ViewModel.Load(user);
        w.Owner = UiService.ActiveWindow();
        w.ShowDialog();
        return w.ViewModel.Changed;
    }

    [RelayCommand]
    private async Task ToggleLockAsync(AdminUser user)
    {
        var lockIt = user.Status != UserStatus.Locked;
        if (lockIt && !await _ui.ConfirmAsync("Khóa tài khoản",
                $"Khóa \"{user.StoreName}\" ({user.Email})?\n\nTài khoản bị vô hiệu hóa trên Firebase Auth, mọi phiên đăng nhập bị thu hồi " +
                "và app sẽ tự đăng xuất trong vòng tối đa 15 phút.", "Khóa", danger: true))
            return;
        try
        {
            await _users.SetLockedAsync(user.Uid, lockIt);
            _ui.Success(lockIt ? $"Đã khóa {user.StoreName}." : $"Đã mở khóa {user.StoreName}.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _ui.Error(ex);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(AdminUser user)
    {
        if (!await _ui.ConfirmAsync("Xóa tài khoản",
                $"Xóa vĩnh viễn \"{user.StoreName}\" ({user.Email})?\n\nSẽ xóa tài khoản đăng nhập, thiết bị, mẫu tin, cài đặt và nhật ký. " +
                "Không thể hoàn tác. Nếu chỉ muốn tạm ngừng, hãy dùng Khóa.", "Xóa vĩnh viễn", danger: true))
            return;
        try
        {
            await _users.DeleteUserAsync(user.Uid);
            _ui.Success($"Đã xóa {user.StoreName}.");
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _ui.Error(ex);
        }
    }

    [RelayCommand]
    private void ShowLogs(AdminUser user) => ShowLogsRequested?.Invoke(user);
}
