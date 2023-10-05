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
using System.Windows.Navigation;
using System.Windows.Shapes;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.View.Pages
{
    /// <summary>
    /// MacroSettingsPage.xaml 的交互逻辑
    /// </summary>
    public partial class MacroSettingsPage : Page
    {
        MacroSettingsPageViewModel ViewModel { get; }

        public MacroSettingsPage(MacroSettingsPageViewModel viewModel)
        {
            DataContext = ViewModel = viewModel;
            InitializeComponent();
        }
    }
}