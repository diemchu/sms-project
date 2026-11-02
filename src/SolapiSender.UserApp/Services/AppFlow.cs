using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SolapiSender.Core.Firebase;
using SolapiSender.Core.Models.Firebase;
using SolapiSender.Core.Security;
using SolapiSender.UserApp.Localization;
using SolapiSender.UserApp.Views;

namespace SolapiSender.UserApp.Services;

/// <summary>
/// Điều phối luồng màn hình: Đăng nhập → (Kết nối SOLAPI) → Main.
/// Mỗi phiên đăng nhập có một DI scope riêng để trang/ViewModel không giữ dữ liệu của tài khoản trước.
/// </summary>
public sealed class AppFlow
{
    private static readonly TimeSpan LicenseCheckInterval = TimeSpan.FromMinutes(15);

    private readonly IServiceProvider _root;
    private readonly FirebaseAuthService _auth;
    private readonly LicenseService _license;
    private readonly SessionService _session;
    private readonly CredentialStore _credentials;
    private readonly LocalSettingsService _local;
    private readonly ILogger<AppFlow> _logger;
    private readonly DispatcherTimer _licenseTimer;
    private IServiceScope? _scope;

    public AppFlow(IServiceProvider root, FirebaseAuthService auth, LicenseService license, SessionService session,
        CredentialStore credentials, LocalSettingsService local, ILogger<AppFlow> logger)
    {
        _root = root;
        _auth = auth;
        _license = license;
        _session = session;
        _credentials = credentials;
        _local = local;
        _logger = logger;
        _licenseTimer = new DispatcherTimer { Interval = LicenseCheckInterval };
        _licenseTimer.Tick += async (_, _) => await RecheckLicenseAsync();
    }

    public static string AppVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>Phạm vi DI của phiên hiện tại (trang, ViewModel, MainWindow).</summary>
    public IServiceProvider SessionServices => _scope?.ServiceProvider ?? _root;

    public async Task StartAsync()
    {
        if (App.PreviewMode)
        {
            Preview.PreviewData.StartSession(_session, _auth);
            await AfterLoginAsync(null);
            return;
        }

        var token = _local.Settings.RememberLogin ? _session.LoadRefreshToken() : null;
        if (token is not null)
        {
            try
            {
                var s = await _auth.RestoreAsync(token);
                var error = await CompleteLoginAsync(s, remember: true);
                if (error is null)
                {
                    await AfterLoginAsync(null);
                    return;
                }
                ShowLogin(error);
                return;
            }
            catch (Exception ex) when (ex is FirebaseAuthException or FirestoreException)
            {
                _logger.LogInformation(ex, "Không khôi phục được phiên đã lưu");
                if (ex is FirebaseAuthException { Kind: not FirebaseAuthErrorKind.Network }) _session.ClearRefreshToken();
            }
        }
        ShowLogin();
    }

    /// <summary>Đăng nhập email/mật khẩu. Trả về thông báo lỗi, hoặc null nếu thành công.</summary>
    public async Task<string?> SignInAsync(string email, string password, bool remember)
    {
        try
        {
            var s = await _auth.SignInAsync(email, password);
            return await CompleteLoginAsync(s, remember);
        }
        catch (Exception ex) when (ex is FirebaseAuthException or FirestoreException)
        {
            return ErrorText.Describe(ex);
        }
    }

    private async Task<string?> CompleteLoginAsync(FirebaseSession s, bool remember)
    {
        var result = await _license.CheckAndRegisterDeviceAsync(s.Uid, MachineId.Current, MachineId.MachineName, AppVersion);
        var error = DescribeLicense(result);
        if (error is not null)
        {
            _auth.SignOut();
            return error;
        }

        _session.Start(result.Profile!);
        if (remember) _session.SaveRefreshToken(_auth.Session!.RefreshToken);
        else _session.ClearRefreshToken();
        _local.Settings.RememberLogin = remember;
        _local.Settings.LastEmail = s.Email;
        _local.Save();

        _ = _root.GetRequiredService<ActivityLogService>()
            .LogAsync(s.Uid, MachineId.Current, ActivityLogTypes.Login, $"{MachineId.MachineName} v{AppVersion}");
        return null;
    }

