using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 集中展开引用、解析参数和固定资源版本的运行准备原型，不触碰游戏环境。
/// </summary>
public sealed class PuloniaTaskBuilder
{
    /// <summary>
    /// 读取引用计划和共享预设的同一存储入口。
    /// </summary>
    private readonly PuloniaTaskStore _store;

    /// <summary>
    /// 使用存储建立准备入口。
    /// </summary>
    public PuloniaTaskBuilder(PuloniaTaskStore store)
    {
        _store = store;
    }

    /// <summary>
    /// 先固定编辑模型和准备选项，再在后台生成只读树。
    /// </summary>
    public Task<PuloniaTaskSnapshot> BuildAsync(PuloniaTaskPlan plan, PuloniaTaskBuildOptions options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(options);
        if (options.Definitions is null || options.PathVariables is null || options.ParameterOverrides is null
            || string.IsNullOrWhiteSpace(options.BaseDirectory))
            throw new PuloniaTaskValidationException(plan.Id, "准备选项的集合或资源基础目录不能为空。");
        var fixedPlan = PuloniaTaskJson.ClonePlan(plan);
        var fixedOptions = PuloniaTaskJson.Read<PuloniaTaskBuildOptions>(PuloniaTaskJson.Write(options));
        if (fixedOptions.TargetTaskId is { } targetId)
        {
            // 禁用范围外的兄弟节点，保留祖先策略、稳定地址和完整配置快照。
            if (!RestrictToTarget(fixedPlan.RootTask, targetId))
                throw new PuloniaTaskValidationException(targetId, "触发器的目标节点不存在。");
        }
        if (fixedOptions.MaxDepth is <= 0 or > PuloniaTaskValidator.MaxTreeDepth
            || fixedOptions.MaxNodes is <= 0 or > PuloniaTaskValidator.MaxTreeNodes)
            throw new PuloniaTaskValidationException(plan.Id, "准备深度或数量限制超出支持范围。");

        // 扫描和哈希都在后台进行；输入副本在返回 Task 前已固定，不受后续编辑影响。
        return Task.Run(async () =>
        {
            var context = new BuildContext(fixedOptions, ct);
            var definitionKeys = new HashSet<(string, string?)>();
            foreach (var definition in fixedOptions.Definitions)
            {
                if (definition is null || definition.ParameterSchema is null || definition.PublicParameters is null
                    || definition.AvailabilityRules is null)
                    throw new PuloniaTaskValidationException(plan.Id, "类型说明不能为空。");
                PuloniaTaskValidator.ValidateDefinitionScope(definition.TaskType, definition.ResourceId,
                    definition.SchemaVersion, definition.DefaultParameters, "definition/" + definition.TaskType);
                if (!definitionKeys.Add((definition.TaskType, definition.ResourceId)))
                    throw new PuloniaTaskValidationException(plan.Id, "类型和资源说明重复注册。");
                PuloniaTaskValidator.ValidateParameterSchema(definition.ParameterSchema, "definition/" + definition.TaskType);
                PuloniaTaskAvailability.ValidateRules(definition.AvailabilityRules,
                    "definition/" + definition.TaskType + "/availability");
            }
            if (fixedOptions.AccountId is { } accountId)
            {
                PuloniaTaskValidator.ValidateId(accountId, "account_id");
                if (!fixedPlan.Accounts.Any(a => a.AccountId == accountId && a.Enabled))
                    throw new PuloniaTaskValidationException(fixedPlan.Id, "所选账号未启用或未绑定该计划。");
            }
            var planGraph = await PuloniaTaskValidator.ValidatePlanReferencesAsync(fixedPlan,
                _store.LoadPlanAsync, ct).ConfigureAwait(false);
            foreach (var referencedPlan in planGraph.Values)
                RegisterPlan(context, referencedPlan);
            var root = await BuildTaskAsync(context, fixedPlan, fixedPlan.RootTask, fixedPlan.Id,
                true, new PuloniaTaskPolicy(), [], [], new HashSet<string> { fixedPlan.Id }, 0).ConfigureAwait(false);
            foreach (var address in fixedOptions.ParameterOverrides.Keys)
            {
                if (!context.UsedCallOverrides.Contains(address))
                    throw new PuloniaTaskValidationException(address, "调用覆盖未指向启用的具体任务。");
            }
            ct.ThrowIfCancellationRequested();
            return new PuloniaTaskSnapshot(fixedPlan, fixedOptions.AccountId, root, context.PlanJson, context.PresetJson);
        }, ct);
    }

