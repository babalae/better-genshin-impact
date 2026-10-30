using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Pages;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>自动配音设置窗口：引擎与声线、角色、识别、高级四页；DataContext 为单例设置模型。</summary>
public partial class QuestVoiceSettingsWindow : FluentWindow
{
    /// <summary>沿用 MusicSettingsWindow 的约定：构造注入模型并启用主题背景。</summary>
    public QuestVoiceSettingsWindow(QuestVoiceSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => WindowHelper.TryApplySystemBackdrop(this);
    }
}