    private static string? DescribeLicense(LicenseCheckResult r) => r.Status switch
    {
        LicenseStatus.Ok => null,
        LicenseStatus.Locked => Loc.T("Err_Login_Locked"),
        LicenseStatus.Expired => Loc.F("Err_Login_Expired", r.Profile?.ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd")),
        LicenseStatus.DeviceLimitReached => Loc.F("Err_Login_DeviceLimit", r.Profile?.MaxDevices),
        _ => Loc.T("Err_Login_NotProvisioned"),
    };

    /// <summary>Sau khi đăng nhập: tải cài đặt đồng bộ, rồi mở Kết nối SOLAPI hoặc Main.</summary>
    public async Task AfterLoginAsync(Window? from)
    {
        _scope?.Dispose();
        _scope = _root.CreateScope();

        try
        {
            await SessionServices.GetRequiredService<CloudDataService>().LoadSettingsAsync();
        }
        catch (Exception ex) when (ex is FirestoreException or FirebaseAuthException)
        {
            _logger.LogWarning(ex, "Không tải được cài đặt đồng bộ, dùng mặc định");
        }

        if (_credentials.HasCredentials) ShowMain();
        else ShowConnect(isChange: false);
        from?.Close();
        if (!App.PreviewMode) _licenseTimer.Start();
    }

    public void ShowLogin(string? message = null)
    {
        var w = _root.GetRequiredService<LoginWindow>();
        if (message is not null) w.ViewModel.ErrorMessage = message;
        Application.Current.MainWindow = w;
        w.Show();
    }

    public void ShowConnect(bool isChange)
    {
        var w = SessionServices.GetRequiredService<ConnectSolapiWindow>();
        w.ViewModel.IsChange = isChange;
        if (isChange && Application.Current.MainWindow is MainWindow main)
        {
            w.Owner = main;
            w.ShowDialog();
            return;
        }
        Application.Current.MainWindow = w;
        w.Show();
    }

    public void ShowMain()
    {
        var w = SessionServices.GetRequiredService<MainWindow>();
        Application.Current.MainWindow = w;
        w.Show();
    }

    public async Task LogoutAsync(string? reason = null)
    {
        _licenseTimer.Stop();
        if (_session.IsActive && !App.PreviewMode)
            await _root.GetRequiredService<ActivityLogService>()
                .LogAsync(_session.Uid, MachineId.Current, ActivityLogTypes.Logout, reason ?? "user");

        _auth.SignOut();
        _session.End();
        _session.ClearRefreshToken();

        var old = Application.Current.Windows.OfType<Window>().ToList();
        ShowLogin(reason);
        foreach (var w in old) w.Close();
        _scope?.Dispose();
        _scope = null;
    }

    private async Task RecheckLicenseAsync()
    {
        if (!_session.IsActive) return;
        try
        {
            var r = await _license.CheckStatusAsync(_session.Uid);
            if (r.Status == LicenseStatus.Ok)
            {
                _session.UpdateProfile(r.Profile!);
                return;
            }
            await LogoutAsync(DescribeLicense(r));
        }
        catch (FirebaseAuthException ex) when (ex.Kind is FirebaseAuthErrorKind.UserDisabled or FirebaseAuthErrorKind.SessionExpired)
        {
            await LogoutAsync(ErrorText.Describe(ex));
        }
        catch (Exception ex) when (ex is FirestoreException or FirebaseAuthException)
        {
            // Lỗi mạng tạm thời: thử lại lần sau.
            _logger.LogWarning(ex, "Kiểm tra giấy phép định kỳ thất bại");
        }
    }
}