    /// <summary>只允许目标子树及其祖先参与准备；范围外节点保留但不会读取资源。</summary>
    private static bool RestrictToTarget(PuloniaTask task, string targetId)
    {
        if (task.Id == targetId)
            return true;
        var found = false;
        foreach (var child in task.Children)
        {
            var contains = RestrictToTarget(child, targetId);
            if (!contains) child.IsEnabled = false;
            found |= contains;
        }
        return found;
    }

    /// <summary>
    /// 递归构建节点；每一步都受取消、深度和总节点数限制。
    /// </summary>
    private async Task<PuloniaTaskPreparedTask> BuildTaskAsync(BuildContext context, PuloniaTaskPlan plan,
        PuloniaTask task, string parentAddress, bool parentEnabled, PuloniaTaskPolicy inheritedPolicy,
        List<PuloniaTaskParameterOverride> ancestorOverrides, List<PuloniaTaskParameterOverride> referenceOverrides,
        HashSet<string> planStack, int depth, string? sourceTaskId = null)
    {
        context.Cancellation.ThrowIfCancellationRequested();
        var address = parentAddress + "/" + task.Id;
        if (depth > context.Options.MaxDepth || ++context.NodeCount > context.Options.MaxNodes)
            throw new PuloniaTaskValidationException(address, "引用、目录或分组重复展开超过深度/数量限制。");
        var enabled = parentEnabled && task.IsEnabled;
        var policy = MergePolicy(inheritedPolicy, task.Policy);
        var children = new List<PuloniaTaskPreparedTask>();
        var parameters = new JObject();
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        string? path = task.Path;
        string? version = null;

        if (!enabled)
        {
            // 保留禁用结构和有效开关，不读取资源/预设，也不展开已校验过的引用计划。
            parameters = (JObject)task.Parameters.DeepClone();
            foreach (var child in task.Children)
                children.Add(await BuildTaskAsync(context, plan, child, address, false, policy,
                    ancestorOverrides, referenceOverrides, planStack, depth + 1).ConfigureAwait(false));
        }
        else if (task.TaskType == "group" && task.Source is { Kind: "plan" } planSource)
        {
            var targetId = planSource.PlanId!;
            if (planStack.Contains(targetId))
                throw new PuloniaTaskValidationException(address, $"计划引用形成循环：{targetId}。");
            var target = context.Plans[targetId];
            var nextStack = new HashSet<string>(planStack, StringComparer.Ordinal) { targetId };

            // 引用边界重置普通祖先参数；只有引用节点显式提供的公共参数可以跨入。
            var repeatCount = task.RepeatCount ?? 1;
            for (var iteration = 1; iteration <= repeatCount; iteration++)
            {
                var iterationAddress = GetGroupIterationAddress(address, repeatCount, iteration);
                children.Add(await BuildTaskAsync(context, target, target.RootTask,
                    iterationAddress + "/@" + targetId, true,
                    MergePolicy(new PuloniaTaskPolicy(), task.Policy), [], task.ParameterOverrides,
                    nextStack, depth + 1).ConfigureAwait(false));
            }
        }
        else if (task.TaskType == "group")
        {
            var nestedOverrides = new List<PuloniaTaskParameterOverride>(ancestorOverrides);
            var repeatCount = task.RepeatCount ?? 1;
            // 同层通用覆盖先应用，资源专用覆盖后应用；两者都低于更近的祖先。
            nestedOverrides.AddRange(task.ParameterOverrides.OrderBy(item => item.ResourceId is null ? 0 : 1));
            if (task.Source is { Kind: "directory" } directory)
            {
                path = ResolvePath(context, directory.Path!, address);
                var files = EnumerateFiles(context, path, "*.json", directory.Recursive, address);
                var manifest = new StringBuilder();
                for (var iteration = 1; iteration <= repeatCount; iteration++)
                {
                    var iterationAddress = GetGroupIterationAddress(address, repeatCount, iteration);
                    foreach (var file in files)
                    {
                        var relative = NormalizeRelativePath(path, file);
                        // ID 来自相对资源位置，与文件内容、数组顺序和组名无关。
                        var generatedId = "resource-" + HashText(relative.ToLowerInvariant());
                        var generatedTask = new PuloniaTask
                        {
                            Id = generatedId,
                            Name = System.IO.Path.GetFileNameWithoutExtension(file),
                            TaskType = directory.TaskType!,
                            Path = file
                        };
                        var prepared = await BuildTaskAsync(context, plan, generatedTask, iterationAddress, true, policy,
                            nestedOverrides, referenceOverrides, planStack, depth + 1, task.Id).ConfigureAwait(false);
                        children.Add(prepared);
                        if (iteration == 1)
                            manifest.Append(relative).Append('\0').Append(prepared.ResourceVersion).Append('\n');
                    }
                }
                version = PuloniaTaskResourceFingerprint.ComputeTextVersion(manifest.ToString());
                if (directory.Version is not null && directory.Version != version)
                    throw new PuloniaTaskValidationException(address, "引用目录内容与固定版本不一致，请确认新内容后更新计划。");
            }
            else
            {
                for (var iteration = 1; iteration <= repeatCount; iteration++)
                {
                    var iterationAddress = GetGroupIterationAddress(address, repeatCount, iteration);
                    foreach (var child in task.Children)
                        children.Add(await BuildTaskAsync(context, plan, child, iterationAddress, true, policy,
                            nestedOverrides, referenceOverrides, planStack, depth + 1).ConfigureAwait(false));
                }
            }
        }
        else
        {
            var definition = FindDefinition(context, task, address);
            ApplyParameters(parameters, sources, definition.DefaultParameters, "default/" + task.TaskType);
            var presetId = task.PresetId;
            if (context.Options.AccountId is { } accountId)
            {
                var binding = plan.Accounts.FirstOrDefault(a => a.AccountId == accountId && a.Enabled);
                if (binding?.PresetSelections.TryGetValue(task.Id, out var replacement) == true)
                    presetId = replacement;
            }
            if (presetId is not null)
            {
                if (!context.Presets.TryGetValue(presetId, out var preset))
                {
                    preset = await _store.LoadPresetAsync(presetId, context.Cancellation).ConfigureAwait(false)
                             ?? throw new PuloniaTaskValidationException(address, $"预设 {presetId} 不存在。");
                    context.Presets.Add(presetId, preset);
                    context.PresetJson.Add(presetId, PuloniaTaskJson.WritePreset(preset));
                }
                if (!MatchesScope(preset.TaskType, preset.ResourceId, preset.SchemaVersion, task, definition))
                    throw new PuloniaTaskValidationException(address, "预设的类型、资源或参数版本不匹配。");
                ApplyParameters(parameters, sources, preset.Values, "preset/" + presetId);
            }
            foreach (var item in ancestorOverrides)
            {
                if (MatchesScope(item.TaskType, item.ResourceId, item.SchemaVersion, task, definition))
                    ApplyParameters(parameters, sources, item.Values, context.OverrideSources[item]);
            }
            ApplyParameters(parameters, sources, task.Parameters, address);
            foreach (var item in referenceOverrides.OrderBy(item => item.ResourceId is null ? 0 : 1))
            {
                if (!MatchesScope(item.TaskType, item.ResourceId, item.SchemaVersion, task, definition))
                    continue;
                if (item.Values.Properties().Any(p => !definition.PublicParameters.Contains(p.Name, StringComparer.Ordinal)))
                    throw new PuloniaTaskValidationException(address, "计划引用试图覆盖未声明的公共参数。");
                ApplyParameters(parameters, sources, item.Values, context.OverrideSources[item] + "/public");
            }
            if (context.Options.ParameterOverrides.TryGetValue(address, out var callValues))
            {
                if (callValues is null)
                    throw new PuloniaTaskValidationException(address, "调用覆盖必须是对象。");
                ApplyParameters(parameters, sources, callValues, "call/" + address);
                context.UsedCallOverrides.Add(address);
            }
            PuloniaTaskValidator.ValidateParameters(parameters, definition.ParameterSchema, address + "/parameters");
            if (task.TaskType is "javascript" or "pathing" or "keymouse")
            {
                path = ResolvePath(context, task.Path!, address, definition.ResourceBaseDirectory);
                try
                {
                    version = task.TaskType == "javascript" && Directory.Exists(path)
                        ? await HashDirectoryAsync(context, path, address).ConfigureAwait(false)
                        : await HashFileAsync(context, path, address).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new PuloniaTaskValidationException(address, $"无法读取能力资源 {path}：{ex.Message}", ex);
                }
                if (task.ResourceVersion is not null && task.ResourceVersion != version)
                    throw new PuloniaTaskValidationException(address,
                        "资源内容与任务创建时固定的版本不一致，请在任务计划中确认更新后再运行。");
            }
        }

        return new PuloniaTaskPreparedTask(plan.Id, task.Id, sourceTaskId ?? task.Id, address, task.Name,
            task.TaskType, enabled, path, version, parameters, sources, policy, children);
    }

