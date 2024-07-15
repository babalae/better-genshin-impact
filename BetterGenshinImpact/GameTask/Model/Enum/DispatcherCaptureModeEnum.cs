namespace BetterGenshinImpact.GameTask.Model.Enum;

public enum DispatcherCaptureModeEnum
{
    // 正常运行调度器
    NormalTrigger,

    // 正常运行调度器，但不执行触发器，仅捕获并缓存图像模式
    OnlyCacheCapture,

    // 正常运行调度器，捕获并缓存图像模式，并执行触发器
    CacheCaptureWithTrigger,

    // --------------------------------------------
    // 下面两个模式无法直接设置，只能通过调度器的 StartTimer 和 StopTimer 方法来设置

    // 停止运行整个调度器
    Stop,

    // 启动整个调度器
    Start,

    // --------------------------------------------
}
