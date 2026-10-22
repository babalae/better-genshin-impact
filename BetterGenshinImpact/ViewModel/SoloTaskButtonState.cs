using BetterGenshinImpact.GameTask.SoloTask.Execution;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel;

/// <summary>
/// 一个独立任务按钮的只读展示状态。
/// </summary>
/// <param name="taskId">此按钮负责管理的独立任务标识</param>
public sealed partial class SoloTaskButtonState(string taskId) : ObservableObject
{
    /// <summary>
    /// 此按钮负责管理的独立任务标识。
    /// </summary>
    public string TaskId { get; } = taskId;

    /// <summary>
    /// 当前可管理的实例总数。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    private int _targetCount = 1;

    /// <summary>
    /// 当前正在运行此任务的实例数量。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    private int _activeRunCount;

    /// <summary>
    /// 单实例目标中的当前运行状态；多实例时不参与按钮文字判断。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    private SoloTaskRunState _singleTargetState;

    /// <summary>
    /// 此按钮发出的启动或停止请求是否仍在等待目标实例确认。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    private bool _isOperationPending;

    /// <summary>
    /// 单实例当前是否正被其他独立任务占用。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    private bool _isBlockedByOtherTask;

    /// <summary>
    /// 根据实例数量和活动运行数量生成按钮文字。
    /// </summary>
    public string ButtonText => IsOperationPending
        ? "处理中…"
        : TargetCount <= 1
            ? SingleTargetState switch
            {
                SoloTaskRunState.Starting => "启动中",
                SoloTaskRunState.Running => "停止",
                SoloTaskRunState.Stopping => "停止中",
                _ => "启动"
            }
            : ActiveRunCount > 0 ? $"管理（{ActiveRunCount}）" : "启动";

    /// <summary>
    /// 当前是否允许再次进入启动或管理流程。
    /// </summary>
    public bool CanInteract => !IsOperationPending
                               && !IsBlockedByOtherTask
                               && (TargetCount > 1 || SingleTargetState is SoloTaskRunState.Idle or SoloTaskRunState.Running);

    /// <summary>
    /// 使用最新实例快照刷新按钮展示。
    /// </summary>
    public void Update(
        int targetCount,
        int activeRunCount,
        SoloTaskRunState singleTargetState,
        bool isBlockedByOtherTask,
        bool isOperationPending)
    {
        TargetCount = targetCount;
        ActiveRunCount = activeRunCount;
        SingleTargetState = singleTargetState;
        IsBlockedByOtherTask = isBlockedByOtherTask;
        IsOperationPending = isOperationPending;
    }
}