    /// <summary>
    /// 仅在分组执行多轮时追加轮次地址，保持普通分组原有任务地址不变。
    /// </summary>
    private static string GetGroupIterationAddress(string address, int repeatCount, int iteration)
        => repeatCount > 1 ? address + "/#" + iteration : address;

    /// <summary>
    /// 按资源专用说明优先、类型通用说明其次查找能力。
    /// </summary>
    private static PuloniaTaskDefinition FindDefinition(BuildContext context, PuloniaTask task, string address)
    {
        return context.Options.Definitions.FirstOrDefault(d => d.TaskType == task.TaskType && d.ResourceId == task.Path)
               ?? context.Options.Definitions.FirstOrDefault(d => d.TaskType == task.TaskType && d.ResourceId is null)
               ?? throw new PuloniaTaskValidationException(address, $"未注册能力或资源 {task.TaskType}/{task.Path}。");
    }

    /// <summary>
    /// 严格匹配能力作用域，防止脚本同名参数相互污染。
    /// </summary>
    private static bool MatchesScope(string type, string? resourceId, int schemaVersion, PuloniaTask task, PuloniaTaskDefinition definition)
        => type == task.TaskType && schemaVersion == definition.SchemaVersion
           && (resourceId is null || resourceId == definition.ResourceId || resourceId == task.Path);

