using System.Windows.Controls;
using SolapiSender.UserApp.ViewModels.Pages;

namespace SolapiSender.UserApp.Views.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += (_, _) => viewModel.Reload();
    }
}
