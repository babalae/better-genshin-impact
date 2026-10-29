using System.Windows.Controls;

namespace BetterGenshinImpact.View.Pages.QuestVoiceSettings;

/// <summary>独立引擎表单，继承 ContentControl 模板传入的引擎设置模型。</summary>
public partial class HuoshanSettingsView : UserControl
{
    /// <summary>视图只创建布局，交互由绑定和 Behavior 处理。</summary>
    public HuoshanSettingsView() => InitializeComponent();
}