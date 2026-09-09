using System.Linq;
using System.Windows;
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

    /// <summary>
    /// 以单实例方式打开名单编辑窗口：已打开时仅激活；Owner 取当前活动窗口（配置组独立窗口场景下
    /// 归属正确），避免以主窗口为 Owner 造成模态语义错乱或重复开窗。
    /// </summary>
    public static void OpenForOwner()
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        if (app.Windows.Cast<Window>().FirstOrDefault(w => w is GuardianAvatarListDialog) is { } existing)
        {
            existing.Activate();
            return;
        }

        var owner = app.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive && w is not GuardianAvatarListDialog)
                    ?? app.MainWindow;
        var dialog = new GuardianAvatarListDialog
        {
            Owner = owner
        };
        dialog.ShowDialog();
    }
}
