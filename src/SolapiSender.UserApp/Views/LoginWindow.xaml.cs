using System.Windows;
using SolapiSender.UserApp.ViewModels;
using Wpf.Ui.Controls;

namespace SolapiSender.UserApp.Views;

public partial class LoginWindow : FluentWindow
{
    public LoginViewModel ViewModel { get; }

    public LoginWindow(LoginViewModel viewModel)
    {
        ViewModel = viewModel;
        viewModel.Owner = this;
        DataContext = viewModel;
        InitializeComponent();
        viewModel.ReadPassword = () => PasswordInput.Password;
        Loaded += (_, _) =>
        {
            if (string.IsNullOrEmpty(viewModel.Email)) return;
            PasswordInput.Focus();
        };
    }

    // PasswordBox không hỗ trợ binding hai chiều an toàn; đẩy giá trị sang ViewModel thủ công.
    private void OnPasswordChanged(object sender, RoutedEventArgs e) => ViewModel.Password = PasswordInput.Password;
}
