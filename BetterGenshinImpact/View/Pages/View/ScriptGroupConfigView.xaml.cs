using System.Windows.Controls;
using BetterGenshinImpact.Core.Script.Group;

namespace BetterGenshinImpact.View.Pages.View
{
    /// <summary>
    /// ScriptGroupConfigView.xaml 的交互逻辑
    /// </summary>
    public partial class ScriptGroupConfigView : UserControl
    {
        public ScriptGroupConfigView(ScriptGroupConfig config)
        {
            InitializeComponent();
            DataContext = config;
        }
    }
}
