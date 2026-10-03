using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 集中计算账号/世界作用域、滚动 CD 与服务器日历额度。
/// </summary>
internal static class PuloniaTaskAvailability
{
    /// <summary>
    /// 建立不依赖计划名和节点 ID 的稳定作用域键。
    /// </summary>
    public static string BuildScopeKey(PuloniaTaskRequest request, PuloniaTaskScopeKind scope)
    {
        var identity = scope == PuloniaTaskScopeKind.World
            ? request.WorldOwnerAccountId ?? request.AccountId ?? "default"
            : request.AccountId ?? "default";
        return $"{request.Server.Trim().ToLowerInvariant()}/{scope.ToString().ToLowerInvariant()}/{identity}";
    }

    /// <summary>
    /// 返回当前节点实际生效的能力规则。
    /// </summary>
    public static IReadOnlyList<PuloniaTaskAvailabilityRule> GetApplicableRules(
        PuloniaTaskDefinition definition, PuloniaTaskPreparedTask task)
        => definition.AvailabilityRules.Where(rule => rule.AppliesTo(task.Parameters)).ToArray();

    /// <summary>
    /// 在执行前检查未决操作、滚动 CD 与周期额度。
    /// </summary>
    public static PuloniaTaskAvailabilityDecision Evaluate(PuloniaTaskRequest request,
        IReadOnlyList<PuloniaTaskAvailabilityRule> rules, IReadOnlyList<PuloniaTaskLedgerEntry> ledger,
        IReadOnlyList<PuloniaTaskOperationIntent> uncertainOperations, DateTimeOffset now)
    {
        foreach (var rule in rules)
        {
            if (rule.Kind == PuloniaTaskAvailabilityKind.Unrestricted)
                continue;
            var scopeKey = BuildScopeKey(request, rule.Scope);
            var uncertain = uncertainOperations.FirstOrDefault(intent => intent.Reservations.Any(reservation =>
                reservation.ScopeKey == scopeKey && reservation.EffectKey == rule.EffectKey));
            if (uncertain is not null)
                return new PuloniaTaskAvailabilityDecision(false, true, null,
                    $"资源 {rule.EffectKey} 存在未核验操作（运行 {uncertain.RunId:D}），不能自动重放。");

            var matching = ledger.Where(item => item.ScopeKey == scopeKey && item.EffectKey == rule.EffectKey).ToArray();
            if (rule.Kind == PuloniaTaskAvailabilityKind.RollingCooldown)
            {
                var latest = matching.OrderByDescending(item => item.OccurredAt).FirstOrDefault();
                if (latest?.NextEligibleAt is { } next && next > now)
                    return new PuloniaTaskAvailabilityDecision(false, false, next,
                        $"资源 {rule.EffectKey} 仍在冷却，{next.ToLocalTime():yyyy-MM-dd HH:mm:ss} 后可执行。");
                continue;
            }

            var window = GetWindow(rule, request.ServerUtcOffsetMinutes, now);
            var used = matching.Where(item => item.WindowKey == window.Key).Sum(item => item.Units);
            if (used >= rule.Quota)
                return new PuloniaTaskAvailabilityDecision(false, false, window.End,
                    $"资源 {rule.EffectKey} 的{GetPeriodName(rule.ResetPeriod)}额度已用完（{used}/{rule.Quota}），"
                    + $"{window.End.ToLocalTime():yyyy-MM-dd HH:mm:ss} 后重置。");
        }

        return new PuloniaTaskAvailabilityDecision(true, false, null, "当前资源可执行。");
    }

    /// <summary>
    /// 将确认事件转换为可幂等保存的账本项。
    /// </summary>
    public static PuloniaTaskLedgerEntry CreateLedgerEntry(PuloniaTaskRequest request,
        PuloniaTaskAvailabilityRule rule, PuloniaTaskCompletionEvent completionEvent, string eventKey,
        Guid runId, string taskAddress)
    {
        var window = rule.Kind == PuloniaTaskAvailabilityKind.ResetQuota
            ? GetWindow(rule, request.ServerUtcOffsetMinutes, completionEvent.OccurredAt).Key
            : "rolling";
        DateTimeOffset? nextEligible = rule.Kind == PuloniaTaskAvailabilityKind.RollingCooldown
            ? completionEvent.OccurredAt.AddSeconds(rule.CooldownSeconds!.Value)
            : null;
        return new PuloniaTaskLedgerEntry
        {
            EventKey = eventKey,
            ScopeKey = BuildScopeKey(request, rule.Scope),
            EffectKey = rule.EffectKey,
            WindowKey = window,
            Units = completionEvent.Units,
            OccurredAt = completionEvent.OccurredAt.ToUniversalTime(),
            NextEligibleAt = nextEligible?.ToUniversalTime(),
            TaskRunId = $"{runId:N}:{taskAddress}",
            Evidence = completionEvent.Evidence
        };
    }

