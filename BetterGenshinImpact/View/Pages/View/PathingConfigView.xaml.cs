using BetterGenshinImpact.Core.Config;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages.View
{
    /// <summary>
    /// PathingConfigView.xaml 的交互逻辑
    /// </summary>
    public partial class PathingConfigView : UserControl
    {
        public PathingConfigView(PathingConfig pathingConfig)
        {
            InitializeComponent();
            DataContext = pathingConfig;
        }
    }
}
