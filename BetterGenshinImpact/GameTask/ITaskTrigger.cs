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
    /// 常驻触发器：跨任务存活，且在「异常恢复挂起」期间继续运行。
    ///
    /// 默认 false —— 代表本触发器只服务于当前这次实时触发会话：
    /// 任务启动会清空实时触发器（<see cref="TaskTriggerDispatcher.ClearTriggers"/>），
    /// 挂起期间也会被调度器跳过。
    ///
    /// 只有承担解除挂起职责、且必须在任务运行期间继续工作的触发器
    /// （异常弹窗处理、断网看门狗）才返回 true：
    /// 它们必须留在触发器列表里，否则任务运行期间没有任何 OnCapture 驱动，
    /// 检测与自动恢复会整体失效。
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
