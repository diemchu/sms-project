using System.Windows.Controls;
using System.Windows.Threading;
using SolapiSender.UserApp.ViewModels.Pages;

namespace SolapiSender.UserApp.Views.Pages;

public partial class BulkSendPage : Page
{
    public BulkSendPage(BulkSendViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await viewModel.OnShownAsync();

        // Bảng kết quả nằm cuối trang: cuộn tới khi gửi xong để người dùng thấy ngay.
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BulkSendViewModel.ResultSummary) && viewModel.ResultSummary is not null)
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => ResultsCard.BringIntoView());
        };
    }
}
