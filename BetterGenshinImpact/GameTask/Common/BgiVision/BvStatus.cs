using System;

namespace BetterGenshinImpact.GameTask.Common.BgiVision;

/// <summary>
/// 模仿OpenCv的静态类
/// 用于原神的各类识别与控制操作
///
/// 此处主要是对游戏内的一些状态进行识别
/// </summary>
static partial class Bv
{
    public static string WhichGameUi()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 是否在主界面
    /// </summary>
    /// <returns></returns>
    public static bool IsInMainUi(CaptureContent content)
    {
        return false;
    }
}
