using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages
{
    public partial class HomePage
    {
        public HomePageViewModel ViewModel { get; }

        public HomePage(HomePageViewModel viewModel, HotKeyPageViewModel hotKeyPageViewModel)
        {
            DataContext = ViewModel = viewModel;
            InitializeComponent();

            // hotKeyPageViewModel 放在这里是为了在首页就初始化热键
        }
    }
}