using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Executors;

/// <summary>
/// 把独立任务注册表接入任务计划：注册表中带 Plan 范围的任务都成为任务计划能力，任务类型写作 solo.{TaskId}。
/// 参数在节点 JSON 与强类型参数之间按 SoloTaskJson 约定转换，结果原样映射为节点结果。
/// </summary>
public sealed class PuloniaSoloTaskExecutor : IPuloniaTaskExecutor
{
    /// <summary>
    /// 独立任务在任务计划中的类型前缀。
    /// </summary>
    public const string TaskTypePrefix = "solo.";

    /// <summary>
    /// 每周安排参数名：按服务器时间凌晨四点切换日期，当天的字段覆盖任务参数，enabled=false 表示当天跳过。
    /// </summary>
    public const string WeekdayOverridesParameter = "weekday_overrides";

    /// <summary>
    /// 独立任务注册表。
    /// </summary>
    private readonly SoloTaskRegistry _registry;

    /// <summary>
    /// 创建任务实例时解析其余依赖的服务容器。
    /// </summary>
    private readonly IServiceProvider _services;

    /// <summary>
    /// 按任务类型索引的能力定义。
    /// </summary>
    private readonly Dictionary<string, PuloniaTaskDefinition> _definitions;

    /// <inheritdoc />
    public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; }

    /// <summary>
    /// 从注册表建立执行器与全部能力定义。
    /// </summary>
    /// <param name="registry">独立任务注册表。</param>
    /// <param name="services">服务容器。</param>
    public PuloniaSoloTaskExecutor(SoloTaskRegistry registry, IServiceProvider services)
    {
        _registry = registry;
        _services = services;
        Definitions = registry.GetByScope(SoloTaskScopes.Plan).Select(BuildDefinition).ToList().AsReadOnly();
        _definitions = Definitions.ToDictionary(d => d.TaskType, StringComparer.Ordinal);
    }

    /// <summary>
    /// 把独立任务标识转换为任务计划的任务类型。
    /// </summary>
    /// <param name="taskId">独立任务标识。</param>
    /// <returns>任务类型。</returns>
    public static string ToTaskType(string taskId) => TaskTypePrefix + taskId;

    /// <summary>
    /// 判断任务类型是否为独立任务。
    /// </summary>
    /// <param name="taskType">任务类型。</param>
    /// <returns>以 solo. 开头时返回 true。</returns>
    public static bool IsSoloTaskType(string? taskType) => taskType?.StartsWith(TaskTypePrefix, StringComparison.Ordinal) == true;

    /// <summary>
    /// 从任务类型取出独立任务标识。
    /// </summary>
    /// <param name="taskType">任务类型。</param>
    /// <returns>独立任务标识；不是独立任务时返回 null。</returns>
    public static string? ToTaskId(string? taskType) => IsSoloTaskType(taskType) ? taskType![TaskTypePrefix.Length..] : null;

    /// <summary>
    /// 判断能力是否有需要在创建时填写的参数；每周安排不算。没有参数的独立任务可以跳过创建对话框直接插入。
    /// </summary>
    /// <param name="definition">能力定义。</param>
    /// <returns>存在每周安排以外的参数时返回 true。</returns>
    public static bool HasEditableParameters(PuloniaTaskDefinition definition)
        => definition.ParameterSchema["properties"] is JObject properties
           && properties.Properties().Any(p => p.Name != WeekdayOverridesParameter);

    /// <inheritdoc />
    public async Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task, PuloniaTaskExecutionContext context,
        CancellationToken ct)
    {
        if (!_registry.TryGet(ToTaskId(task.TaskType), out var descriptor)
            || !_definitions.TryGetValue(task.TaskType, out var definition))
        {
            return PuloniaTaskOutcome.Failure($"未注册独立任务 {task.TaskType}。");
        }

        // 1. 每周安排：按服务器四点日切取当天配置，只合并本节点的参数，不修改计划
        var values = (JObject)task.Parameters.DeepClone();
        if (values[WeekdayOverridesParameter] is JObject weekly
            && weekly[((int)ServerTimeHelper.GetServerTimeNow().AddHours(-4).DayOfWeek).ToString()] is JObject today)
        {
            if (today.Value<bool?>("enabled") == false)
            {
                return new PuloniaTaskOutcome(PuloniaTaskOutcomeKind.Skipped, "今天不在此任务的运行日期内。",
                    reasonCode: SoloTaskReasons.NotScheduledToday);
            }

            foreach (var field in today.Properties().Where(p => p.Name != "enabled"))
            {
                values[field.Name] = field.Value.DeepClone();
            }

            PuloniaTaskValidator.ValidateParameters(values, definition.ParameterSchema, task.TaskType);
        }

        values.Remove(WeekdayOverridesParameter);

        // 2. 任务只使用本节点租约记录的运行环境；它已停止时当前节点失败
        if (!context.TryGetRuntime(out var runtime, out var runtimeError))
        {
            return PuloniaTaskOutcome.Failure(runtimeError, reasonCode: SoloTaskReasons.RuntimeStopped);
        }

        // 3. 节点 JSON → 强类型参数 → 运行任务
        var param = descriptor.DeserializeParam(values.ToString(Formatting.None));
        ISoloTaskResult result;
        try
        {
            result = await descriptor.InvokeAsync(_services, runtime, param, ct).ConfigureAwait(false);
        }
        catch (NormalEndException e) when (!ct.IsCancellationRequested)
        {
            // 运行环境停止引起的正常结束：当前节点失败，下一节点重新准备运行环境
            return PuloniaTaskOutcome.Failure(e.Message, reasonCode: SoloTaskReasons.RuntimeStopped);
        }
        catch (NormalEndException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        ct.ThrowIfCancellationRequested();
        return ToOutcome(result);
    }

    /// <summary>
    /// 把独立任务结果映射为节点结果，附加数据按 SoloTaskJson 约定写入 Data。
    /// </summary>
    /// <param name="result">独立任务结果。</param>
    /// <returns>节点结果。</returns>
    private static PuloniaTaskOutcome ToOutcome(ISoloTaskResult result)
    {
        var kind = result.Kind switch
        {
            SoloTaskResultKind.Succeeded => PuloniaTaskOutcomeKind.Succeeded,
            SoloTaskResultKind.Unverified => PuloniaTaskOutcomeKind.ExecutedUnverified,
            SoloTaskResultKind.Partial => PuloniaTaskOutcomeKind.PartiallySucceeded,
            SoloTaskResultKind.Skipped => PuloniaTaskOutcomeKind.Skipped,
            _ => PuloniaTaskOutcomeKind.Failed
        };
        var data = result.Data is null or NoData
            ? null
            : PuloniaTaskJson.Read<JObject>(SoloTaskJson.Serialize(result.Data, result.Data.GetType()));
        var message = string.IsNullOrEmpty(result.Message) ? kind.ToString() : result.Message;
        return new PuloniaTaskOutcome(kind, message, data, reasonCode: result.ReasonCode);
    }

    /// <summary>
    /// 由独立任务描述符建立能力定义：默认参数来自参数类的无参构造，Schema 按参数 JSON 合同生成。
    /// </summary>
    /// <param name="descriptor">独立任务描述符。</param>
    /// <returns>能力定义。</returns>
    private static PuloniaTaskDefinition BuildDefinition(SoloTaskDescriptor descriptor)
    {
        var defaults = PuloniaTaskJson.Read<JObject>(descriptor.SerializeParam(descriptor.CreateDefaultParam()));
        // 材料目标是独立任务页的专属设置，任务计划节点不继承全局配置，避免节点改动首领或秘境后目标不匹配
        if (defaults["target_items"] is not null)
        {
            defaults["target_items"] = new JArray();
        }

        var schema = PuloniaSoloTaskSchema.Create(descriptor.ParamType);
        var properties = schema["properties"] as JObject ?? new JObject();
        // 每周安排是任务计划的编排能力，所有独立任务都可以按星期覆盖参数或跳过
        properties[WeekdayOverridesParameter] = new JObject { ["type"] = "object" };
        schema["properties"] = properties;
        schema["additionalProperties"] = false;
        return new PuloniaTaskDefinition
        {
            TaskType = ToTaskType(descriptor.TaskId),
            DisplayName = descriptor.DisplayName,
            Description = string.IsNullOrWhiteSpace(descriptor.Description)
                ? $"执行独立任务“{descriptor.DisplayName}”。"
                : descriptor.Description,
            RequiresGameSession = true,
            DefaultParameters = defaults,
            ParameterSchema = schema,
            PublicParameters = properties.Properties().Select(p => p.Name).ToArray(),
            IsSoloTask = true,
            Group = descriptor.Group,
            Order = descriptor.Order,
            OneDragonOrder = descriptor.OneDragonOrder
        };
    }
}