    /// <summary>
    /// 顶层字段整体替换；显式空值、布尔值和集合不会丢失。
    /// </summary>
    private static void ApplyParameters(JObject result, Dictionary<string, string> sources, JObject values, string source)
    {
        foreach (var field in values.Properties())
        {
            result[field.Name] = field.Value.DeepClone();
            sources[field.Name] = source;
        }
    }

    /// <summary>
    /// 合并策略的每个可继承字段，0 不被当成未填写。
    /// </summary>
    private static PuloniaTaskPolicy MergePolicy(PuloniaTaskPolicy inherited, PuloniaTaskPolicy current) => new()
    {
        TimeoutSeconds = current.TimeoutSeconds ?? inherited.TimeoutSeconds,
        FailureBehavior = current.FailureBehavior ?? inherited.FailureBehavior ?? "stop_plan",
        MaxRetries = current.MaxRetries ?? inherited.MaxRetries ?? 0,
        RetryDelaySeconds = current.RetryDelaySeconds ?? inherited.RetryDelaySeconds ?? 0
    };

    /// <summary>
    /// 缓存每份计划及其参数来源，重复引用始终使用同一修订。
    /// </summary>
    private static void RegisterPlan(BuildContext context, PuloniaTaskPlan plan)
    {
        context.Plans.Add(plan.Id, plan);
        context.PlanJson.Add(plan.Id, PuloniaTaskJson.WritePlan(plan));
        RegisterOverrideSources(context, plan.RootTask, plan.Id);
    }

