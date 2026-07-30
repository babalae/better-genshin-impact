using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoFight.Script;

/// <summary>
/// 跨战斗持久化的 E 技能冷却跟踪器。
/// 本身不负责识别——仅提供两个基础操作：
///   <list type="bullet">
///     <item><description><see cref="Record"/>：记录传入的 CD 值（纯存储，>0 才记录）</description></item>
///     <item><description><see cref="ApplyFallback"/>：根据角色预置配置进行兜底设置</description></item>
///   </list>
/// 由调用方（Avatar）自行决定何时调用兜底。
/// 可通过 <see cref="IsReady"/> / <see cref="GetRemainingCd"/> 查询任意角色的 E 技能状态，
/// 即使跨越多场战斗和地图追踪简易策略节点。
/// </summary>
public static class ESkillCdTracker
{
    /// <summary>特殊处理类别</summary>
    public enum FallbackType
    {
        /// <summary>无特殊处理，使用现有逻辑</summary>
        None = 0,
        /// <summary>OCR读不到CD时，直接设置为完整CD</summary>
        SetFull = 1,
        /// <summary>OCR读不到CD且已有剩余CD大于0时，取 min(剩余CD, 完整CD)</summary>
        MinRemaining = 2
    }

    /// <summary>角色名 → FallbackType 映射（硬编码名单）</summary>
    public static readonly Dictionary<string, FallbackType> CdFallbackMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["莉奈娅"] = FallbackType.SetFull,
        ["玛薇卡"] = FallbackType.MinRemaining,
    };

    /// <summary>角色名 → E 技能就绪的时间戳（UTC）</summary>
    private static readonly ConcurrentDictionary<string, DateTime> EReadyAt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>E 键检测防抖：短时间多次触发仅最后一次生效</summary>
    private static CancellationTokenSource? _debounceCts;
    private static readonly object _debounceLock = new();

    private static readonly ILogger Logger = App.GetLogger<ConditionEvaluator>(); // ESkillCdTracker 是静态类，不能用作泛型参数

    /// <summary>
    /// 防抖触发 E 技能 CD 检测。
    /// 短时间内的多次调用仅最后一次生效，延迟 200ms 后执行 <paramref name="ocrFunc"/> 并记录/兜底。
    /// </summary>
    /// <param name="ocrFunc">执行 OCR 的函数，返回 CD 秒数（&gt;0 为有效值）</param>
    /// <param name="characterName">角色名</param>
    /// <param name="ct">外部取消令牌</param>
    public static void TriggerECheck(Func<double> ocrFunc, string characterName, CancellationToken ct)
    {
        CancellationTokenSource? oldCts;
        CancellationTokenSource newCts;
        lock (_debounceLock)
        {
            oldCts = _debounceCts;
            _debounceCts = newCts = new CancellationTokenSource();
        }

        // 只取消旧 CTS（唤醒旧 Task），Dispose 由旧 Task 自身的 finally 处理
        try { oldCts?.Cancel(); } catch (ObjectDisposedException) { }

        var capturedCts = newCts;
        var debounceToken = capturedCts.Token;

        Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                // 等待 200ms 防抖窗口，有新触发或外部取消时提前退出
                if (debounceToken.WaitHandle.WaitOne(200)) return;

                ct.ThrowIfCancellationRequested();
                debounceToken.ThrowIfCancellationRequested();

                var cd = ocrFunc();

                ct.ThrowIfCancellationRequested();
                debounceToken.ThrowIfCancellationRequested();

                var recordedCd = Record(characterName, cd);
                if (recordedCd <= 0)
                {
                    recordedCd = ApplyFallback(characterName);
                }

                if (recordedCd > 0)
                {
                    Logger.LogInformation("{Name} 元素战技，cd:{Cooldown} 秒",
                        characterName, Math.Round(recordedCd, 2));
                }
                else
                {
                    Logger.LogWarning("{Name} 战技cd未更新", characterName);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                capturedCts.Dispose();
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// 记录传入的 CD 值。> 0 时才写入，否则忽略。
    /// </summary>
    /// <param name="characterName">角色名</param>
    /// <param name="cdSeconds">冷却秒数</param>
    /// <returns>实际记录的值（<= 0 表示未记录）</returns>
    public static double Record(string characterName, double cdSeconds)
    {
        if (string.IsNullOrEmpty(characterName) || cdSeconds <= 0) return 0;
        EReadyAt[characterName] = DateTime.UtcNow.AddSeconds(cdSeconds);
        return cdSeconds;
    }

    /// <summary>
    /// 根据 <see cref="CdFallbackMap"/> 兜底设置 CD。
    /// 返回实际记录的 CD 值，无兜底或兜底失败时返回 0。
    /// </summary>
    internal static double ApplyFallback(string characterName, bool log = true)
    {
        if (!CdFallbackMap.TryGetValue(characterName, out var fallbackType)) return 0;

        if (!DefaultAutoFightConfig.CombatAvatarMap.TryGetValue(characterName, out var cfg)) return 0;

        var cfgCd = cfg.SkillCd > 0 ? cfg.SkillCd : cfg.SkillHoldCd;
        if (cfgCd <= 0) return 0;

        if (fallbackType == FallbackType.SetFull)
        {
            Record(characterName, cfgCd);
            if (log)
                Logger.LogInformation("{Name} 点按元素战技，cd:{Cd}秒（兜底设置）", characterName, Math.Round(cfgCd, 2));
            return cfgCd;
        }
        else if (fallbackType == FallbackType.MinRemaining)
        {
            var remaining = GetRemainingCd(characterName);
            var actual = remaining > 0 ? Math.Min(remaining, cfgCd) : cfgCd;
            EReadyAt[characterName] = DateTime.UtcNow.AddSeconds(actual);
            if (log)
                Logger.LogInformation("{Name} 点按元素战技，cd:{Cd}秒（兜底设置）", characterName, Math.Round(actual, 2));
            return actual;
        }

        return 0;
    }

    /// <summary>
    /// 获取指定角色 E 技能的剩余冷却秒数。
    /// 无记录（从未释放过或已过期）时返回 0（表示就绪）。
    /// </summary>
    public static double GetRemainingCd(string characterName)
    {
        if (string.IsNullOrEmpty(characterName)) return 0;
        if (EReadyAt.TryGetValue(characterName, out var readyAt))
        {
            var remaining = (readyAt - DateTime.UtcNow).TotalSeconds;
            return remaining > 0 ? remaining : 0;
        }

        return 0;
    }

    /// <summary>
    /// 判断指定角色 E 技能是否就绪。
    /// </summary>
    public static bool IsReady(string characterName)
    {
        return GetRemainingCd(characterName) <= 0;
    }

    /// <summary>
    /// 清除所有角色的 E 技能冷却记录（用于调试或重置）。
    /// </summary>
    public static void Clear()
    {
        EReadyAt.Clear();
    }
}
