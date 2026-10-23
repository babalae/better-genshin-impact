using BetterGenshinImpact.GameTask.SoloTask.Execution;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Service.Instance;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.ViewModel.Windows;

/// <summary>
/// 云原神管理窗口中的实例行，仅展示进程、连接和独立任务的已知状态。
/// </summary>
public sealed partial class CloudWebInstanceRowViewModel : ObservableObject
{
    /// <summary>
    /// 实例名称，也是其独立浏览器数据目录的标识。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 当前在线端点；操作时仍由实例服务重新校验连接。
    /// </summary>
    public InstanceEndpoint? Endpoint { get; private set; }

    /// <summary>
    /// 实例进程是否仍在运行。
    /// </summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>
    /// 是否已连接主实例；连接成功不代表游戏已登录或截图器已就绪。
    /// </summary>
    [ObservableProperty]
    private bool _isConnected;

    /// <summary>
    /// 行内展示的连接状态。
    /// </summary>
    [ObservableProperty]
    private string _statusText = "未启动";

    /// <summary>
    /// 当前独立任务或下一步操作提示。
    /// </summary>
    [ObservableProperty]
    private string _detailText = "登录状态独立保存";

    /// <summary>
    /// 主操作根据进程状态显示打开游戏或查看窗口。
    /// </summary>
    [ObservableProperty]
    private string _actionText = "打开游戏";

    /// <summary>
    /// 是否允许启动进程或激活已连接的游戏窗口。
    /// </summary>
    [ObservableProperty]
    private bool _canOpen;

    /// <summary>
    /// 是否可以通过在线端点正常关闭实例。
    /// </summary>
    [ObservableProperty]
    private bool _canClose;

    /// <summary>
    /// 只有进程已退出且没有其他操作时才允许删除浏览器数据。
    /// </summary>
    [ObservableProperty]
    private bool _canDelete;

    /// <summary>
    /// 创建按实例名长期复用的展示行，避免轮询时重建控件。
    /// </summary>
    public CloudWebInstanceRowViewModel(string name)
    {
        Name = name;
    }

    /// <summary>
    /// 合并实例基础信息、在线连接和独立任务快照，更新展示与操作权限。
    /// </summary>
    public void Update(
        CloudWebInstanceItem instance,
        InstanceEndpoint? endpoint,
        SoloTaskStatusSnapshot? status,
        Func<string?, string> getTaskDisplayName,
        bool isOperationPending)
    {
        Endpoint = endpoint;
        IsConnected = endpoint is not null;
        IsRunning = instance.IsRunning || IsConnected;
        StatusText = IsConnected ? "已连接" : IsRunning ? "等待连接" : "未启动";
        ActionText = IsConnected ? "查看窗口" : IsRunning ? "等待连接" : "打开游戏";
        CanOpen = !isOperationPending && (!IsRunning || IsConnected);
        CanClose = !isOperationPending && IsConnected;
        CanDelete = !isOperationPending && !IsRunning;

        // 连接只表示进程在线；不把 IPC 登记误当作游戏就绪，也不推测 Pulonia 的运行状态。
        if (status?.IsActive == true)
        {
            var phase = status.State switch
            {
                SoloTaskRunState.Starting => "任务启动中",
                SoloTaskRunState.Stopping => "任务停止中",
                _ => "正在执行"
            };
            DetailText = $"{phase} · {getTaskDisplayName(status.TaskId)}";
        }
        else
        {
            DetailText = IsConnected ? "暂无独立任务" : IsRunning
                ? "请等待连接，也可从游戏窗口手动关闭"
                : "登录状态独立保存";
        }
    }
}
