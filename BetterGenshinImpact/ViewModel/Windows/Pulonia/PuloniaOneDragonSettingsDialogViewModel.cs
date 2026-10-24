using System;
using System.Linq;
using System.Windows;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.View.Windows.Pulonia;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 一条龙“高级设置”窗口的视图模型：只服务打开时选中的一个清单步骤，高级编辑状态按需计算，修改即时写入共享文档。
/// </summary>
public partial class PuloniaOneDragonSettingsDialogViewModel : ObservableObject, IDisposable
{
    /// <summary>编辑会话，提供更新资源和刷新预设命令。</summary>
    public PuloniaPlanEditingSession Session { get; }

    /// <summary>被编辑的清单步骤。</summary>
    public PuloniaTaskNodeViewModel Node { get; }

    /// <summary>步骤所属的一条龙配置。</summary>
    public PuloniaTaskPlanDocumentViewModel Document => Node.Document;

    /// <summary>原始参数、策略、来源、预设与有效参数来源，只在本窗口打开期间存在。</summary>
    public PuloniaTaskNodeAdvancedSettingsViewModel Advanced { get; }

    /// <summary>为指定步骤建立高级设置窗口状态。</summary>
    /// <param name="session">编辑会话。</param>
    /// <param name="node">被编辑的清单步骤。</param>
    public PuloniaOneDragonSettingsDialogViewModel(PuloniaPlanEditingSession session, PuloniaTaskNodeViewModel node)
    {
        Session = session;
        Node = node;
        // 一条龙的公共设置使用本窗口自己的表单，不经过配置组表单入口。
        Advanced = new PuloniaTaskNodeAdvancedSettingsViewModel(node.Document, node, dialogs: null);
    }

    /// <summary>公共设置按本步骤覆盖提交，禁止写回旧全局任务配置。</summary>
    /// <param name="taskType">pathing、javascript 或 shell。</param>
    [RelayCommand]
    private void EditCommonSettings(string taskType)
    {
        if (taskType is not ("pathing" or "javascript" or "shell")) return;
        try
        {
            var original = PuloniaTaskCommonSettings.ToParameters(taskType,
                PuloniaTaskCommonSettings.FromParameters(taskType, Document.GetCommonSettingsParameters(Node, taskType)));
            var draft = new PuloniaOneDragonCommonSettingsViewModel(taskType, original);
            var dialog = new PuloniaOneDragonCommonSettingsDialog(draft) { Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow };
            if (dialog.ShowDialog() == true) Document.ApplyCommonSettings(Node, taskType, original, draft.Result ?? original, draft.RestoreInheritance);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or Newtonsoft.Json.JsonException
                                   or ArgumentException or InvalidOperationException or OverflowException)
        { ThemedMessageBox.Error("任务设置未应用：" + ex.Message, "任务设置"); }
    }

    /// <summary>窗口关闭时释放高级设置的文档订阅。</summary>
    public void Dispose() => Advanced.Dispose();
}
