using BetterGenshinImpact.ViewModel;
using BetterGenshinImpact.ViewModel.Pages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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