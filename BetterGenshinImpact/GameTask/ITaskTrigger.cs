using BetterGenshinImpact.GameTask.Common.BgiVision;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 触发器接口
/// * 可以用于任务的触发、任务触发前的控件展示
/// * 也可以是任务的本身
///
/// 需要短时间内持续循环获取游戏图像的，使用触发器；
/// 需要休眠等待且有一定流程的，应自行实现Task
/// </summary>
public interface ITaskTrigger
{
    /// <summary>
    /// 触发器名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 是否处于启用状态
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// 执行优先级，越大越先执行
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 当前是否处于独占模式
    /// </summary>
    bool IsExclusive { get; }

    /// <summary>
    /// 处于可以后台运行的状态（原神窗口不处于激活状态）
    /// </summary>
    bool IsBackgroundRunning => false;

    /// <summary>
    /// 常驻触发器：跨任务存活，且不受界面分类门控。
    ///
    /// 默认 false —— 代表本触发器只服务于当前这次实时触发会话：
    /// 任务启动会清空实时触发器（<see cref="GameTaskManager.ClearTriggers"/>），
    /// 且长对话/大地图等界面稳定超过 30 秒后会被 UI 分类门控滤掉。
    ///
    /// 只有承担"游戏异常自动处理"这类职责、必须在任务运行期间继续工作的触发器才返回 true：
    /// 它必须留在触发器列表里，否则任务运行期间根本没有 OnCapture 驱动。
    ///
    /// **生命周期契约**（返回 true 就必须遵守）：
    /// * <see cref="GameTaskManager.ConvertToTriggerList"/> **不会**对它调用 <see cref="Init"/>，
    ///   所以实例的运行状态会跨任务边界保留——这正是常驻的意义；
    /// * <see cref="Init"/> 只由 <c>TaskTriggerDispatcher.Start</c> 在**实时触发会话启动时**调用一次，
    ///   因此实现必须把"回到干净初始状态"全部写进 <see cref="Init"/>，
    ///   并保证构造函数之后、<see cref="Init"/> 之前的默认值不会让 <see cref="OnCapture"/> 出错；
    /// * 由此推出的前提：<see cref="OnCapture"/> 的状态只会在截图调度线程上被读写
    ///   （<c>Init</c> 不在捕获循环内并发），实现里**不需要**为这些状态加锁。
    /// </summary>
    bool AlwaysActive => false;

    GameUiCategory SupportedGameUiCategory => GameUiCategory.Unknown;

    /// <summary>
    /// 初始化
    /// </summary>
    void Init();

    /// <summary>
    /// 捕获图像后操作
    /// </summary>
    /// <param name="content">捕获的图片等内容</param>
    void OnCapture(CaptureContent content);
}
