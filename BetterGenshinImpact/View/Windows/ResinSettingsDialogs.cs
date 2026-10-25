using System.Linq;
using System.Windows;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.ViewModel.Resin;

namespace BetterGenshinImpact.View.Windows;

/// <summary>树脂编辑弹窗的视图层入口，取消不返回草稿值。</summary>
public sealed class ResinSettingsDialogs : IResinSettingsDialogs
{
    /// <inheritdoc />
    public ResinUsage? EditResinUsage(string taskName, ResinUsageCapability capability, ResinUsage current)
    {
        ResinSettingsDialog? dialog = null;
        var editor = new ResinUsageEditorViewModel(taskName, capability, current, async confirmation =>
            await ThemedMessageBox.ShowAsync(confirmation.Message, confirmation.Title,
                MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Question,
                defaultResult: MessageBoxResult.No, owner: dialog) == MessageBoxResult.Yes);
        dialog = new ResinSettingsDialog(editor) { Owner = GetOwner() };
        return dialog.ShowDialog() == true ? editor.BuildValue() : null;
    }

    /// <inheritdoc />
    public int? EditCraftResin(int minResinToKeep)
    {
        var editor = new CraftResinEditorViewModel(minResinToKeep);
        var dialog = new ResinSettingsDialog(editor) { Owner = GetOwner() };
        return dialog.ShowDialog() == true ? editor.BuildValue() : null;
    }

    /// <summary>优先使用活动窗口，使二级确认显示在打开入口的弹窗上方。</summary>
    private static Window? GetOwner()
        => Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
           ?? Application.Current?.MainWindow;
}
