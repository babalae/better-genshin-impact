using System.Windows.Controls;
using BetterGenshinImpact.ViewModel.Pages.QuestVoiceSettings;

namespace BetterGenshinImpact.View.Pages;

/// <summary>六种其他引擎的独立设置页；页面选择不影响配音默认引擎。</summary>
public partial class QuestVoiceOtherEnginesPage : UserControl
{
    /// <summary>单例设置模型，切换页面保留输入草稿。</summary>
    public OtherEnginesSettingsViewModel ViewModel { get; }

    /// <summary>遵循所在页面的 DataContext=this 及 ViewModel 绑定约定。</summary>
    public QuestVoiceOtherEnginesPage(OtherEnginesSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }
}