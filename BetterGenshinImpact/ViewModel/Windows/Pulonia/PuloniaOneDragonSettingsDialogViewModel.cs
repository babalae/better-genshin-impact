using System;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 一条龙“高级设置”窗口的视图模型：只服务打开时选中的一个清单步骤，高级编辑状态按需计算，修改即时写入共享文档。
/// </summary>
public partial class PuloniaOneDragonSettingsDialogViewModel : ObservableObject, IDisposable
{
    /// <summary>编辑会话，提供更新资源和设置方案命令。</summary>
    public PuloniaPlanEditingSession Session { get; }

    /// <summary>被编辑的清单步骤。</summary>
    public PuloniaTaskNodeViewModel Node { get; }

    /// <summary>步骤所属的一条龙配置。</summary>
    public PuloniaTaskPlanDocumentViewModel Document => Node.Document;

    /// <summary>共享设置入口、原始参数、策略、来源与有效参数来源，只在本窗口打开期间存在。</summary>
    public PuloniaTaskNodeAdvancedSettingsViewModel Advanced { get; }

    /// <summary>为指定步骤建立高级设置窗口状态。</summary>
    /// <param name="session">编辑会话。</param>
    /// <param name="node">被编辑的清单步骤。</param>
    /// <param name="dialogs">共享设置与方案窗口入口；为空时共享设置入口不可用。</param>
    public PuloniaOneDragonSettingsDialogViewModel(PuloniaPlanEditingSession session, PuloniaTaskNodeViewModel node,
        IPuloniaTaskSettingsDialogs? dialogs = null)
    {
        Session = session;
        Node = node;
        Advanced = new PuloniaTaskNodeAdvancedSettingsViewModel(node.Document, node, dialogs, session);
    }

    /// <summary>窗口关闭时释放高级设置的文档订阅。</summary>
    public void Dispose() => Advanced.Dispose();
}
