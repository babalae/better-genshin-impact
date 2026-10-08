using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.Core.Script.Group;

/// <summary>
/// 执行计划里的一个脚本。重复执行（<c>RunNum</c>）已经折算进预估耗时。
/// </summary>
public sealed record ScriptGroupProgressStep(string GroupName, string Name, double EstimatedSeconds);

/// <summary>
/// 进度计算结果
/// </summary>
/// <param name="Progress">当前配置组内的进度百分比</param>
/// <param name="GroupRemainingSeconds">当前配置组的预计剩余秒数</param>
/// <param name="TotalRemainingSeconds">本次执行（含后续配置组）的预计总剩余秒数</param>
public readonly record struct ScriptGroupProgressResult(
    double Progress,
    double GroupRemainingSeconds,
    double TotalRemainingSeconds);

/// <summary>
/// 一次执行的脚本计划与进度计算（纯逻辑，不依赖 WPF，可单测）。
/// <para>
/// 一次执行可以覆盖多个配置组（连续执行 / 一条龙 / Worker 连续执行），预估按脚本顺序累加；
/// 预计剩余时间 = 还没开始执行的脚本预估之和 + 当前脚本的剩余预估。
/// 因此每跑完（或被跳过）一个脚本，剩余时间会立刻按剩下的脚本重算，
/// 而不是「整组固定总时长 - 已用时间」一直背着前面脚本的误差。
/// </para>
/// </summary>
public sealed class ScriptGroupProgressPlan
{
    /// <summary>
    /// 当前脚本超出预估时，每次补的时长。脚本跑超了就按这个粒度继续补，
    /// 保证倒计时永远是正数，而不是卡在 <c>0:00</c> 不动。
    /// </summary>
    public const double OverrunExtensionSeconds = 10;

    private readonly List<ScriptGroupProgressStep> _steps = [];

    /// <summary>当前配置组在计划中的区间（闭开区间）</summary>
    private int _groupStart;

    private int _groupEnd;

    /// <summary>当前正在执行的脚本下标；等于 <see cref="_groupEnd"/> 表示当前配置组已全部结束</summary>
    private int _index;

    public bool HasSteps => _steps.Count > 0;

    public double TotalEstimatedSeconds => _steps.Sum(step => step.EstimatedSeconds);

    /// <summary>是否还有没结束的脚本（含当前正在执行的）</summary>
    public bool HasPendingSteps => _index < _steps.Count;

    public void Reset()
    {
        _steps.Clear();
        _groupStart = 0;
        _groupEnd = 0;
        _index = 0;
    }

    /// <summary>把一批脚本追加到计划末尾，用于预置后续配置组的预估</summary>
    public void Append(IEnumerable<ScriptGroupProgressStep> steps)
    {
        _steps.AddRange(steps);
    }

