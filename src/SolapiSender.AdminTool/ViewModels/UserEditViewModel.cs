using System.Collections.ObjectModel;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolapiSender.AdminTool.Services;
using SolapiSender.Core.Models.Firebase;

namespace SolapiSender.AdminTool.ViewModels;

public sealed partial class UserEditViewModel : ObservableObject
{
    private readonly IUserAdminService _users;
    private readonly UiService _ui;
    private AdminUser? _original;

    public UserEditViewModel(IUserAdminService users, UiService ui)
    {
        _users = users;
        _ui = ui;
    }

    public System.Windows.Window? Owner { get; set; }

    /// <summary>Đã lưu thay đổi (để trang danh sách tải lại).</summary>
    public bool Changed { get; private set; }

    [ObservableProperty] private bool _isNew;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _storeName = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private DateTime? _expiresDate = DateTime.Today.AddMonths(1);
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private double _maxDevices = 1;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _uidText = "";

    public ObservableCollection<AdminDevice> Devices { get; } = [];

    public void Load(AdminUser? user)
    {
        _original = user;
        IsNew = user is null;
        Title = IsNew ? "Thêm người dùng" : "Sửa người dùng";
        if (user is null)
        {
            Password = GeneratePassword();
            return;
        }
        Email = user.Email;
        StoreName = user.StoreName;
        Phone = user.Phone;
        ExpiresDate = user.ExpiresAt > DateTimeOffset.MinValue ? user.ExpiresAt.ToLocalTime().Date : DateTime.Today.AddMonths(1);
        IsActive = user.Status == UserStatus.Active;
        MaxDevices = user.MaxDevices;
        UidText = "UID: " + user.Uid;
        _ = LoadDevicesAsync();
    }

    private async Task LoadDevicesAsync()
    {
        if (_original is null) return;
        try
        {
            Devices.Clear();
            foreach (var d in await _users.ListDevicesAsync(_original.Uid)) Devices.Add(d);
        }
        catch (Exception ex)
        {
            ErrorMessage = UiService.Describe(ex);
        }
    }

    [RelayCommand]
    private void Extend(string months)
    {
        var baseDate = ExpiresDate is { } d && d > DateTime.Today ? d : DateTime.Today;
        ExpiresDate = baseDate.AddMonths(int.Parse(months));
    }

    [RelayCommand]
    private void NewPassword() => Password = GeneratePassword();

    /// <summary>Mật khẩu ngẫu nhiên 10 ký tự, bỏ các ký tự dễ nhầm (0/O, 1/l/I).</summary>
    private static string GeneratePassword()
    {
        const string chars = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 10).Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        if (ExpiresDate is null)
        {
            ErrorMessage = "Vui lòng chọn ngày hết hạn.";
            return;
        }
        var model = new UserEditModel
        {
            Email = Email,
            Password = string.IsNullOrEmpty(Password) ? null : Password,
            StoreName = StoreName,
            Phone = Core.PhoneNumberHelper.DigitsOnly(Phone),
            // Hết hạn vào cuối ngày đã chọn theo giờ Hàn Quốc.
            ExpiresAt = new DateTimeOffset(ExpiresDate.Value.Date.AddDays(1).AddSeconds(-1), TimeSpan.FromHours(9)),
            Status = IsActive ? UserStatus.Active : UserStatus.Locked,
            MaxDevices = (int)Math.Round(MaxDevices),
        };
        try
        {
            if (_original is null)
            {
                await _users.CreateUserAsync(model);
                await _ui.AlertAsync("Đã tạo tài khoản",
                    $"Gửi thông tin đăng nhập cho chủ quán:\n\nEmail: {model.Email.Trim()}\nMật khẩu: {model.Password}\n\n" +
                    "Mật khẩu chỉ hiển thị lần này.");
            }
            else
            {
                await _users.UpdateUserAsync(_original.Uid, model);
                _ui.Success($"Đã lưu {model.StoreName.Trim()}.");
            }
            Changed = true;
            Owner?.Close();
        }
        catch (Exception ex)
        {
            ErrorMessage = UiService.Describe(ex);
        }
    }

    [RelayCommand]
    private async Task RemoveDeviceAsync(AdminDevice device)
    {
        if (_original is null) return;
        if (!await _ui.ConfirmAsync("Gỡ thiết bị", $"Gỡ \"{device.MachineName}\"? Máy này sẽ phải đăng nhập lại và chiếm một suất mới.", "Gỡ"))
            return;
        try
        {
            await _users.RemoveDeviceAsync(_original.Uid, device.MachineId);
            Devices.Remove(device);
            Changed = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiService.Describe(ex);
        }
    }

    [RelayCommand]
    private async Task ResetDevicesAsync()
    {
        if (_original is null || Devices.Count == 0) return;
        if (!await _ui.ConfirmAsync("Gỡ tất cả thiết bị", "Gỡ toàn bộ PC đã đăng ký của người dùng này?", "Gỡ tất cả", danger: true))
            return;
        try
        {
            await _users.ResetDevicesAsync(_original.Uid);
            Devices.Clear();
            Changed = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = UiService.Describe(ex);
        }
    }

    [RelayCommand]
    private void Cancel() => Owner?.Close();
}
