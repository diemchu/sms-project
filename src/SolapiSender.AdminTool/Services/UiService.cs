using System.Windows;
using Microsoft.Win32;
using Wpf.Ui;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace SolapiSender.AdminTool.Services;

public sealed class UiService
{
    private readonly ISnackbarService _snackbar;

    public UiService(ISnackbarService snackbar) => _snackbar = snackbar;

    public void Success(string message) => Show("Hoàn tất", message, ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
    public void Error(string message) => Show("Lỗi", message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
    public void Error(Exception ex) => Error(Describe(ex));

    public static string Describe(Exception ex) => ex switch
    {
        AdminException a => a.Message,
        Grpc.Core.RpcException r when r.StatusCode == Grpc.Core.StatusCode.PermissionDenied =>
            "Service account không có quyền truy cập Firestore (cần vai trò Firebase Admin / Cloud Datastore User).",
        Grpc.Core.RpcException r when r.StatusCode == Grpc.Core.StatusCode.FailedPrecondition && r.Status.Detail.Contains("index") =>
            "Firestore cần index cho truy vấn này. Hãy deploy firebase/firestore.indexes.json (xem README).",
        Grpc.Core.RpcException r when r.StatusCode is Grpc.Core.StatusCode.Unavailable or Grpc.Core.StatusCode.DeadlineExceeded =>
            "Không kết nối được Firestore. Kiểm tra Internet.",
        Grpc.Core.RpcException r => $"Firestore báo lỗi {r.StatusCode}: {r.Status.Detail}",
        FirebaseAdmin.FirebaseException f => "Firebase báo lỗi: " + f.Message,
        System.Net.Http.HttpRequestException => "Không kết nối được máy chủ. Kiểm tra Internet.",
        _ => ex.Message,
    };

    private void Show(string title, string message, ControlAppearance appearance, SymbolRegular icon)
    {
        if (_snackbar.GetSnackbarPresenter() is null)
        {
            _ = AlertAsync(title, message);
            return;
        }
        _snackbar.Show(title, message, appearance, new SymbolIcon(icon), TimeSpan.FromSeconds(4));
    }

    public async Task AlertAsync(string title, string message)
    {
        var box = CreateBox(title, message);
        box.CloseButtonText = "OK";
        await box.ShowDialogAsync();
    }

    public async Task<bool> ConfirmAsync(string title, string message, string primaryText, bool danger = false)
    {
        var box = CreateBox(title, message);
        box.PrimaryButtonText = primaryText;
        box.PrimaryButtonAppearance = danger ? ControlAppearance.Danger : ControlAppearance.Primary;
        box.CloseButtonText = "Hủy";
        return await box.ShowDialogAsync() == MessageBoxResult.Primary;
    }

    private static MessageBox CreateBox(string title, string message) => new()
    {
        Title = title,
        Content = new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 22, MaxWidth = 460 },
        Owner = ActiveWindow(),
        MinWidth = 420,
        MaxWidth = 520,
        FontFamily = Application.Current.TryFindResource("AppFont") as System.Windows.Media.FontFamily
                     ?? new System.Windows.Media.FontFamily("Segoe UI"),
    };

    public static string? PickOpenFile(string filter)
    {
        var dlg = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        return dlg.ShowDialog(ActiveWindow()) == true ? dlg.FileName : null;
    }

    public static string? PickSaveFile(string filter, string fileName)
    {
        var dlg = new SaveFileDialog { Filter = filter, FileName = fileName, AddExtension = true };
        return dlg.ShowDialog(ActiveWindow()) == true ? dlg.FileName : null;
    }

    public static Window? ActiveWindow() =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
}