    /// <summary>从游标开始是否还有该配置组的脚本</summary>
    public bool ContainsPendingGroup(string groupName)
    {
        for (var i = _index; i < _steps.Count; i++)
        {
            if (_steps[i].GroupName == groupName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 进入一个配置组。计划里已经有该组时，用实际要执行的脚本替换它原有的预估条目
    /// （继续执行 / 跳过规则会让实际列表和预估列表不一致），否则追加到末尾。
    /// 游标移到该组第一个脚本。
    /// </summary>
    public void StartGroup(string groupName, IReadOnlyList<ScriptGroupProgressStep> steps)
    {
        var start = -1;
        for (var i = _index; i < _steps.Count; i++)
        {
            if (_steps[i].GroupName == groupName)
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            start = _steps.Count;
            _steps.AddRange(steps);
        }
        else
        {
            var end = start;
            while (end < _steps.Count && _steps[end].GroupName == groupName)
            {
                end++;
            }

            _steps.RemoveRange(start, end - start);
            _steps.InsertRange(start, steps);
        }

        _groupStart = start;
        _groupEnd = start + steps.Count;
        _index = start;
    }

    /// <summary>
    /// 当前配置组里的第 index 个脚本开始执行。它之前的脚本一律视为已结束
    /// （被跳过、被禁用的脚本不会单独上报），于是它们立刻退出「预计剩余时间」。
    /// </summary>
    public void StartStep(int index)
    {
        var target = _groupStart + index;
        if (target > _index && target < _steps.Count)
        {
            _index = target;
        }
    }

    /// <summary>当前配置组结束，游标推进到该组之后</summary>
    public void EndGroup()
    {
        if (_groupEnd > _index)
        {
            _index = _groupEnd;
        }
    }

    /// <summary>
    /// 给当前正在执行的脚本补时（如触发了回血，路线要重试）。
    /// 补的是当前脚本的预估，因此「预计剩余时间」与「预计总剩余时间」同时增加；
    /// 本组总预估也会变大，进度条不回退由调用方保证。
    /// </summary>
    public void AddExtraSecondsToCurrentStep(double seconds)
    {
        if (seconds <= 0 || _index < _groupStart || _index >= _groupEnd || _index >= _steps.Count)
        {
            return;
        }

        var step = _steps[_index];
        _steps[_index] = step with { EstimatedSeconds = step.EstimatedSeconds + seconds };
    }

    /// <param name="currentStepElapsedSeconds">当前脚本已经执行的时间</param>
    public ScriptGroupProgressResult Calculate(double currentStepElapsedSeconds)
    {
        // 超时保护：当前脚本跑超了预估就按 10 秒粒度继续补，还超就继续补。
        // 用 >= 判断，保证倒计时始终是正数：正好跑满时也会再补一轮，而不是显示 0:00 停住。
        // 补时只加在当前脚本上，因此脚本结束后剩余时间会按后面的脚本重新算。
        var currentEstimate = GetCurrentStepEstimate();
        while (currentEstimate > 0 && currentStepElapsedSeconds >= currentEstimate)
        {
            currentEstimate += OverrunExtensionSeconds;
            _steps[_index] = _steps[_index] with { EstimatedSeconds = currentEstimate };
        }

        var groupTotal = 0.0;
        for (var i = _groupStart; i < _groupEnd && i < _steps.Count; i++)
        {
            groupTotal += _steps[i].EstimatedSeconds;
        }

        var currentStepElapsed = Math.Min(currentStepElapsedSeconds, currentEstimate);

        // 当前配置组：已结束的脚本 + 当前脚本已消耗的部分
        var groupDone = currentStepElapsed;
        for (var i = _groupStart; i < _index && i < _steps.Count; i++)
        {
            groupDone += _steps[i].EstimatedSeconds;
        }

        // 当前配置组剩余 = 本组还没开始的脚本预估 + 当前脚本的剩余预估
        var groupRemaining = Math.Max(0, currentEstimate - currentStepElapsedSeconds);
        for (var i = Math.Max(_index + 1, _groupStart); i < _groupEnd && i < _steps.Count; i++)
        {
            groupRemaining += _steps[i].EstimatedSeconds;
        }

        // 后续配置组的预估：本组之后的全部条目，起点就是本组的结束位置
        var laterGroupsRemaining = 0.0;
        for (var i = _groupEnd; i < _steps.Count; i++)
        {
            laterGroupsRemaining += _steps[i].EstimatedSeconds;
        }

        var progress = groupTotal > 0 ? Math.Min(100, groupDone / groupTotal * 100) : 0;
        return new ScriptGroupProgressResult(progress, groupRemaining, groupRemaining + laterGroupsRemaining);
    }

    /// <summary>当前脚本的预估（含已经补过的时间）；游标不在任何脚本上时返回 0</summary>
    private double GetCurrentStepEstimate()
        => _index >= _groupStart && _index < _groupEnd && _index < _steps.Count
            ? _steps[_index].EstimatedSeconds
            : 0;

    /// <summary>计划里是否还有当前配置组之后的脚本（决定要不要显示「预计总剩余时间」）</summary>
    public bool HasLaterGroups => _groupEnd < _steps.Count;

    /// <summary>计划里是否包含多个配置组</summary>
    public bool HasMultipleGroups
    {
        get
        {
            for (var i = 1; i < _steps.Count; i++)
            {
                if (_steps[i].GroupName != _steps[0].GroupName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
