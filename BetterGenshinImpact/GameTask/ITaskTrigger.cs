using BetterGenshinImpact.GameTask.Common.BgiVision;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 触发器接口
/// * 可以用于任务的触发、任务触发前的控件展示
/// * 也可以是任务的本身
///
/// 需要短时间内持续循环获取游戏图像的，使用触发器；
/// 需要休眠等待且有一定流程的，应自行实现Task
///
/// 生命周期：实例在启动截图器时创建，停止截图器时丢弃，期间不会重建。
/// 是否运行由 <see cref="TaskTriggerDispatcher"/> 每帧决定：不在任务中时看 <see cref="IsEnabledByConfig"/>，
/// 任务中只运行任务或脚本通过 AddTrigger 启用的触发器。状态变化时回调 <see cref="OnEnabled"/> / <see cref="OnDisabled"/>。
/// 所有回调都在调度器的 Tick 中串行执行，触发器内部不需要为它们加锁
/// </summary>
public interface ITaskTrigger
{
    /// <summary>
    /// 触发器名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 执行优先级，越大越先执行
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 用户是否开启。调度器每帧读取，实现里直接读配置，不要缓存
    /// </summary>
    bool IsEnabledByConfig { get; }

    /// <summary>
    /// 当前是否处于独占模式
    /// </summary>
    bool IsExclusive { get; }

    /// <summary>
    /// 处于可以后台运行的状态（原神窗口不处于激活状态）。调度器每帧读取
    /// </summary>
    bool IsBackgroundRunning => false;

    GameUiCategory SupportedGameUiCategory => GameUiCategory.Unknown;

    /// <summary>
    /// 停用 → 启用时调用，用于重置运行状态、加载派生数据
    /// </summary>
    /// <param name="options">脚本通过 AddTrigger 传入的参数，没有则为 null</param>
    void OnEnabled(object? options)
    {
    }

    /// <summary>
    /// 启用 → 停用时调用，用于清理自己的绘制、UI 状态和等待
    /// </summary>
    void OnDisabled()
    {
    }

    /// <summary>
    /// 捕获图像后操作。只在启用且未暂停时调用
    /// </summary>
    /// <param name="content">捕获的图片等内容</param>
    void OnCapture(CaptureContent content);
}
