using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Executors;

/// <summary>
/// 调用显式注册委托的进程内 C# 任务执行器。
/// </summary>
public sealed class PuloniaCSharpTaskExecutor : IPuloniaTaskExecutor
{
    /// <summary>
    /// 已注册 C# 操作的查找入口。
    /// </summary>
    private readonly PuloniaCSharpTaskRegistry _registry;

    /// <inheritdoc />
    public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; } = [new()
    {
        TaskType = "csharp",
        DisplayName = "进程内 C#",
        Description = "调用显式注册的进程内 C# 操作，不通过反射猜测方法。",
        DefaultParameters = new JObject
        {
            ["operation"] = "sample.sum",
            ["values"] = new JArray(1, 2, 3),
            ["delay_milliseconds"] = 0
        },
        ParameterSchema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["operation"] = new JObject { ["type"] = "string" },
                ["values"] = new JObject
                {
                    ["type"] = "array",
                    ["items"] = new JObject { ["type"] = "number" }
                },
                ["delay_milliseconds"] = new JObject { ["type"] = "integer" }
            },
            ["required"] = new JArray("operation"),
            ["additionalProperties"] = true
        },
        PublicParameters = ["operation", "values", "delay_milliseconds", "event_key", "units", "occurred_at"],
        AvailabilityRules =
        [
            new PuloniaTaskAvailabilityRule
            {
                RuleId = "daily",
                EffectKey = "sample.ledger.daily",
                Kind = PuloniaTaskAvailabilityKind.ResetQuota,
                Scope = PuloniaTaskScopeKind.Account,
                ResetPeriod = PuloniaTaskResetPeriod.Daily,
                Quota = 1,
                Operation = "sample.ledger.daily"
            },
            new PuloniaTaskAvailabilityRule
            {
                RuleId = "weekly",
                EffectKey = "sample.ledger.weekly",
                Kind = PuloniaTaskAvailabilityKind.ResetQuota,
                Scope = PuloniaTaskScopeKind.World,
                ResetPeriod = PuloniaTaskResetPeriod.Weekly,
                Quota = 1,
                Operation = "sample.ledger.weekly"
            },
            new PuloniaTaskAvailabilityRule
            {
                RuleId = "monthly",
                EffectKey = "sample.ledger.monthly",
                Kind = PuloniaTaskAvailabilityKind.ResetQuota,
                Scope = PuloniaTaskScopeKind.Account,
                ResetPeriod = PuloniaTaskResetPeriod.Monthly,
                Quota = 1,
                Operation = "sample.ledger.monthly"
            },
            new PuloniaTaskAvailabilityRule
            {
                RuleId = "rolling",
                EffectKey = "sample.ledger.rolling",
                Kind = PuloniaTaskAvailabilityKind.RollingCooldown,
                Scope = PuloniaTaskScopeKind.World,
                CooldownSeconds = 60,
                Operation = "sample.ledger.rolling"
            },
            new PuloniaTaskAvailabilityRule
            {
                RuleId = "partial",
                EffectKey = "sample.ledger.partial",
                Kind = PuloniaTaskAvailabilityKind.ResetQuota,
                Scope = PuloniaTaskScopeKind.Account,
                ResetPeriod = PuloniaTaskResetPeriod.Daily,
                Quota = 1,
                Operation = "sample.ledger.partial"
            }
        ]
    }];

    /// <summary>
    /// 使用显式操作注册表建立执行器。
    /// </summary>
    public PuloniaCSharpTaskExecutor(PuloniaCSharpTaskRegistry registry)
    {
        _registry = registry;
    }

    /// <inheritdoc />
    public Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task,
        PuloniaTaskExecutionContext context, CancellationToken ct)
    {
        var parameters = task.Parameters;
        var operation = parameters.Value<string>("operation");
        if (string.IsNullOrWhiteSpace(operation))
            return Task.FromResult(PuloniaTaskOutcome.Failure("未指定进程内 C# 操作。"));
        if (!_registry.TryGet(operation, out var handler) || handler is null)
            return Task.FromResult(PuloniaTaskOutcome.Failure($"未注册进程内 C# 操作 {operation}。"));
        return handler(context, parameters, ct);
    }
}
