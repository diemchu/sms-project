using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SolapiSender.AdminTool.Views;

namespace SolapiSender.AdminTool.Services;

/// <summary>Luồng màn hình: (Kết nối Firebase) → Đăng nhập admin → (Đổi mật khẩu bắt buộc) → Main.</summary>
public sealed class AdminFlow
{
    private readonly IServiceProvider _sp;
    private readonly ServiceAccountStore _store;
    private readonly FirebaseAdminContext _ctx;
    private readonly IAdminAuthService _auth;
    private readonly ILogger<AdminFlow> _logger;

    public AdminFlow(IServiceProvider sp, ServiceAccountStore store, FirebaseAdminContext ctx, IAdminAuthService auth,
        ILogger<AdminFlow> logger)
    {
        _sp = sp;
        _store = store;
        _ctx = ctx;
        _auth = auth;
        _logger = logger;
    }

    public void Start()
    {
        if (App.PreviewMode)
        {
            ShowLogin();
            return;
        }

        var json = _store.Load();
        if (json is null)
        {
            ShowSetup(null);
            return;
        }
        try
        {
            _ctx.Initialize(json);
            ShowLogin();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Không khởi tạo được Firebase từ service account đã lưu");
            ShowSetup("Service account đã lưu không dùng được: " + UiService.Describe(ex));
        }
    }

    public void ShowSetup(string? error)
    {
        var w = _sp.GetRequiredService<SetupWindow>();
        w.ViewModel.ErrorMessage = error;
        Replace(w);
    }

    public void ShowLogin(string? message = null)
    {
        var w = _sp.GetRequiredService<LoginWindow>();
        if (message is not null) w.ViewModel.ErrorMessage = message;
        Replace(w);
    }

    /// <summary>Sau khi đăng nhập: buộc đổi mật khẩu nếu cần, rồi mở Main.</summary>
    public void AfterLogin(bool mustChangePassword)
    {
        if (mustChangePassword)
        {
            var dlg = _sp.GetRequiredService<ChangePasswordWindow>();
            dlg.ViewModel.Mandatory = true;
            dlg.ViewModel.Username = _auth.CurrentAdmin ?? "";
            Replace(dlg);
            return;
        }
        Replace(_sp.GetRequiredService<MainWindow>());
    }

    public void ChangePasswordOptional()
    {
        var dlg = _sp.GetRequiredService<ChangePasswordWindow>();
        dlg.ViewModel.Mandatory = false;
        dlg.ViewModel.Username = _auth.CurrentAdmin ?? "";
        dlg.Owner = Application.Current.MainWindow;
        dlg.ShowDialog();
    }

    public void Logout()
    {
        _auth.Logout();
        ShowLogin();
    }

    public void SwitchServiceAccount()
    {
        _auth.Logout();
        _store.Clear();
        _ctx.Dispose();
        ShowSetup(null);
    }

    /// <summary>Mở cửa sổ mới trước rồi đóng các cửa sổ cũ (ShutdownMode = OnLastWindowClose).</summary>
    private static void Replace(Window next)
    {
        var old = Application.Current.Windows.OfType<Window>().Where(w => w != next).ToList();
        Application.Current.MainWindow = next;
        next.Show();
        foreach (var w in old) w.Close();
    }
}
