using System.Windows.Controls;
using SolapiSender.AdminTool.ViewModels;

namespace SolapiSender.AdminTool.Views.Pages;

public partial class UsersPage : Page
{
    public UsersPage(UsersViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.OnShownAsync();
    }
}

public partial class LogsPage : Page
{
    public LogsPage(LogsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.OnShownAsync();
    }
}

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
