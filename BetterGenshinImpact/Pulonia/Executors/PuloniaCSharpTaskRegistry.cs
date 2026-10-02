using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Executors;

/// <summary>
/// 进程内 C# 任务处理委托，显式接收上下文、参数和取消令牌。
/// </summary>
public delegate Task<PuloniaTaskOutcome> PuloniaCSharpTaskHandler(
    PuloniaTaskExecutionContext context, JObject parameters, CancellationToken ct);

/// <summary>
/// 保存显式注册的进程内 C# 操作，避免按字符串反射程序集和重载。
/// </summary>
public sealed class PuloniaCSharpTaskRegistry
{
    /// <summary>
    /// 受注册门保护的操作表。
    /// </summary>
    private readonly Dictionary<string, PuloniaCSharpTaskHandler> _handlers = new(StringComparer.Ordinal);

    /// <summary>
    /// 串行化运行期间的动态注册和查询。
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// 建立注册表并加入不接触游戏资源的验收计算样例。
    /// </summary>
    public PuloniaCSharpTaskRegistry()
    {
        Register("sample.sum", ExecuteSampleSumAsync);
    }

    /// <summary>
    /// 注册一个稳定操作名；重复名称明确失败，防止静默替换正在使用的能力。
    /// </summary>
    public void Register(string operation, PuloniaCSharpTaskHandler handler)
    {
        if (string.IsNullOrWhiteSpace(operation))
            throw new ArgumentException("C# 操作名不能为空。", nameof(operation));
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            if (!_handlers.TryAdd(operation, handler))
                throw new InvalidOperationException($"C# 操作 {operation} 已注册。");
        }
    }

    /// <summary>
    /// 查找指定操作的固定处理委托。
    /// </summary>
    internal bool TryGet(string operation, out PuloniaCSharpTaskHandler? handler)
    {
        lock (_gate)
            return _handlers.TryGetValue(operation, out handler);
    }

    /// <summary>
    /// 对一组数字求和；可选延时用于验收取消和快照隔离，不启动游戏或截图器。
    /// </summary>
    private static async Task<PuloniaTaskOutcome> ExecuteSampleSumAsync(
        PuloniaTaskExecutionContext context, JObject parameters, CancellationToken ct)
    {
        var delayMilliseconds = parameters.Value<int?>("delay_milliseconds") ?? 0;
        if (delayMilliseconds is < 0 or > 600000)
            return PuloniaTaskOutcome.Failure("delay_milliseconds 必须在 0—600000 之间。");
        if (delayMilliseconds > 0)
            await Task.Delay(delayMilliseconds, ct).ConfigureAwait(false);

        var values = parameters["values"] as JArray ?? new JArray();
        decimal sum;
        try
        {
            sum = values.Values<decimal>().Sum();
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
        {
            return PuloniaTaskOutcome.Failure("values 必须全部是可求和的有限数字：" + ex.Message);
        }

        return PuloniaTaskOutcome.Success($"计算完成，合计为 {sum}。", new JObject
        {
            ["sum"] = sum,
            ["value_count"] = values.Count,
            ["run_id"] = context.RunId.ToString("D")
        });
    }
}
