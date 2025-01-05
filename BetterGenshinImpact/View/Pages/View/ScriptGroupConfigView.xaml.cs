using System.Windows.Controls;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.ViewModel.Pages.View;

namespace BetterGenshinImpact.View.Pages.View
{
    /// <summary>
    /// ScriptGroupConfigView.xaml 的交互逻辑
    /// </summary>
    public partial class ScriptGroupConfigView : UserControl
    {
        private ScriptGroupConfigViewModel ViewModel { get; }

        public ScriptGroupConfigView(ScriptGroupConfigViewModel viewModel)
        {
            DataContext  = ViewModel = viewModel;
            InitializeComponent();
            
        }
    }
}
