using System.Windows;
using SolapiSender.AdminTool.ViewModels;
using Wpf.Ui.Controls;

namespace SolapiSender.AdminTool.Views;

public partial class SetupWindow : FluentWindow
{
    public SetupViewModel ViewModel { get; }

    public SetupWindow(SetupViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }
}

public partial class LoginWindow : FluentWindow
{
    public LoginViewModel ViewModel { get; }

    public LoginWindow(LoginViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        viewModel.ReadPassword = () => PasswordInput.Password;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => ViewModel.Password = PasswordInput.Password;
}

public partial class ChangePasswordWindow : FluentWindow
{
    public ChangePasswordViewModel ViewModel { get; }

    public ChangePasswordWindow(ChangePasswordViewModel viewModel)
    {
        ViewModel = viewModel;
        viewModel.Owner = this;
        DataContext = viewModel;
        InitializeComponent();
        viewModel.ReadPasswords = () => (CurrentInput.Password, NewInput.Password, ConfirmInput.Password);
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentPassword = CurrentInput.Password;
        ViewModel.NewPassword = NewInput.Password;
        ViewModel.ConfirmPassword = ConfirmInput.Password;
    }
}

public partial class UserEditWindow : FluentWindow
{
    public UserEditViewModel ViewModel { get; }

    public UserEditWindow(UserEditViewModel viewModel)
    {
        ViewModel = viewModel;
        viewModel.Owner = this;
        DataContext = viewModel;
        InitializeComponent();
    }
}
