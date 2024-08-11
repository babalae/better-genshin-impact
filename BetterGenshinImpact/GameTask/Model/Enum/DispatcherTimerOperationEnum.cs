namespace BetterGenshinImpact.GameTask.Model.Enum;

/// <summary>
/// 存在触发器运行的情况下，优先使用触发器的缓存图像
/// </summary>
public enum DispatcherTimerOperationEnum
{
    // 关闭实时触发器，自己主动获取图像
    UseSelfCaptureImage,

    // 使用实时触发器的缓存图模式,但是不执行触发器
    UseCacheImage,

    // 使用实时触发器的缓存图模式
    UseCacheImageWithTrigger,

    // 不做任何操作
    None
}
