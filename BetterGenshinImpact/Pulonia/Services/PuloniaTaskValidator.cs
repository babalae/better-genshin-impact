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
    /// 上限 32 位：生成的 ID 最长 24 位（运行 ID），收紧上限防止 ID 成为路径中的长段。
    /// 保留名检查不能删除：命令行传入的计划 ID 会直接拼成 plans/{id}.json 等文件路径。
    /// </summary>
    public static void ValidateId(string? id, string location)
    {
        if (id is null || !Regex.IsMatch(id, "^[a-z0-9][a-z0-9_-]{0,31}\\z")
            || Regex.IsMatch(id, "^(con|prn|aux|nul|com[0-9]|lpt[0-9])\\z"))
        {
            throw new PuloniaTaskValidationException(location, "ID 必须为 1—32 位小写字母、数字、下划线或短横线，且不能使用 Windows 保留名称。");
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
        if (plan.RootTask is not { TaskType: "group", Source: null, RepeatCount: null })
            throw new PuloniaTaskValidationException(location, "根节点必须是无来源引用、无重复配置的 group。");
        // 账号绑定、触发配置、共享设置等集合由 JSON 契约（Required.DisallowNull）和模型默认值保证非空，不再重复检查。

        // 相同 ID 不能出现在不同位置；同一对象挂在两个父节点下或形成循环时，第二次出现同样因 ID 重复被拒绝。
        var tasks = new Dictionary<string, PuloniaTask>(StringComparer.Ordinal);
        ValidateTask(plan.RootTask, location, tasks, 0);
        var accounts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var account in plan.Accounts)
        {
            if (account is null)
                throw new PuloniaTaskValidationException(location, "账号绑定不能为 null。");
            ValidateId(account.AccountId, location + "/accounts");
            if (!accounts.Add(account.AccountId))
                throw new PuloniaTaskValidationException(location, "账号不能重复绑定。");
        }
        var triggerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var trigger in plan.Triggers)
        {
            if (trigger is null || !triggerIds.Add(trigger.Id))
                throw new PuloniaTaskValidationException(location, "触发器不能为空或重复。");
            try { PuloniaTaskSchedule.Validate(trigger); }
            catch (Exception ex) { throw new PuloniaTaskValidationException(location + "/triggers", ex.Message); }
            if (trigger.TargetTaskId is { } target && !tasks.ContainsKey(target)
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
    /// 校验设置方案的身份和其中的共享设置。
    /// </summary>
    public static void ValidateProfile(PuloniaSettingsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateId(profile.Id, "profile.id");
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Revision < 0)
            throw new PuloniaTaskValidationException(profile.Id, "方案名称不能为空，修订号不能为负数。");
        ValidateSettings(profile.Settings, "profile/" + profile.Id + "/settings");
    }

    /// <summary>
    /// 校验一组共享设置：每个键都必须是目录中的设置项，值符合设置项的值类型。
    /// </summary>
    /// <param name="settings">节点或方案中的共享设置。</param>
    /// <param name="location">报错位置。</param>
    public static void ValidateSettings(JObject settings, string location)
    {
        ValidateJsonValue(settings, location);
        foreach (var field in settings.Properties())
        {
            if (!PuloniaSettingCatalog.TryGet(field.Name, out var definition))
                throw new PuloniaTaskValidationException(location + "/" + field.Name, "不存在的共享设置项。");
            ValidateParameters(field.Value, definition.ValueSchema, location + "/" + field.Name);
            if (definition.Key == PuloniaSettingCatalog.CollectionCooldownKey)
                PuloniaCollectionCooldown.ValidateSettings(field.Value, location + "/" + field.Name);
        }
    }

    /// <summary>
    /// 检查执行器类型说明的作用域与默认参数。
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
    /// 一次性检查一组能力说明：字段非空、作用域合法、类型与资源不重复、参数 Schema 只用受支持的子集。
    /// 能力说明来自执行器代码，只需在注册时或单元测试中校验，运行准备不再逐次复验。
    /// </summary>
    /// <param name="definitions">要注册的全部能力说明。</param>
    public static void ValidateDefinitions(IEnumerable<PuloniaTaskDefinition> definitions)
    {
        var keys = new HashSet<(string, string?)>();
        foreach (var definition in definitions)
        {
            if (definition is null || definition.ParameterSchema is null || definition.SettingBindings is null)
                throw new PuloniaTaskValidationException("definition", "类型说明不能为空。");
            var location = "definition/" + definition.TaskType;
            ValidateDefinitionScope(definition.TaskType, definition.ResourceId, definition.SchemaVersion,
                definition.DefaultParameters, location);
            if (!keys.Add((definition.TaskType, definition.ResourceId)))
                throw new PuloniaTaskValidationException(location, "类型和资源说明重复注册。");
            ValidateParameterSchema(definition.ParameterSchema, location);
            ValidateSettingBindings(definition, location);
        }
    }

    /// <summary>
    /// 检查能力的共享设置绑定：设置项存在，路径在默认参数中存在，且能力默认值等于设置项默认值，
    /// 保证“未设置”与能力默认行为一致；默认值不同的设置项不能绑定。
    /// </summary>
    private static void ValidateSettingBindings(PuloniaTaskDefinition definition, string location)
    {
        foreach (var (key, path) in definition.SettingBindings)
        {
            if (!PuloniaSettingCatalog.TryGet(key, out var setting))
                throw new PuloniaTaskValidationException(location, $"绑定了不存在的共享设置项 {key}。");
            JToken? current = definition.DefaultParameters;
            foreach (var segment in path.Split('.'))
                current = (current as JObject)?[segment];
            if (current is null)
                throw new PuloniaTaskValidationException(location, $"共享设置项 {key} 的参数路径 {path} 不存在。");
            if (!JToken.DeepEquals(current, setting.DefaultValue))
                throw new PuloniaTaskValidationException(location, $"共享设置项 {key} 的默认值与能力默认值不一致。");
        }
    }

    /// <summary>
    /// 校验有限超时、重试和失败行为。
    /// </summary>
    public static void ValidatePolicy(PuloniaTaskPolicy policy, string location)
    {
        // policy 由 JSON 契约（Required.DisallowNull）和模型默认值保证非空。
        if (policy.TimeoutSeconds is { } timeout && (!double.IsFinite(timeout) || timeout <= 0 || timeout > int.MaxValue / 1000d)
            || policy.RetryDelaySeconds is { } delay && (!double.IsFinite(delay) || delay < 0 || delay > int.MaxValue / 1000d)
            || policy.MaxRetries is < 0 or > 100
            || policy.FailureBehavior is not (null or "stop_plan" or "skip_group" or "continue"))
            throw new PuloniaTaskValidationException(location, "执行策略无效；超时必须有限且大于 0，重试次数为 0—100，失败行为须为已知值。");
    }

    /// <summary>
    /// 按支持的 Schema 子集检查有效参数。Schema 本身的合法性（关键字、类型名与约束格式）只由
    /// <see cref="ValidateParameterSchema"/> 检查：能力说明在注册时或单元测试中校验，其余 Schema 由代码构造。
    /// </summary>
    public static void ValidateParameters(JToken value, JObject schema, string location, int depth = 0)
    {
        if (depth > MaxTreeDepth)
            throw new PuloniaTaskValidationException(location, "参数嵌套过深。");

        // 1. 标量约束：类型、允许值和数值下限
        if (schema["type"] is { } type)
        {
            var types = type is JArray array ? array.Values<string>() : [type.Value<string>()];
            if (!types.Any(t => MatchesType(value, t!)))
                throw new PuloniaTaskValidationException(location, $"参数类型应为 {type.ToString(Newtonsoft.Json.Formatting.None)}，实际为 {value.Type}。");
        }
        if (schema["enum"] is JArray allowed && !allowed.Any(item => JToken.DeepEquals(item, value)))
            throw new PuloniaTaskValidationException(location, "参数不在允许值列表内。");
        if (schema["minimum"] is { } minimum && value.Type is JTokenType.Integer or JTokenType.Float
            && (double)value < (double)minimum)
            throw new PuloniaTaskValidationException(location, $"参数不能小于 {minimum.ToString(Newtonsoft.Json.Formatting.None)}。");

        // 2. 对象：必填字段、已声明字段递归检查、按需拒绝未声明字段
        if (value is JObject obj)
        {
            foreach (var required in (schema["required"] as JArray ?? new JArray()).Values<string>())
            {
                if (obj.Property(required!) is null)
                    throw new PuloniaTaskValidationException(location + "/" + required, "缺少必填参数。");
            }
            var properties = schema["properties"] as JObject;
            foreach (var field in obj.Properties())
            {
                if (properties?[field.Name] is JObject fieldSchema)
                    ValidateParameters(field.Value, fieldSchema, location + "/" + field.Name, depth + 1);
                else if (schema["additionalProperties"]?.Value<bool>() == false)
                    throw new PuloniaTaskValidationException(location + "/" + field.Name, "不允许未声明的参数。");
            }
        }

        // 3. 数组：逐项检查
        if (value is JArray items && schema["items"] is JObject itemSchema)
        {
            for (var i = 0; i < items.Count; i++)
                ValidateParameters(items[i], itemSchema, location + "/" + i, depth + 1);
        }
    }

    /// <summary>
    /// 预先检查全部 Schema 分支，未提供的可选字段也不能藏有未知约束；这是 Schema 合法性的唯一检查入口。
    /// 支持的关键字：type、properties、required、additionalProperties、items、enum、minimum。
    /// </summary>
    public static void ValidateParameterSchema(JObject schema, string location, int depth = 0)
    {
        if (depth > MaxTreeDepth)
            throw new PuloniaTaskValidationException(location, "参数 Schema 嵌套过深。");
        foreach (var property in schema.Properties())
        {
            if (property.Name is not ("type" or "properties" or "required" or "additionalProperties" or "items" or "enum" or "minimum"))
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
            || schema["required"] is { } required && (required is not JArray || required.Any(t => t.Type != JTokenType.String))
            || schema["minimum"] is { Type: not (JTokenType.Integer or JTokenType.Float) })
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
    /// 递归检查编辑树；先登记节点 ID 再递归，同一对象挂在多处或形成循环时第二次出现即因 ID 重复被拒绝，不会栈溢出。
    /// </summary>
    /// <param name="tasks">已检查的节点，按 ID 索引，供触发目标查找。</param>
    private static void ValidateTask(PuloniaTask task, string parent, Dictionary<string, PuloniaTask> tasks, int depth)
    {
        if (task is null || depth > MaxTreeDepth || tasks.Count >= MaxTreeNodes)
            throw new PuloniaTaskValidationException(parent, "树含空节点，或超过深度/数量上限。");
        ValidateId(task.Id, parent + "/id");
        var location = parent + "/" + task.Id;
        if (!tasks.TryAdd(task.Id, task) || string.IsNullOrWhiteSpace(task.Name) || !IsTaskType(task.TaskType))
            throw new PuloniaTaskValidationException(location, "节点 ID 重复（含同一节点挂在多处或形成循环），或名称/类型无效。");
        // 参数、子节点和共享设置集合由 JSON 契约（Required.DisallowNull）和模型默认值保证非空，不再重复检查。
        ValidateJsonValue(task.Parameters, location + "/parameters");
        // 叶子能否使用某个设置项取决于能力绑定，运行准备时检查；这里只检查设置项存在且值合法。
        ValidateSettings(task.Settings, location + "/settings");
        ValidatePolicy(task.Policy, location);
        if (task.ProfileId is not null)
            ValidateId(task.ProfileId, location + "/profile_id");
        if (task.ResourceVersion is not null
            && (task.TaskType is not ("javascript" or "pathing" or "keymouse")
                || !Regex.IsMatch(task.ResourceVersion, "^[0-9a-f]{64}\\z")))
            throw new PuloniaTaskValidationException(location,
                "只有资源任务可以保存 64 位小写 SHA-256 资源版本。");

        if (task.TaskType == "group"
            && (task.Parameters.Count != 0 || task.Path is not null || task.Resource is not null || task.ResourceVersion is not null))
            throw new PuloniaTaskValidationException(location,
                "分组通过共享设置向下传播配置，不使用任务参数、path 或资源版本。");

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
            ValidateTask(child, location, tasks, depth + 1);
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