    /// <summary>
    /// 为执行前操作意图固定本次规则作用域。
    /// </summary>
    public static List<PuloniaTaskRuleReservation> CreateReservations(PuloniaTaskRequest request,
        IEnumerable<PuloniaTaskAvailabilityRule> rules)
        => rules.Where(rule => rule.Kind != PuloniaTaskAvailabilityKind.Unrestricted)
            .Select(rule => new PuloniaTaskRuleReservation
            {
                RuleId = rule.RuleId,
                ScopeKey = BuildScopeKey(request, rule.Scope),
                EffectKey = rule.EffectKey
            }).ToList();

    /// <summary>
    /// 校验规则字段，避免无效周期产生永不释放的额度。
    /// </summary>
    public static void ValidateRules(IEnumerable<PuloniaTaskAvailabilityRule> rules, string path)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.RuleId) || !ids.Add(rule.RuleId))
                throw new PuloniaTaskValidationException(path, "可用性规则 ID 不能为空或重复。");
            if (string.IsNullOrWhiteSpace(rule.EffectKey))
                throw new PuloniaTaskValidationException(path, "可用性规则的资源键不能为空。");
            if (rule.ResetHour is < 0 or > 23)
                throw new PuloniaTaskValidationException(path, "服务器重置小时必须在 0—23 之间。");
            if (rule.Kind == PuloniaTaskAvailabilityKind.ResetQuota
                && (rule.ResetPeriod is null || rule.Quota <= 0))
                throw new PuloniaTaskValidationException(path, "周期额度必须指定周期和正数额度。");
            if (rule.Kind == PuloniaTaskAvailabilityKind.RollingCooldown
                && (rule.CooldownSeconds is null or <= 0 || !double.IsFinite(rule.CooldownSeconds.Value)))
                throw new PuloniaTaskValidationException(path, "滚动 CD 必须指定有限的正数秒数。");
        }
    }

    /// <summary>
    /// 计算服务器时区和重置小时下的稳定窗口键与结束时间。
    /// </summary>
    private static (string Key, DateTimeOffset End) GetWindow(PuloniaTaskAvailabilityRule rule,
        int offsetMinutes, DateTimeOffset instant)
    {
        var offset = TimeSpan.FromMinutes(offsetMinutes);
        var shifted = instant.ToOffset(offset).AddHours(-rule.ResetHour);
        DateTime startDate;
        DateTime endDate;
        switch (rule.ResetPeriod)
        {
            case PuloniaTaskResetPeriod.Daily:
                startDate = shifted.Date;
                endDate = startDate.AddDays(1);
                break;
            case PuloniaTaskResetPeriod.Weekly:
                var daysAfterMonday = ((int)shifted.DayOfWeek + 6) % 7;
                startDate = shifted.Date.AddDays(-daysAfterMonday);
                endDate = startDate.AddDays(7);
                break;
            case PuloniaTaskResetPeriod.Monthly:
                startDate = new DateTime(shifted.Year, shifted.Month, 1);
                endDate = startDate.AddMonths(1);
                break;
            default:
                throw new PuloniaTaskValidationException(rule.RuleId, "周期额度缺少有效重置周期。");
        }

        var start = new DateTimeOffset(startDate.AddHours(rule.ResetHour), offset).ToUniversalTime();
        var end = new DateTimeOffset(endDate.AddHours(rule.ResetHour), offset).ToUniversalTime();
        return (start.ToString("O", CultureInfo.InvariantCulture), end);
    }

    /// <summary>
    /// 返回面向用户的周期名称。
    /// </summary>
    private static string GetPeriodName(PuloniaTaskResetPeriod? period) => period switch
    {
        PuloniaTaskResetPeriod.Daily => "每日",
        PuloniaTaskResetPeriod.Weekly => "每周",
        PuloniaTaskResetPeriod.Monthly => "每月",
        _ => "周期"
    };
}

/// <summary>
/// 一次执行前可用性检查结果。
/// </summary>
internal sealed class PuloniaTaskAvailabilityDecision
{
    /// <summary>
    /// 当前是否允许执行。
    /// </summary>
    public bool IsAllowed { get; }

    /// <summary>
    /// 是否因未决副作用而需要人工核验。
    /// </summary>
    public bool NeedsAttention { get; }

    /// <summary>
    /// 已知的下次可执行时间。
    /// </summary>
    public DateTimeOffset? NextEligibleAt { get; }

    /// <summary>
    /// 面向用户的判断原因。
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 建立一次可用性判断结果。
    /// </summary>
    public PuloniaTaskAvailabilityDecision(bool isAllowed, bool needsAttention,
        DateTimeOffset? nextEligibleAt, string message)
    {
        IsAllowed = isAllowed;
        NeedsAttention = needsAttention;
        NextEligibleAt = nextEligibleAt;
        Message = message;
    }
}
