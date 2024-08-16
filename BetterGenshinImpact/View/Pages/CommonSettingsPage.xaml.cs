using BetterGenshinImpact.ViewModel.Pages;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages
{
    /// <summary>
    /// CommonSettingsPage.xaml 的交互逻辑
    /// </summary>
    public partial class CommonSettingsPage : Page
    {
        CommonSettingsPageViewModel ViewModel { get; }
        public CommonSettingsPage(CommonSettingsPageViewModel viewModel)
        {
            DataContext = ViewModel = viewModel;
            InitializeComponent();
        }
    }
}