    /// <summary>
    /// 记录公共参数在编辑树中的原始位置。
    /// </summary>
    private static void RegisterOverrideSources(BuildContext context, PuloniaTask task, string parent)
    {
        var address = parent + "/" + task.Id;
        foreach (var item in task.ParameterOverrides)
            context.OverrideSources.Add(item, address);
        foreach (var child in task.Children)
            RegisterOverrideSources(context, child, address);
    }

    /// <summary>
    /// 解析显式路径变量；未知变量在原节点报错。
    /// </summary>
    private static string ResolvePath(BuildContext context, string path, string address, string? baseDirectory = null)
    {
        var expanded = Regex.Replace(path, "\\{([^{}]+)\\}", match =>
            context.Options.PathVariables.TryGetValue(match.Groups[1].Value, out var value)
                ? value
                : throw new PuloniaTaskValidationException(address, $"未知路径变量 {match.Value}。"));
        if (expanded.Contains('{') || expanded.Contains('}'))
            throw new PuloniaTaskValidationException(address, "资源路径包含未解析的变量。");
        return System.IO.Path.GetFullPath(expanded,
            System.IO.Path.GetFullPath(baseDirectory ?? context.Options.BaseDirectory));
    }

    /// <summary>
    /// 有限扫描目录，跳过重解析点并稳定排序；访问失败不能生成空占位。
    /// </summary>
    private static string[] EnumerateFiles(BuildContext context, string path, string pattern, bool recursive, string address)
    {
        var key = (path, pattern, recursive);
        if (context.DirectoryFiles.TryGetValue(key, out var cached))
            return cached;
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new PuloniaTaskValidationException(address, "目录引用不能直接指向重解析点。");
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            var pending = new Stack<(string Directory, int Depth)>();
            pending.Push((path, 0));
            var directoryCount = 1;
            var files = new List<string>();
            while (pending.TryPop(out var current))
            {
                context.Cancellation.ThrowIfCancellationRequested();
                foreach (var file in Directory.EnumerateFiles(current.Directory, pattern, options))
                {
                    context.Cancellation.ThrowIfCancellationRequested();
                    if (files.Count >= context.Options.MaxNodes)
                        throw new PuloniaTaskValidationException(address, "资源目录超过扫描数量限制。");
                    files.Add(file);
                }
                if (!recursive)
                    continue;
                foreach (var directory in Directory.EnumerateDirectories(current.Directory, "*", options))
                {
                    context.Cancellation.ThrowIfCancellationRequested();
                    if (current.Depth >= context.Options.MaxDepth || ++directoryCount > context.Options.MaxNodes)
                        throw new PuloniaTaskValidationException(address, "目录层级或数量超过限制，不能只展开部分内容。");
                    pending.Push((directory, current.Depth + 1));
                }
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            var result = files.ToArray();
            context.DirectoryFiles.Add(key, result);
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PuloniaTaskValidationException(address, $"无法读取引用目录 {path}：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 固定 JS 目录的全部文件指纹；不加载脚本引擎。
    /// </summary>
    private static async Task<string> HashDirectoryAsync(BuildContext context, string path, string address)
    {
        var manifest = new StringBuilder();
        foreach (var file in EnumerateFiles(context, path, "*", true, address))
        {
            var hash = await HashFileAsync(context, file, address).ConfigureAwait(false);
            manifest.Append(NormalizeRelativePath(path, file)).Append('\0').Append(hash).Append('\n');
        }
        return PuloniaTaskResourceFingerprint.ComputeTextVersion(manifest.ToString());
    }

    /// <summary>
    /// 记录文件内容哈希；相同文件在本次准备中只读取一次。
    /// </summary>
    private static async Task<string> HashFileAsync(BuildContext context, string path, string address)
    {
        context.Cancellation.ThrowIfCancellationRequested();
        if (context.ResourceHashes.TryGetValue(path, out var cached))
            return cached;
        if (context.ResourceHashes.Count >= context.Options.MaxNodes)
            throw new PuloniaTaskValidationException(address, "本次资源文件总数超过限制。");
        var hash = await PuloniaTaskResourceFingerprint.ComputeFileVersionAsync(path, context.Cancellation)
            .ConfigureAwait(false);
        context.ResourceHashes.Add(path, hash);
        return hash;
    }

    /// <summary>
    /// 统一相对路径分隔符，用于身份及清单指纹。
    /// </summary>
    private static string NormalizeRelativePath(string directory, string file)
        => PuloniaTaskResourceFingerprint.NormalizeRelativePath(directory, file);

    /// <summary>
    /// 生成稳定的文本 SHA-256。
    /// </summary>
    private static string HashText(string value) => PuloniaTaskResourceFingerprint.ComputeTextVersion(value);

    /// <summary>
    /// 仅属于一次准备的缓存与预算，不跨运行共享可变状态。
    /// </summary>
    private sealed class BuildContext
    {
        /// <summary>
        /// 已固定的本次选项。
        /// </summary>
        public PuloniaTaskBuildOptions Options { get; }
        /// <summary>
        /// 本次取消令牌。
        /// </summary>
        public CancellationToken Cancellation { get; }
        /// <summary>
        /// 已展开节点数量。
        /// </summary>
        public int NodeCount { get; set; }
        /// <summary>
        /// 本次固定的计划。
        /// </summary>
        public Dictionary<string, PuloniaTaskPlan> Plans { get; } = new(StringComparer.Ordinal);
        /// <summary>
        /// 序列化的计划副本。
        /// </summary>
        public Dictionary<string, string> PlanJson { get; } = new(StringComparer.Ordinal);
        /// <summary>
        /// 本次固定的预设。
        /// </summary>
        public Dictionary<string, PuloniaTaskPreset> Presets { get; } = new(StringComparer.Ordinal);
        /// <summary>
        /// 序列化的预设副本。
        /// </summary>
        public Dictionary<string, string> PresetJson { get; } = new(StringComparer.Ordinal);
        /// <summary>
        /// 公共参数的编辑位置。
        /// </summary>
        public Dictionary<PuloniaTaskParameterOverride, string> OverrideSources { get; } = new();
        /// <summary>
        /// 已固定的资源指纹。
        /// </summary>
        public Dictionary<string, string> ResourceHashes { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// 本次固定的目录清单，重复引用不重新枚举。
        /// </summary>
        public Dictionary<(string Path, string Pattern, bool Recursive), string[]> DirectoryFiles { get; } = new();
        /// <summary>
        /// 已消费的本次调用覆盖地址。
        /// </summary>
        public HashSet<string> UsedCallOverrides { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 建立独立准备上下文。
        /// </summary>
        public BuildContext(PuloniaTaskBuildOptions options, CancellationToken cancellation)
        {
            Options = options;
            Cancellation = cancellation;
        }
    }
}
