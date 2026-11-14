using System;
using System.IO;
using System.Threading;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OpenCv.TemplateMatch;

/// <summary>
/// 把「小地图预处理结果」落盘，用于排查「模板匹配永远失败」。
///
/// 为什么需要它：模板匹配失败时日志只会说「粗匹配结果不唯一 / 得分过低」，
/// 但从外部完全看不到**真正喂进匹配的是什么东西**。而失败原因恰恰分成两大类，
/// 且处理方式完全相反：
///   1) 小地图本身没内容（未探索雾区、被 UI 遮住、画面过平）—— 换图层无用，
///      必须改地图追踪策略或等玩家探索；
///   2) 小地图有内容但图层对不上（尺度/内容/坐标错）—— 需要补或重做图层。
/// 没有落盘就只能猜。这里只在 DEBUG 构建生效，且每次进程运行最多存
/// <see cref="MaxDumps"/> 组，不会持续刷盘。
///
/// 输出目录：&lt;程序目录&gt;\log\screenshot\minimap\
/// </summary>
internal static class MiniMapDump
{
    /// <summary>每次进程运行最多落盘的组数。8 组足够覆盖一次失败过程的几个阶段。</summary>
    private const int MaxDumps = 8;

    private static int _count;
    private static string? _dir;
    private static bool _dirFailed;

    /// <summary>
    /// 领取本次落盘编号。返回 0 表示已达上限，调用方应跳过全部 <see cref="Save"/>。
    /// </summary>
    public static int Begin()
    {
#if DEBUG
        var n = Interlocked.Increment(ref _count);
        return n <= MaxDumps ? n : 0;
#else
        return 0;
#endif
    }

    /// <summary>
    /// 保存一张图。浮点图（mask/32F 归一化结果）会先拉伸到 0~255 再存，
    /// 否则 imwrite 对 CV_32F 支持不全，会静默产出空白或直接抛异常。
    /// </summary>
    public static void Save(int id, string name, Mat mat)
    {
#if DEBUG
        if (id <= 0 || mat is null || mat.Empty())
        {
            return;
        }

        try
        {
            var dir = EnsureDir();
            if (dir == null)
            {
                return;
            }

            var path = Path.Combine(dir, $"{id:00}_{name}.png");
            if (mat.Depth() == MatType.CV_32F || mat.Depth() == MatType.CV_64F)
            {
                using var norm = new Mat();
                Cv2.Normalize(mat, norm, 0, 255, NormTypes.MinMax);
                using var u8 = new Mat();
                norm.ConvertTo(u8, MatType.CV_8U);
                Cv2.ImWrite(path, u8);
            }
            else
            {
                Cv2.ImWrite(path, mat);
            }
        }
        catch
        {
            // 纯排查用途，任何失败都不允许影响主流程
        }
#endif
    }

    private static string? EnsureDir()
    {
        if (_dir != null || _dirFailed)
        {
            return _dir;
        }

        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "log", "screenshot", "minimap");
            Directory.CreateDirectory(dir);
            _dir = dir;
        }
        catch
        {
            _dirFailed = true;
            _dir = null;
        }

        return _dir;
    }
}
