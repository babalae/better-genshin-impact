using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Service.Instance;
using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 独立任务实例管理窗口中的单个实例展示行。
/// </summary>
public sealed partial class SoloTaskInstanceRowViewModel : ObservableObject
{
    /// <summary>
    /// 此行对应的实例端点。
    /// </summary>
    public InstanceEndpoint Endpoint { get; }

    /// <summary>
    /// 当前实例的显示名称。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 与实例类型匹配的 WPF-UI 图标。
    /// </summary>
    public SymbolRegular InstanceIcon { get; }

    /// <summary>
    /// 最近一次同步到界面的真实运行状态。
    /// </summary>
    public SoloTaskStatusSnapshot Status { get; private set; } = SoloTaskStatusSnapshot.Idle();

    /// <summary>
    /// 此实例是否仍在线。
    /// </summary>
    [ObservableProperty]
    private bool _isOnline = true;

    /// <summary>
    /// 当前运行状态的中文说明。
    /// </summary>
    [ObservableProperty]
    private string _statusText = "可启动";

    /// <summary>
    /// 上一次运行失败或请求失败的补充说明。
    /// </summary>
    [ObservableProperty]
    private string? _detailText;

    /// <summary>
    /// 行操作按钮文字。
    /// </summary>
    [ObservableProperty]
    private string _actionText = "启动";

    /// <summary>
    /// 当前是否允许对这一行发起启动或停止请求。
    /// </summary>
    [ObservableProperty]
    private bool _canOperate = true;

    /// <summary>
    /// 创建一个实例展示行。
    /// </summary>
    public SoloTaskInstanceRowViewModel(InstanceEndpoint endpoint)
    {
        Endpoint = endpoint;
        DisplayName = GetDisplayName(endpoint);
        InstanceIcon = endpoint.InstanceType == BetterGiInstanceType.WebView
            ? SymbolRegular.Cloud24
            : SymbolRegular.DesktopCursor24;
    }

    /// <summary>
    /// 使用实例在线状态和任务状态刷新整行展示。
    /// </summary>
    public void Update(
        SoloTaskStatusSnapshot status,
        SoloTaskType managedTaskType,
        bool isOnline,
        bool isOperationPending)
    {
        Status = status;
        IsOnline = isOnline;
        DetailText = status.ErrorMessage;

        if (!isOnline)
        {
            StatusText = "离线";
            ActionText = "不可用";
            CanOperate = false;
            return;
        }

        if (!status.IsActive)
        {
            StatusText = "可启动";
            ActionText = "启动";
            CanOperate = !isOperationPending;
            return;
        }

        if (status.TaskType != managedTaskType)
        {
            StatusText = $"正被“{SoloTaskRegistry.GetDisplayName(status.TaskType!.Value)}”占用";
            ActionText = "已占用";
            CanOperate = false;
            return;
        }

        switch (status.State)
        {
            case SoloTaskRunState.Starting:
                StatusText = "启动中";
                ActionText = "请稍候";
                CanOperate = false;
                break;
            case SoloTaskRunState.Running:
                StatusText = "运行中";
                ActionText = "停止";
                CanOperate = !isOperationPending;
                break;
            case SoloTaskRunState.Stopping:
                StatusText = "停止中";
                ActionText = "请稍候";
                CanOperate = false;
                break;
            default:
                StatusText = "可启动";
                ActionText = "启动";
                CanOperate = !isOperationPending;
                break;
        }
    }

    /// <summary>
    /// 按实例类型生成稳定且易区分的显示名称。
    /// </summary>
    private static string GetDisplayName(InstanceEndpoint endpoint)
    {
        return endpoint.InstanceType switch
        {
            BetterGiInstanceType.Primary => "主实例",
            BetterGiInstanceType.ChildSession => $"桌面分身（会话 {endpoint.WindowsSessionId}）",
            BetterGiInstanceType.WebView => $"云原神（{endpoint.InstanceName ?? endpoint.ProcessId.ToString()}）",
            _ => $"实例 {endpoint.ProcessId}"
        };
    }
}
