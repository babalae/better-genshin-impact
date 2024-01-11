namespace BetterGenshinImpact.GameTask.Model.Enum;

public enum DispatcherCaptureModeEnum
{
    // 仅触发器模式
    OnlyTrigger,

    // 仅捕获并缓存图像模式
    OnlyCacheCapture,

    // 捕获并缓存图像模式，并执行触发器
    CacheCaptureWithTrigger
}