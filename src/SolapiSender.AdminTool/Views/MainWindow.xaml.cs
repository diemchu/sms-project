using System.Windows;
using SolapiSender.AdminTool.Services;
using SolapiSender.AdminTool.ViewModels;
using SolapiSender.AdminTool.Views.Pages;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SolapiSender.AdminTool.Views;

public partial class MainWindow : FluentWindow
{
    private readonly UsersViewModel _usersVm;
    private readonly LogsViewModel _logsVm;

    public MainWindow(IServiceProvider sp, IUserAdminService users, IAdminAuthService auth, ISnackbarService snackbar,
        UsersViewModel usersVm, LogsViewModel logsVm)
    {
        _usersVm = usersVm;
        _logsVm = logsVm;
        ProjectId = users.Account.ProjectId;
        AdminText = $"Đăng nhập: {auth.CurrentAdmin}";
        Title = App.PreviewMode ? "SolapiSender Admin — PREVIEW" : $"SolapiSender Admin — {ProjectId}";
        InitializeComponent();

        RootNavigation.SetServiceProvider(sp);
        snackbar.SetSnackbarPresenter(SnackbarPresenter);
        usersVm.ShowLogsRequested += OnShowLogs;

        RoutedEventHandler? first = null;
        first = (_, _) =>
        {
            Loaded -= first;
            RootNavigation.Navigate(typeof(UsersPage));
        };
        Loaded += first;
        Closed += (_, _) => usersVm.ShowLogsRequested -= OnShowLogs;
    }

    public string ProjectId { get; }
    public string AdminText { get; }

    private async void OnShowLogs(AdminUser user)
    {
        RootNavigation.Navigate(typeof(LogsPage));
        await _logsVm.ShowForAsync(user);
    }
}
