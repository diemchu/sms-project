using System.Windows.Controls;
using SolapiSender.UserApp.ViewModels.Pages;

namespace SolapiSender.UserApp.Views.Pages;

public partial class TemplatesPage : Page
{
    public TemplatesPage(TemplatesViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.OnShownAsync();
    }
}
