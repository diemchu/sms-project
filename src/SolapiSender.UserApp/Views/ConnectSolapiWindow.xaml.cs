using System.Windows;
using SolapiSender.UserApp.ViewModels;
using Wpf.Ui.Controls;

namespace SolapiSender.UserApp.Views;

public partial class ConnectSolapiWindow : FluentWindow
{
    public ConnectSolapiViewModel ViewModel { get; }

    public ConnectSolapiWindow(ConnectSolapiViewModel viewModel)
    {
        ViewModel = viewModel;
        viewModel.Owner = this;
        DataContext = viewModel;
        InitializeComponent();
        viewModel.ReadSecret = () => SecretInput.Password;
        if (Owner is null) WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    private void OnSecretChanged(object sender, RoutedEventArgs e) => ViewModel.ApiSecret = SecretInput.Password;
}
