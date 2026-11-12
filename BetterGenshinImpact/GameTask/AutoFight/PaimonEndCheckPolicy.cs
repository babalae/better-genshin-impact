using System;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 派蒙辅助检测的决策结果
/// </summary>
internal enum PaimonCheckDecision
{
    /// <summary>派蒙头像已消失（编队界面已打开），继续走黄条判定</summary>
    ProceedToBarCheck,

    /// <summary>派蒙头像可见，说明按 L 未生效，重按一次 L 后复检</summary>
    ReopenPartyScreen
}

/// <summary>
/// 派蒙辅助检测的决策逻辑（从 AutoFightTask.CheckFightFinish 中抽离，便于单元测试）。
///
/// 设计要点：派蒙头像可见只能推出"按 L 未生效、编队界面未打开"，不能直接推出"战斗未结束"。
/// 原实现把"派蒙可见"当成不可推翻的否定判决并立即提前返回，一旦出现误判
/// （模板误匹配、截图取到按 L 之前的旧帧等），战斗结束检查就会被无限次拦截，
/// 自动战斗永远无法结束，日志反复输出"派蒙头像可见，提前跳出战斗结束检查"。
///
/// 因此这里把"派蒙可见"降级为一次可复检的信号：重按 L 后再看，
/// 重试次数用尽仍可见时也必须继续走黄条判定兜底，保证检测一定收敛。
/// </summary>
internal static class PaimonEndCheckPolicy
{
    /// <summary>
    /// 派蒙可见时最多重按 L 的次数。超过后不再重试，直接进入黄条判定，
    /// 保证"派蒙始终被判为可见"这种异常情形不会无限阻断战斗结束检查。
    /// </summary>
    internal const int MaxReopenAttempts = 1;

    /// <summary>
    /// 派蒙检测延时下限（毫秒）。游戏内键鼠响应有 30~60ms 延迟，截图来源图本身也有延迟，
    /// 等待时间过短会取到按 L 之前的旧帧，从而把"界面没打开"误判为"派蒙可见"。
    /// </summary>
    internal const int MinDelayMs = 150;

    /// <summary>
    /// 派蒙检测延时上限（毫秒）。上限过大时等待本身就变成了界面等待，
    /// 会真的把编队界面等出来，反而破坏黄条判定。
    /// </summary>
    internal const int MaxDelayMs = 400;

    /// <summary>
    /// 把配置中的派蒙检测延时（毫秒）约束到支持范围
    /// </summary>
    internal static int NormalizeDelayMs(int configuredMs)
    {
        return Math.Clamp(configuredMs, MinDelayMs, MaxDelayMs);
    }

    /// <summary>
    /// 派出派蒙检测期间还需要补足的等待时间（毫秒）。
    /// 派蒙分支自身已经等待了一部分时间，这里只补足到 detectDelayTime，
    /// 保证总等待时长仍由 detectDelayTime 控制，不会因为重按复检把延时拖长。
    /// </summary>
    /// <param name="detectDelayTime">配置的检测前总延时（毫秒）</param>
    /// <param name="waitedMs">派蒙分支已经等待的时长（毫秒）</param>
    internal static int RemainingDelayMs(int detectDelayTime, int waitedMs)
    {
        return Math.Max(0, detectDelayTime - waitedMs);
    }

    /// <summary>
    /// 根据派蒙头像可见性与已重按次数给出后续动作
    /// </summary>
    /// <param name="paimonVisible">左上角派蒙头像是否可见</param>
    /// <param name="reopenAttempts">本次检查中已经重按 L 的次数</param>
    internal static PaimonCheckDecision Decide(bool paimonVisible, int reopenAttempts)
    {
        if (!paimonVisible)
        {
            return PaimonCheckDecision.ProceedToBarCheck;
        }

        // 派蒙可见说明按 L 未生效：还有复检配额时重按一次 L，否则交给黄条判定兜底
        return reopenAttempts < MaxReopenAttempts
            ? PaimonCheckDecision.ReopenPartyScreen
            : PaimonCheckDecision.ProceedToBarCheck;
    }
}
