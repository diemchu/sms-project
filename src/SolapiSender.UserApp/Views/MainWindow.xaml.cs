using System.Windows;
using SolapiSender.UserApp.Services;
using SolapiSender.UserApp.ViewModels;
using SolapiSender.UserApp.Views.Pages;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SolapiSender.UserApp.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainWindowViewModel viewModel, AppFlow flow, ISnackbarService snackbar)
    {
        DataContext = viewModel;
        InitializeComponent();

        // Trang được lấy từ DI scope của phiên đăng nhập hiện tại.
        RootNavigation.SetServiceProvider(flow.SessionServices);
        snackbar.SetSnackbarPresenter(SnackbarPresenter);

        // Loaded có thể chạy lại (đổi theme, thu gọn pane…) → chỉ điều hướng lần đầu.
        RoutedEventHandler? firstLoad = null;
        firstLoad = (_, _) =>
        {
            Loaded -= firstLoad;
            RootNavigation.Navigate(typeof(DashboardPage));
        };
        Loaded += firstLoad;
        Closed += (_, _) =>
        {
            if (ReferenceEquals(snackbar.GetSnackbarPresenter(), SnackbarPresenter))
                snackbar.SetSnackbarPresenter(null!);
        };
    }
}
