using OpenCvSharp;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.Model;

/// <summary>
/// 独立任务基类：提供所属运行环境的画面信息
/// </summary>
/// <param name="info">所属运行环境的画面信息（GameRuntime.SystemInfo）</param>
public class BaseIndependentTask(ISystemInfo info)
{
    /// <summary>
    /// 所属运行环境的画面信息
    /// </summary>
    protected ISystemInfo Info { get; } = info ?? throw new System.ArgumentNullException(nameof(info));

    /// <summary>
    /// 捕获区域（大于 1080P 时缩放到 1080P）
    /// </summary>
    protected Rect CaptureRect => Info.ScaleMax1080PCaptureRect;

    /// <summary>
    /// 以 1080P 为标准的素材缩放比例
    /// </summary>
    protected double AssetScale => Info.AssetScale;
}
