using System.Windows.Controls;
using SolapiSender.UserApp.ViewModels.Pages;

namespace SolapiSender.UserApp.Views.Pages;

public partial class DashboardPage : Page
{
    public DashboardPage(DashboardViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.OnShownAsync();
    }
}
