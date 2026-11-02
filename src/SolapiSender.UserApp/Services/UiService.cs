using System.Windows;
using Microsoft.Win32;
using SolapiSender.UserApp.Localization;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace SolapiSender.UserApp.Services;

/// <summary>Hộp thoại, thông báo nhanh, chọn file, theme — để ViewModel không phụ thuộc trực tiếp vào View.</summary>
public sealed class UiService
{
    private readonly ISnackbarService _snackbar;

    public UiService(ISnackbarService snackbar) => _snackbar = snackbar;

    public void Success(string message) => Show(Loc.T("Common_Done"), message, ControlAppearance.Success, SymbolRegular.CheckmarkCircle24);
    public void Info(string message) => Show(Loc.T("Common_Info"), message, ControlAppearance.Info, SymbolRegular.Info24);
    public void Error(string message) => Show(Loc.T("Common_Error"), message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);
    public void Error(Exception ex) => Error(ErrorText.Describe(ex));

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
        box.CloseButtonText = Loc.T("Common_OK");
        await box.ShowDialogAsync();
    }

    public async Task<bool> ConfirmAsync(string title, object content, string primaryText, bool danger = false)
    {
        var box = CreateBox(title, content);
        box.PrimaryButtonText = primaryText;
        box.PrimaryButtonAppearance = danger ? ControlAppearance.Danger : ControlAppearance.Primary;
        box.CloseButtonText = Loc.T("Common_Cancel");
        return await box.ShowDialogAsync() == MessageBoxResult.Primary;
    }

    /// <summary>MessageBox của WPF-UI tự co theo nội dung → đặt bề rộng tối thiểu và font của app.</summary>
    private static MessageBox CreateBox(string title, object content) => new()
    {
        Title = title,
        Content = content is string text
            ? new System.Windows.Controls.TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 22, MaxWidth = 460 }
            : content,
        Owner = ActiveWindow(),
        MinWidth = 420,
        MaxWidth = 520,
        FontFamily = Application.Current.TryFindResource("AppFont") as System.Windows.Media.FontFamily
                     ?? new System.Windows.Media.FontFamily("Segoe UI"),
    };

    /// <summary>Mở hộp thoại chọn cột Excel; trả về danh sách thô hoặc null nếu hủy.</summary>
    public List<Core.Models.RawRecipient>? ShowImportDialog(string path, Core.Excel.ExcelService excel)
    {
        try
        {
            var dlg = new Views.ImportDialog(path, excel) { Owner = ActiveWindow() };
            return dlg.ShowDialog() == true ? dlg.Result : null;
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or ArgumentException)
        {
            Error(Loc.F("Import_ReadError", ex.Message));
            return null;
        }
    }

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

    public static void ApplyTheme(string theme)
    {
        var target = theme switch
        {
            "light" => ApplicationTheme.Light,
            "dark" => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light,
        };
        ApplicationThemeManager.Apply(target, WindowBackdropType.Mica, true);
        foreach (Window w in Application.Current.Windows) ApplicationThemeManager.Apply(w);
    }

    public static Window? ActiveWindow() =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current.MainWindow;
}
