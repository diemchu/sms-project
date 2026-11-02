using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolapiSender.AdminTool.Services;

namespace SolapiSender.AdminTool.ViewModels;

/// <summary>Chọn file service account JSON và kiểm tra kết nối.</summary>
public sealed partial class SetupViewModel : ObservableObject
{
    private readonly ServiceAccountStore _store;
    private readonly FirebaseAdminContext _ctx;
    private readonly AdminFlow _flow;
    private string? _json;

    public SetupViewModel(ServiceAccountStore store, FirebaseAdminContext ctx, AdminFlow flow)
    {
        _store = store;
        _ctx = ctx;
        _flow = flow;
    }

    [ObservableProperty] private string? _fileName;
    [ObservableProperty] private string? _projectId;
    [ObservableProperty] private string? _clientEmail;
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private bool _hasFile;

    [RelayCommand]
    private void PickFile()
    {
        var path = UiService.PickOpenFile("Service account JSON|*.json");
        if (path is null) return;
        ErrorMessage = null;
        try
        {
            var (json, info) = ServiceAccountStore.ReadAndValidate(path);
            _json = json;
            FileName = System.IO.Path.GetFileName(path);
            ProjectId = info.ProjectId;
            ClientEmail = info.ClientEmail;
            HasFile = true;
        }
        catch (AdminException ex)
        {
            ErrorMessage = ex.Message;
            HasFile = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasFile))]
    private async Task ConnectAsync()
    {
        ErrorMessage = null;
        try
        {
            _ctx.Initialize(_json!);
            // Gọi thử Firestore để chắc chắn service account có quyền.
            await _ctx.Db.Collection("admins").Limit(1).GetSnapshotAsync();
            _store.Save(_json!);
            _json = null;
            _flow.ShowLogin();
        }
        catch (Exception ex)
        {
            _ctx.Dispose();
            ErrorMessage = UiService.Describe(ex);
        }
    }

    [RelayCommand]
    private static void OpenConsole() =>
        Process.Start(new ProcessStartInfo("https://console.firebase.google.com/project/_/settings/serviceaccounts/adminsdk") { UseShellExecute = true });
}

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAdminAuthService _auth;
    private readonly IUserAdminService _users;
    private readonly AdminFlow _flow;

    public LoginViewModel(IAdminAuthService auth, IUserAdminService users, AdminFlow flow)
    {
        _auth = auth;
        _users = users;
        _flow = flow;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _username = "admin";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = "";

    [ObservableProperty] private string? _errorMessage;

    /// <summary>
    /// Đọc mật khẩu trực tiếp từ control lúc bấm nút: PasswordBox của WPF-UI phát PasswordChanged
    /// trước khi cập nhật Password nên giá trị nhận qua sự kiện có thể thiếu ký tự cuối.
    /// </summary>
    public Func<string>? ReadPassword { get; set; }

    public string ProjectText => App.PreviewMode ? "PREVIEW · dữ liệu giả lập" : "Firebase: " + _users.Account.ProjectId;

    private bool CanLogin() => Username.Trim().Length > 0 && Password.Length > 0;

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ErrorMessage = null;
        try
        {
            var r = await _auth.LoginAsync(Username, ReadPassword?.Invoke() ?? Password);
            switch (r.Status)
            {
                case AdminLoginStatus.Ok:
                case AdminLoginStatus.MustChangePassword:
                    _flow.AfterLogin(r.Status == AdminLoginStatus.MustChangePassword);
                    break;
                case AdminLoginStatus.LockedOut:
                    ErrorMessage = $"Sai mật khẩu quá nhiều lần. Thử lại sau {Math.Ceiling(r.RetryAfter?.TotalMinutes ?? 5)} phút.";
                    break;
                default:
                    ErrorMessage = "Tên đăng nhập hoặc mật khẩu không đúng.";
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = UiService.Describe(ex);
        }
    }

    [RelayCommand]
    private void SwitchServiceAccount() => _flow.SwitchServiceAccount();
}

public sealed partial class ChangePasswordViewModel : ObservableObject
{
    private readonly IAdminAuthService _auth;
    private readonly AdminFlow _flow;

    public ChangePasswordViewModel(IAdminAuthService auth, AdminFlow flow)
    {
        _auth = auth;
        _flow = flow;
    }

    public System.Windows.Window? Owner { get; set; }

    /// <summary>true = lần đăng nhập đầu, không được bỏ qua.</summary>
    [ObservableProperty] private bool _mandatory;
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _currentPassword = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _confirmPassword = "";
    [ObservableProperty] private string? _errorMessage;

    /// <summary>Đọc (hiện tại, mới, nhập lại) trực tiếp từ các PasswordBox lúc bấm Lưu.</summary>
    public Func<(string Current, string New, string Confirm)>? ReadPasswords { get; set; }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        if (ReadPasswords?.Invoke() is { } p)
            (CurrentPassword, NewPassword, ConfirmPassword) = p;
        if (NewPassword != ConfirmPassword)
        {
            ErrorMessage = "Mật khẩu nhập lại không khớp.";
            return;
        }
        try
        {
            await _auth.ChangePasswordAsync(Username, CurrentPassword, NewPassword);
            if (Mandatory) _flow.AfterLogin(mustChangePassword: false);
            else Owner?.Close();
        }
        catch (Exception ex)
        {
            ErrorMessage = UiService.Describe(ex);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (Mandatory) _flow.Logout();
        else Owner?.Close();
    }
}
