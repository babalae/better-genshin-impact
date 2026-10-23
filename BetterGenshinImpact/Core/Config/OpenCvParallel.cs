using System;
using BetterGenshinImpact.GameTask;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Config;

/// <summary>
///     OpenCV 并行行为的集中控制：
///     - 线程数上限：限制 OpenMP 池宽度，减少与游戏的抢核（0 = 全部逻辑核）；
///     - 空闲睡眠：OpenMP 工作线程在并行区域之间默认自旋等待（SwitchToThread 空转），
///       游戏占核时每次空转都计入 CPU，实测单线程每分钟级会话可白烧 40s+；
///       OMP_WAIT_POLICY=PASSIVE 让其立即睡眠，代价是每次唤醒多几十微秒。
///     环境变量必须在 vcomp140.dll 首次加载前设置，因此只在启动时调用 <see cref="ApplyStartup"/>；
///     线程数可随时通过 <see cref="ApplyThreads"/> 热更新。
/// </summary>
public static class OpenCvParallel
{
    public static void ApplyStartup(HardwareAccelerationConfig config)
    {
        if (config.OpenCvIdleSleep)
        {
            Environment.SetEnvironmentVariable("OMP_WAIT_POLICY", "PASSIVE");
        }

        ApplyThreads(config.OpenCvThreads);
    }

    public static void ApplyThreads(int threads)
    {
        if (threads > 0)
        {
            Cv2.SetNumThreads(threads);
        }
    }
}
