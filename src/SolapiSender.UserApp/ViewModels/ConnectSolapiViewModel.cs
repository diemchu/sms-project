using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolapiSender.Core.Firebase;
using SolapiSender.Core.Models.Firebase;
using SolapiSender.Core.Models.Solapi;
using SolapiSender.Core.Solapi;
using SolapiSender.UserApp.Localization;
using SolapiSender.UserApp.Services;

namespace SolapiSender.UserApp.ViewModels;

public sealed partial class ConnectSolapiViewModel : ObservableObject
{
    private readonly SolapiClient _solapi;
    private readonly CredentialStore _credentials;
    private readonly SessionService _session;
    private readonly ActivityLogService _log;
    private readonly AppFlow _flow;

    public ConnectSolapiViewModel(SolapiClient solapi, CredentialStore credentials, SessionService session,
        ActivityLogService log, AppFlow flow)
    {
        _solapi = solapi;
        _credentials = credentials;
        _session = session;
        _log = log;
        _flow = flow;
    }

    public System.Windows.Window? Owner { get; set; }

    /// <summary>true = đổi key từ Cài đặt (cửa sổ con); false = lần đầu sau đăng nhập.</summary>
    [ObservableProperty] private bool _isChange;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private string _apiKey = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    private string _apiSecret = "";

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _successMessage;

    public string StoreName => _session.Profile?.StoreName ?? "";

    /// <summary>Đọc API Secret trực tiếp từ PasswordBox lúc bấm (xem LoginViewModel.ReadPassword).</summary>
    public Func<string>? ReadSecret { get; set; }

    private bool CanVerify() => ApiKey.Trim().Length >= 8 && ApiSecret.Trim().Length >= 8;

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync()
    {
        ErrorMessage = null;
        SuccessMessage = null;
        var creds = new SolapiCredentials(ApiKey.Trim(), (ReadSecret?.Invoke() ?? ApiSecret).Trim());
        try
        {
            var balance = await _solapi.VerifyCredentialsAsync(creds);
            _credentials.Save(creds);
            ApiSecret = "";
            SuccessMessage = Loc.F("Connect_Success", balance.Balance, balance.Point);
            _ = _log.LogAsync(_session.Uid, _session.MachineId, ActivityLogTypes.ConnectApi, "API key verified");

            await Task.Delay(900);
            if (IsChange) Owner?.Close();
            else
            {
                _flow.ShowMain();
                Owner?.Close();
            }
        }
        catch (SolapiException ex)
        {
            ErrorMessage = ErrorText.Describe(ex);
        }
    }

    [RelayCommand]
    private static void OpenConsole() =>
        Process.Start(new ProcessStartInfo("https://console.solapi.com/credentials") { UseShellExecute = true });

    [RelayCommand]
    private async Task CancelAsync()
    {
        if (IsChange) Owner?.Close();
        else await _flow.LogoutAsync();
    }
}
