using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 编辑、读档、复制和运行准备共用的模型校验。
/// </summary>
public static class PuloniaTaskValidator
{
    /// <summary>
    /// 持久化树的深度上限；运行准备还会检查引用展开后的深度。
    /// </summary>
    public const int MaxTreeDepth = 64;

    /// <summary>
    /// 单个计划允许的最大节点数。
    /// </summary>
    public const int MaxTreeNodes = 10000;

    /// <summary>
    /// 校验可以安全用于 Windows 文件名和运行地址的稳定 ID。
    /// </summary>
    public static void ValidateId(string? id, string location)
    {
        if (id is null || !Regex.IsMatch(id, "^[a-z0-9][a-z0-9_-]{0,95}\\z")
            || Regex.IsMatch(id, "^(con|prn|aux|nul|com[0-9]|lpt[0-9])\\z"))
        {
            throw new PuloniaTaskValidationException(location, "ID 必须为 1—96 位小写字母、数字、下划线或短横线，且不能使用 Windows 保留名称。");
        }
    }

    /// <summary>
    /// 校验计划格式、树所有权、稳定身份、账号绑定和类型结构。
    /// </summary>
    public static void ValidatePlan(PuloniaTaskPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateId(plan.Id, "plan.id");
        var location = $"plan/{plan.Id}";
        if (plan.SchemaVersion != PuloniaTaskPlan.CurrentSchemaVersion)
            throw new PuloniaTaskValidationException(location, $"不支持格式版本 {plan.SchemaVersion}。");
        if (plan.Revision < 0 || string.IsNullOrWhiteSpace(plan.Name))
            throw new PuloniaTaskValidationException(location, "修订号不能为负数，名称不能为空。");
        if (plan.RootTask is null || plan.RootTask.TaskType != "group" || plan.RootTask.Source is not null
            || plan.RootTask.RepeatCount is not null)
            throw new PuloniaTaskValidationException(location, "根节点必须是无来源引用、无重复配置的 group。");
        if (plan.Accounts is null || plan.Triggers is null)
            throw new PuloniaTaskValidationException(location, "账号绑定和触发配置不能为 null。");

        // 同一对象不能挂在两个父节点下；相同 ID 也不能出现在不同位置。
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var objects = new HashSet<PuloniaTask>(ReferenceEqualityComparer.Instance);
        ValidateTask(plan.RootTask, location, ids, objects, 0);
        var accounts = new HashSet<string>(StringComparer.Ordinal);
        var tasksById = objects.ToDictionary(task => task.Id, StringComparer.Ordinal);
        foreach (var account in plan.Accounts)
        {
            if (account is null)
                throw new PuloniaTaskValidationException(location, "账号绑定不能为 null。");
            ValidateId(account.AccountId, location + "/accounts");
            if (!accounts.Add(account.AccountId) || account.PresetSelections is null)
                throw new PuloniaTaskValidationException(location, "账号不能重复绑定，预设选择不能为 null。");
            foreach (var (taskId, presetId) in account.PresetSelections)
            {
                if (!ids.Contains(taskId))
                    throw new PuloniaTaskValidationException(location, $"账号预设指向不存在的节点 {taskId}。");
                if (!IsLeafType(tasksById[taskId].TaskType))
                    throw new PuloniaTaskValidationException(location, $"账号预设不能配置在控制节点 {taskId}。");
                ValidateId(presetId, location + "/accounts/preset_selections");
            }
        }
        var triggerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var trigger in plan.Triggers)
        {
            if (trigger is null || !triggerIds.Add(trigger.Id))
                throw new PuloniaTaskValidationException(location, "触发器不能为空或重复。");
            try { PuloniaTaskSchedule.Validate(trigger); }
            catch (Exception ex) { throw new PuloniaTaskValidationException(location + "/triggers", ex.Message); }
            if (trigger.TargetTaskId is { } target && !ids.Contains(target)
                || trigger.AccountId is { } accountId && !accounts.Contains(accountId))
                throw new PuloniaTaskValidationException(location, "触发器指向不存在的节点或未绑定的账号。");
        }
    }

    /// <summary>
    /// 检查包括禁用节点在内的全部计划依赖，并返回本次固定的计划集合。
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, PuloniaTaskPlan>> ValidatePlanReferencesAsync(
        PuloniaTaskPlan plan, Func<string, CancellationToken, Task<PuloniaTaskPlan?>> loadPlan, CancellationToken ct)
    {
        ValidatePlan(plan);
        var plans = new Dictionary<string, PuloniaTaskPlan>(StringComparer.Ordinal) { [plan.Id] = plan };
        var stack = new HashSet<string>(StringComparer.Ordinal) { plan.Id };
        var nodeCount = 0;
        await VisitAsync(plan.RootTask, plan.Id, 0).ConfigureAwait(false);
        return plans;

        // 所有引用在保存和准备时使用同一校验，避免先保存坏引用、启用后才发现循环。
        async Task VisitAsync(PuloniaTask task, string parent, int depth)
        {
            ct.ThrowIfCancellationRequested();
            var address = parent + "/" + task.Id;
            if (depth > MaxTreeDepth || ++nodeCount > MaxTreeNodes)
                throw new PuloniaTaskValidationException(address, "计划依赖超过深度或节点数量上限。");
            if (task.Source is { Kind: "plan" })
            {
                var targetId = task.Source!.PlanId!;
                if (!stack.Add(targetId))
                    throw new PuloniaTaskValidationException(address, $"计划引用形成循环：{targetId}。");
                try
                {
                    if (!plans.TryGetValue(targetId, out var target))
                    {
                        target = await loadPlan(targetId, ct).ConfigureAwait(false)
                                 ?? throw new PuloniaTaskValidationException(address, $"引用计划 {targetId} 不存在。");
                        ValidatePlan(target);
                        plans.Add(targetId, target);
                    }
                    await VisitAsync(target.RootTask, address + "/@" + targetId, depth + 1).ConfigureAwait(false);
                }
                finally
                {
                    stack.Remove(targetId);
                }
            }
            foreach (var child in task.Children)
                await VisitAsync(child, address, depth + 1).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 校验共享预设的身份和参数作用域。
    /// </summary>
    public static void ValidatePreset(PuloniaTaskPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ValidateId(preset.Id, "preset.id");
        if (string.IsNullOrWhiteSpace(preset.Name) || preset.Revision < 0)
            throw new PuloniaTaskValidationException(preset.Id, "预设名称不能为空，修订号不能为负数。");
        ValidateScope(preset.TaskType, preset.ResourceId, preset.SchemaVersion, preset.Values, preset.Id);
    }

    /// <summary>
    /// 检查参数作用域；JS 自定义参数必须绑定资源，宿主公共设置可以跨资源复用。
    /// </summary>
    public static void ValidateScope(string type, string? resourceId, int version, JObject? values, string location)
    {
        if (!IsLeafType(type) || version <= 0 || values is null)
            throw new PuloniaTaskValidationException(location, "参数作用域必须有具体能力类型、正整数版本和对象参数。");
        if (resourceId is not null && string.IsNullOrWhiteSpace(resourceId)
            || type == "javascript" && resourceId is null
                && (values.Count == 0 || values.Properties().Any(property => !PuloniaTaskCommonSettings.IsPathingParameter(property.Name))))
            throw new PuloniaTaskValidationException(location, "资源标识不能为空；JS 自定义参数必须绑定具体资源。");
        if (type == "javascript" && resourceId is null)
            ValidateParameters(values, new JObject
            {
                ["type"] = "object",
                ["properties"] = PuloniaTaskCommonSettings.CreateSchemaProperties("javascript"),
                ["additionalProperties"] = false
            }, location + "/values");
        ValidateJsonValue(values, location + "/values");
    }

    /// <summary>
    /// 检查执行器类型说明；通用 JS 能力可不绑定资源，具体预设仍由 <see cref="ValidateScope"/> 严格校验。
    /// </summary>
    public static void ValidateDefinitionScope(string type, string? resourceId, int version, JObject? values, string location)
    {
        if (!IsLeafType(type) || version <= 0 || values is null)
            throw new PuloniaTaskValidationException(location, "能力说明必须有具体任务类型、正整数版本和对象参数。");
        if (resourceId is not null && string.IsNullOrWhiteSpace(resourceId))
            throw new PuloniaTaskValidationException(location, "能力说明的资源标识不能为空字符串。");
        ValidateJsonValue(values, location + "/values");
    }

    /// <summary>
    /// 校验有限超时、重试和失败行为。
    /// </summary>
    public static void ValidatePolicy(PuloniaTaskPolicy? policy, string location)
    {
        if (policy is null
            || policy.TimeoutSeconds is { } timeout && (!double.IsFinite(timeout) || timeout <= 0 || timeout > int.MaxValue / 1000d)
            || policy.RetryDelaySeconds is { } delay && (!double.IsFinite(delay) || delay < 0 || delay > int.MaxValue / 1000d)
            || policy.MaxRetries is < 0 or > 100
            || policy.FailureBehavior is not (null or "stop_plan" or "skip_group" or "continue"))
            throw new PuloniaTaskValidationException(location, "执行策略无效；超时必须有限且大于 0，重试次数为 0—100，失败行为须为已知值。");
    }

    /// <summary>
    /// 按支持的 Schema 子集检查有效参数，未知关键字明确拒绝。
    /// </summary>
    public static void ValidateParameters(JToken value, JObject schema, string location, int depth = 0)
    {
        if (depth > MaxTreeDepth)
            throw new PuloniaTaskValidationException(location, "参数嵌套过深。");
        foreach (var property in schema.Properties())
        {
            if (property.Name is not ("type" or "properties" or "required" or "additionalProperties" or "items" or "enum"))
                throw new PuloniaTaskValidationException(location, $"原型尚不支持 Schema 关键字 {property.Name}。");
        }

        if (schema["type"] is { } type)
        {
            var types = type is JArray array ? array.ToArray() : [type];
            if (types.Length == 0 || types.Any(t => t.Type != JTokenType.String || !IsParameterType(t.Value<string>()!)))
                throw new PuloniaTaskValidationException(location, "Schema type 必须是已知类型或非空类型数组。");
            if (!types.Any(t => MatchesType(value, t.Value<string>()!)))
                throw new PuloniaTaskValidationException(location, $"参数类型应为 {type.ToString(Newtonsoft.Json.Formatting.None)}，实际为 {value.Type}。");
        }
        if (schema["enum"] is { } choices)
        {
            if (choices is not JArray allowed || allowed.Count == 0)
                throw new PuloniaTaskValidationException(location, "Schema enum 必须是非空数组。");
            if (!allowed.Any(item => JToken.DeepEquals(item, value)))
                throw new PuloniaTaskValidationException(location, "参数不在允许值列表内。");
        }

        var properties = schema["properties"] as JObject;
        if (schema["properties"] is not null && properties is null
            || schema["additionalProperties"] is { Type: not JTokenType.Boolean }
            || schema["required"] is { } requiredValue && (requiredValue is not JArray || requiredValue.Any(t => t.Type != JTokenType.String))
            || schema["items"] is not null and not JObject)
            throw new PuloniaTaskValidationException(location, "Schema 对象或数组约束格式无效。");

        if (value is JObject obj)
        {
            foreach (var required in (schema["required"] as JArray ?? new JArray()).Values<string>())
            {
                if (obj.Property(required!) is null)
                    throw new PuloniaTaskValidationException(location + "/" + required, "缺少必填参数。");
            }
            foreach (var field in obj.Properties())
            {
                if (properties?[field.Name] is { } fieldSchema)
                {
                    if (fieldSchema is not JObject childSchema)
                        throw new PuloniaTaskValidationException(location, "字段 Schema 必须是对象。");
                    ValidateParameters(field.Value, childSchema, location + "/" + field.Name, depth + 1);
                }
                else if (schema["additionalProperties"]?.Value<bool>() == false)
                    throw new PuloniaTaskValidationException(location + "/" + field.Name, "不允许未声明的参数。");
            }
        }
        if (value is JArray items && schema["items"] is JObject itemSchema)
        {
            for (var i = 0; i < items.Count; i++)
                ValidateParameters(items[i], itemSchema, location + "/" + i, depth + 1);
        }
    }

    /// <summary>
    /// 预先检查全部 Schema 分支，未提供的可选字段也不能藏有未知约束。
    /// </summary>
    public static void ValidateParameterSchema(JObject schema, string location, int depth = 0)
    {
        if (depth > MaxTreeDepth)
            throw new PuloniaTaskValidationException(location, "参数 Schema 嵌套过深。");
        foreach (var property in schema.Properties())
        {
            if (property.Name is not ("type" or "properties" or "required" or "additionalProperties" or "items" or "enum"))
                throw new PuloniaTaskValidationException(location, $"原型尚不支持 Schema 关键字 {property.Name}。");
        }
        if (schema["type"] is { } type)
        {
            var types = type is JArray array ? array.ToArray() : [type];
            if (types.Length == 0 || types.Any(t => t.Type != JTokenType.String || !IsParameterType(t.Value<string>()!)))
                throw new PuloniaTaskValidationException(location, "Schema type 无效。");
        }
        if (schema["enum"] is not null && (schema["enum"] is not JArray choices || choices.Count == 0)
            || schema["additionalProperties"] is { Type: not JTokenType.Boolean }
            || schema["required"] is { } required && (required is not JArray || required.Any(t => t.Type != JTokenType.String)))
            throw new PuloniaTaskValidationException(location, "Schema 约束格式无效。");
        if (schema["properties"] is { } properties)
        {
            if (properties is not JObject fields)
                throw new PuloniaTaskValidationException(location, "Schema properties 必须是对象。");
            foreach (var field in fields.Properties())
            {
                if (field.Value is not JObject child)
                    throw new PuloniaTaskValidationException(location + "/" + field.Name, "字段 Schema 必须是对象。");
                ValidateParameterSchema(child, location + "/" + field.Name, depth + 1);
            }
        }
        if (schema["items"] is { } items)
        {
            if (items is not JObject child)
                throw new PuloniaTaskValidationException(location, "Schema items 必须是对象。");
            ValidateParameterSchema(child, location + "/items", depth + 1);
        }
    }

    /// <summary>
    /// 只接受标准 JSON 数据；非有限数值及 Date/Undefined 等扩展值不能悄悄改变类型。
    /// </summary>
    private static void ValidateJsonValue(JToken token, string location, int depth = 0)
    {
        if (depth > MaxTreeDepth)
            throw new PuloniaTaskValidationException(location, "JSON 数据嵌套过深。");
        if (token is JObject obj)
        {
            foreach (var field in obj.Properties())
                ValidateJsonValue(field.Value, location + "/" + field.Name, depth + 1);
        }
        else if (token is JArray array)
        {
            for (var i = 0; i < array.Count; i++)
                ValidateJsonValue(array[i], location + "/" + i, depth + 1);
        }
        else if (token.Type is not (JTokenType.String or JTokenType.Boolean or JTokenType.Integer or JTokenType.Float or JTokenType.Null)
                 || token is JValue { Value: double number } && !double.IsFinite(number)
                 || token is JValue { Value: float single } && !float.IsFinite(single))
            throw new PuloniaTaskValidationException(location, "参数必须为标准 JSON 数据，数值必须有限。");
    }

    /// <summary>
    /// 递归检查编辑树；先检查对象所有权，避免循环导致栈溢出。
    /// </summary>
    private static void ValidateTask(PuloniaTask task, string parent, HashSet<string> ids, HashSet<PuloniaTask> objects, int depth)
    {
        if (task is null || depth > MaxTreeDepth || objects.Count >= MaxTreeNodes || !objects.Add(task))
            throw new PuloniaTaskValidationException(parent, "树含空节点、对象循环、重复父节点，或超过深度/数量上限。");
        ValidateId(task.Id, parent + "/id");
        var location = parent + "/" + task.Id;
        if (!ids.Add(task.Id) || string.IsNullOrWhiteSpace(task.Name) || !IsTaskType(task.TaskType))
            throw new PuloniaTaskValidationException(location, "节点 ID 重复，或名称/类型无效。");
        if (task.Parameters is null || task.Children is null || task.ParameterOverrides is null)
            throw new PuloniaTaskValidationException(location, "参数、子节点和公共参数不能为 null。");
        ValidateJsonValue(task.Parameters, location + "/parameters");
        ValidatePolicy(task.Policy, location);
        if (task.PresetId is not null)
            ValidateId(task.PresetId, location + "/preset_id");
        if (task.ResourceVersion is not null
            && (task.TaskType is not ("javascript" or "pathing" or "keymouse")
                || !Regex.IsMatch(task.ResourceVersion, "^[0-9a-f]{64}\\z")))
            throw new PuloniaTaskValidationException(location,
                "只有资源任务可以保存 64 位小写 SHA-256 资源版本。");

        if (task.TaskType == "group")
        {
            if (task.Parameters.Count != 0 || task.PresetId is not null || task.Path is not null || task.Resource is not null
                || task.ResourceVersion is not null)
                throw new PuloniaTaskValidationException(location,
                    "分组使用 parameter_overrides 提供公共参数，不使用叶子参数、预设、path 或资源版本。");
        }
        else if (task.ParameterOverrides.Count != 0)
            throw new PuloniaTaskValidationException(location, "公共参数只能配置在 group 节点。");

        var scopes = new HashSet<(string, string?, int)>();
        foreach (var item in task.ParameterOverrides)
        {
            if (item is null)
                throw new PuloniaTaskValidationException(location, "公共参数项不能为 null。");
            ValidateScope(item.TaskType, item.ResourceId, item.SchemaVersion, item.Values, location);
            if (!scopes.Add((item.TaskType, item.ResourceId, item.SchemaVersion)))
                throw new PuloniaTaskValidationException(location, "同一节点不能重复配置相同参数作用域。");
        }

        if (task.Source is { } source)
        {
            if (task.Children.Count != 0)
                throw new PuloniaTaskValidationException(location, "引用来源与可编辑子节点互斥。");
            if (task.TaskType == "group" && source.Kind == "plan")
            {
                ValidateId(source.PlanId, location + "/source/plan_id");
                if (source.Path is not null || source.Resource is not null || source.TaskType is not null || source.Version is not null)
                    throw new PuloniaTaskValidationException(location, "计划引用不能包含目录来源字段。");
            }
            else if (task.TaskType == "group" && source.Kind == "directory")
            {
                if ((string.IsNullOrWhiteSpace(source.Path) && source.Resource is null) || source.TaskType is not ("pathing" or "keymouse") || source.PlanId is not null)
                    throw new PuloniaTaskValidationException(location, "目录引用需指定 pathing/keymouse 类型和非空路径。");
                if (source.Version is not null && !Regex.IsMatch(source.Version, "^[0-9a-f]{64}\\z"))
                    throw new PuloniaTaskValidationException(location, "目录引用版本必须是 64 位小写 SHA-256。");
            }
            else
                throw new PuloniaTaskValidationException(location, "来源 kind 与节点类型不匹配。");
        }

        if (task.TaskType == "group")
        {
            if (task.RepeatCount is < 1 or > MaxTreeNodes)
                throw new PuloniaTaskValidationException(location, "分组执行次数必须为 1—10000。");
        }
        else if (task.RepeatCount is not null)
            throw new PuloniaTaskValidationException(location, "只有分组可以配置执行次数。");

        if (IsLeafType(task.TaskType) && (task.Children.Count != 0 || task.Source is not null))
            throw new PuloniaTaskValidationException(location, "具体任务不能带子节点或引用来源。");
        if (task.TaskType is "javascript" or "pathing" or "keymouse" && string.IsNullOrWhiteSpace(task.Path) && task.Resource is null)
            throw new PuloniaTaskValidationException(location, "资源任务必须填写 path。");

        // 新引用必须明确仓库和版本；旧 path 模型保持兼容，但禁止两个定位同时生效。
        if (task.Resource is { } resource)
        {
            if (task.Path is not null || task.TaskType is not ("javascript" or "pathing" or "keymouse"))
                throw new PuloniaTaskValidationException(location, "仓库资源不能同时配置旧路径或绑定到非资源任务。");
            ValidateRepositoryResourceType(resource, task.TaskType, false, location);
        }
        if (task.Source?.Resource is { } directoryResource)
        {
            if (task.Source.Path is not null)
                throw new PuloniaTaskValidationException(location, "仓库目录引用不能同时配置旧路径。");
            ValidateRepositoryResourceType(directoryResource, task.Source.TaskType!, true, location);
        }

        foreach (var child in task.Children)
            ValidateTask(child, location, ids, objects, depth + 1);
    }

    /// <summary>校验仓库资源类型的根路径，防止把其他能力的文件误当作路线或 JS 项目。</summary>
    private static void ValidateRepositoryResourceType(BetterGenshinImpact.Core.Script.Repositories.ScriptResourceReference resource,
        string taskType, bool directory, string location)
    {
        try { resource.Validate(); }
        catch (ArgumentException ex) { throw new PuloniaTaskValidationException(location, ex.Message, ex); }
        var prefix = taskType == "javascript" ? "js" : taskType;
        if (!(directory && resource.RelativePath == prefix)
            && !resource.RelativePath.StartsWith(prefix + "/", StringComparison.Ordinal))
            throw new PuloniaTaskValidationException(location, "资源位置与任务类型不匹配。");
        if (!directory && taskType != "javascript" && !resource.RelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new PuloniaTaskValidationException(location, "路线和录制资源必须引用 JSON 文件。");
    }

    /// <summary>
    /// 判断具体能力类型，不把控制节点作为参数作用域。
    /// </summary>
    private static bool IsLeafType(string? type) => IsTaskType(type) && type != "group";

    /// <summary>
    /// 任务类型使用稳定的小写机器标识；计划来源和分组执行次数不属于任务类型。
    /// </summary>
    private static bool IsTaskType(string? type) => type is not null && type is not ("repeat" or "plan")
                                                                  && Regex.IsMatch(type, "^[a-z][a-z0-9_.-]{0,95}\\z");

    /// <summary>
    /// 判断原型支持的参数类型名称。
    /// </summary>
    private static bool IsParameterType(string type) => type is "object" or "array" or "string" or "boolean" or "integer" or "number" or "null";

    /// <summary>
    /// 按 JSON 类型判断，不把字符串转换为布尔值或数值。
    /// </summary>
    private static bool MatchesType(JToken value, string type) => type switch
    {
        "object" => value.Type == JTokenType.Object,
        "array" => value.Type == JTokenType.Array,
        "string" => value.Type == JTokenType.String,
        "boolean" => value.Type == JTokenType.Boolean,
        "integer" => value.Type == JTokenType.Integer,
        "number" => value.Type is JTokenType.Integer or JTokenType.Float,
        "null" => value.Type == JTokenType.Null,
        _ => false
    };
}
