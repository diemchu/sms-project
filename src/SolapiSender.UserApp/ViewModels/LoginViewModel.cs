using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolapiSender.UserApp.Localization;
using SolapiSender.UserApp.Services;

namespace SolapiSender.UserApp.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly AppFlow _flow;
    private readonly LocalSettingsService _local;

    public LoginViewModel(AppFlow flow, LocalSettingsService local)
    {
        _flow = flow;
        _local = local;
        _email = local.Settings.LastEmail ?? "";
        _rememberMe = local.Settings.RememberLogin;
        _language = local.Settings.Language;
    }

    /// <summary>View gán để đóng cửa sổ đăng nhập sau khi chuyển màn.</summary>
    public System.Windows.Window? Owner { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _email;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = "";

    [ObservableProperty] private bool _rememberMe;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _language;

    public string Version => "v" + AppFlow.AppVersion;

    /// <summary>
    /// Đọc mật khẩu trực tiếp từ control lúc bấm nút: PasswordBox của WPF-UI phát PasswordChanged
    /// trước khi cập nhật Password nên giá trị nhận qua sự kiện có thể thiếu ký tự cuối.
    /// </summary>
    public Func<string>? ReadPassword { get; set; }

    partial void OnLanguageChanged(string value)
    {
        Loc.Instance.SetLanguage(value);
        _local.Settings.Language = value;
        _local.Save();
    }

    private bool CanLogin() => Email.Contains('@') && Password.Length > 0;

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ErrorMessage = null;
        var error = await _flow.SignInAsync(Email, ReadPassword?.Invoke() ?? Password, RememberMe);
        if (error is not null)
        {
            ErrorMessage = error;
            return;
        }
        Password = "";
        await _flow.AfterLoginAsync(Owner);
    }
}
