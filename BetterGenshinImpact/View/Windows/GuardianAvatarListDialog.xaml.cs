using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Windows;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.View.Windows;

/// <summary>
/// 盾奶位名单编辑窗口：直接编辑 User/GuardianAvatarList.txt。
/// 自动战斗盾奶位"自动"模式按该名单顺序匹配队伍中的盾奶角色。
/// </summary>
public partial class GuardianAvatarListDialog : FluentWindow
{
    public GuardianAvatarListViewModel ViewModel { get; }

    public GuardianAvatarListDialog()
    {
        DataContext = ViewModel = new GuardianAvatarListViewModel();
        InitializeComponent();
        SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);
    }
}
