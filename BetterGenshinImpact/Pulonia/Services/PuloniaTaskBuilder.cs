using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Repositories;
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

    /// <summary>固定资源版本的共享仓库存储。</summary>
    private readonly ScriptRepositoryStore _repositories;

    /// <summary>
    /// 使用存储建立准备入口。
    /// </summary>
    public PuloniaTaskBuilder(PuloniaTaskStore store, ScriptRepositoryStore? repositories = null)
    {
        _store = store;
        _repositories = repositories ?? ScriptRepositoryStore.Shared;
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
            using var context = new BuildContext(fixedOptions, ct);
            // 能力说明来自执行器代码，合法性由 PuloniaTaskValidator.ValidateDefinitions 在单元测试（或注册时）保证，
            // 不在每次准备时复验全部说明。
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
    /// <param name="ancestorOverrides">当前计划内由外到内每个祖先分组的公共参数项；引用边界处重置为空。</param>
    /// <param name="referenceOverrides">最近一层计划引用节点提供的公共参数项。</param>
    private async Task<PuloniaTaskPreparedTask> BuildTaskAsync(BuildContext context, PuloniaTaskPlan plan,
        PuloniaTask task, string parentAddress, bool parentEnabled, PuloniaTaskPolicy inheritedPolicy,
        IReadOnlyList<IReadOnlyList<PuloniaTaskParameterOverride>> ancestorOverrides,
        IReadOnlyList<PuloniaTaskParameterOverride> referenceOverrides,
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
            var repeatCount = task.RepeatCount ?? 1;
            // 本组公共参数作为最内一层追加；同层通用覆盖先应用、资源专用覆盖后应用（由参数解析器统一排序），两者都低于更近的祖先。
            IReadOnlyList<IReadOnlyList<PuloniaTaskParameterOverride>> nestedOverrides = [.. ancestorOverrides, task.ParameterOverrides];
            if (task.Source is { Kind: "directory" } directory)
            {
                // 目录内资源统一表示为相对路径：仓库资源从固定快照列出，本地资源扫描磁盘。
                IReadOnlyList<(string Relative, string? File)> entries;
                if (directory.Resource is { } directoryResource)
                {
                    var snapshot = await OpenRetainedSnapshotAsync(context, directoryResource, directory.TaskType!)
                        .ConfigureAwait(false);
                    entries = ScriptRepositoryStore.ListDirectoryResources(snapshot, directoryResource.RelativePath,
                            directory.Recursive).Select(relative => (relative, (string?)null)).ToArray();
                    if (entries.Count > context.Options.MaxNodes)
                        throw new PuloniaTaskValidationException(address, "资源目录超过扫描数量限制。");
                }
                else
                {
                    path = ResolvePath(context, directory.Path!, address);
                    var directoryPath = path;
                    entries = EnumerateFiles(context, directoryPath, "*.json", directory.Recursive, address)
                        .Select(file => (NormalizeRelativePath(directoryPath, file), (string?)file)).ToArray();
                }
                // ID 来自相对资源位置，与文件内容、数组顺序和组名无关；只差大小写的两个文件会得到同一 ID，明确报错。
                var generatedIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in entries)
                {
                    if (!generatedIds.Add(PuloniaId.FromResourcePath(entry.Relative)))
                        throw new PuloniaTaskValidationException(address, $"目录中的资源 {entry.Relative} 与其他资源生成了相同的节点 ID。");
                }
                var manifest = new List<(string RelativePath, string? Version)>();
                for (var iteration = 1; iteration <= repeatCount; iteration++)
                {
                    var iterationAddress = GetGroupIterationAddress(address, repeatCount, iteration);
                    foreach (var (relative, file) in entries)
                    {
                        var generatedTask = new PuloniaTask
                        {
                            Id = PuloniaId.FromResourcePath(relative),
                            Name = System.IO.Path.GetFileNameWithoutExtension(relative),
                            TaskType = directory.TaskType!,
                            Path = file,
                            Resource = directory.Resource?.WithPath(directory.Resource.RelativePath + "/" + relative)
                        };
                        var prepared = await BuildTaskAsync(context, plan, generatedTask, iterationAddress, true, policy,
                            nestedOverrides, referenceOverrides, planStack, depth + 1, task.Id).ConfigureAwait(false);
                        children.Add(prepared);
                        if (iteration == 1)
                            manifest.Add((relative, prepared.ResourceVersion));
                    }
                }
                // 与创建任务、更新检查使用同一清单格式：相对路径\0子资源指纹\n。
                version = PuloniaTaskResourceFingerprint.ComputeManifestVersion(manifest);
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
            var definition = PuloniaParameterResolver.FindDefinition(context.Options.Definitions, task)
                             ?? throw new PuloniaTaskValidationException(address, $"未注册能力或资源 {task.TaskType}/{task.Path}。");

            // 1. 账号绑定可替换预设选择；预设在本次准备内只读取一次，是否缺失或不兼容由共用解析器判定
            var presetId = PuloniaParameterResolver.GetEffectivePresetId(task, plan.Accounts, context.Options.AccountId);
            var preset = presetId is null ? null : await LoadPresetAsync(context, presetId).ConfigureAwait(false);

            // 2. 与界面预览共用同一套层级解析，再按运行准备的要求把预设、公共参数和调用覆盖问题报为错误
            var hasCallOverride = context.Options.ParameterOverrides.TryGetValue(address, out var callValues);
            var resolution = PuloniaParameterResolver.Resolve(task, definition, presetId, preset, ancestorOverrides,
                referenceOverrides, callValues);
            if (resolution.PresetStatus == PuloniaPresetStatus.Missing)
                throw new PuloniaTaskValidationException(address, $"预设 {presetId} 不存在。");
            if (resolution.PresetStatus == PuloniaPresetStatus.Incompatible)
                throw new PuloniaTaskValidationException(address, "预设的类型、资源或参数版本不匹配。");
            if (resolution.HasUndeclaredPublicOverride)
                throw new PuloniaTaskValidationException(address, "计划引用试图覆盖未声明的公共参数。");
            if (hasCallOverride)
            {
                if (callValues is null)
                    throw new PuloniaTaskValidationException(address, "调用覆盖必须是对象。");
                context.UsedCallOverrides.Add(address);
            }

            // 3. 来源文本随运行快照持久化，格式保持不变
            parameters = resolution.Parameters;
            foreach (var (name, source) in resolution.Sources)
                sources[name] = DescribeSource(context, source, task, presetId, address);
            PuloniaTaskValidator.ValidateParameters(parameters, definition.ParameterSchema, address + "/parameters");
            if (task.TaskType is "javascript" or "pathing" or "keymouse")
            {
                if (task.Resource is { } resource)
                {
                    // 仓库资源不写本机路径：执行器只按 resource 从已保留归档读取，指纹在固定快照上计算。
                    var snapshot = await OpenRetainedSnapshotAsync(context, resource, task.TaskType).ConfigureAwait(false);
                    path = null;
                    version = ComputeRepositoryVersion(context, snapshot, resource, task.TaskType, address);
                }
                else
                {
                    path = ResolvePath(context, task.Path!, address, definition.ResourceBaseDirectory);
                    try
                    {
                        version = task.TaskType == "javascript"
                            ? await HashDirectoryAsync(context, path, address).ConfigureAwait(false)
                            : await HashFileAsync(context, path, address).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        throw new PuloniaTaskValidationException(address, $"无法读取能力资源 {path}：{ex.Message}", ex);
                    }
                }
                if (task.ResourceVersion is not null && task.ResourceVersion != version)
                    throw new PuloniaTaskValidationException(address,
                        "资源内容与任务创建时固定的版本不一致，请在任务计划中确认更新后再运行。");
            }
        }

        return new PuloniaTaskPreparedTask(plan.Id, task.Id, sourceTaskId ?? task.Id, address, task.Name,
            task.TaskType, enabled, path, version, parameters, sources, policy, children, task.Resource ?? task.Source?.Resource,
            task.TaskType == "pathing" && enabled
                ? PuloniaCollectionCooldown.Resolve(path, task.Resource, parameters) : null);
    }

    /// <summary>
    /// 仅在分组执行多轮时追加轮次地址，保持普通分组原有任务地址不变。
    /// </summary>
    private static string GetGroupIterationAddress(string address, int repeatCount, int iteration)
        => repeatCount > 1 ? address + "/#" + iteration : address;

    /// <summary>
    /// 读取并固定本次准备使用的预设；同一预设只读取一次。缺失时返回 null，由解析结果判定并报错。
    /// </summary>
    private async Task<PuloniaTaskPreset?> LoadPresetAsync(BuildContext context, string presetId)
    {
        if (context.Presets.TryGetValue(presetId, out var cached))
            return cached;
        var preset = await _store.LoadPresetAsync(presetId, context.Cancellation).ConfigureAwait(false);
        if (preset is not null)
        {
            context.Presets.Add(presetId, preset);
            context.PresetJson.Add(presetId, PuloniaTaskJson.WritePreset(preset));
        }
        return preset;
    }

    /// <summary>
    /// 生成写入运行快照的参数来源文本：默认值和预设带类型/ID，分组与引用公共参数指向其在编辑树中的位置，
    /// 节点参数和调用覆盖指向运行地址。文本随快照持久化，格式不能改变。
    /// </summary>
    private static string DescribeSource(BuildContext context, PuloniaParameterSource source, PuloniaTask task,
        string? presetId, string address) => source.Layer switch
    {
        PuloniaParameterLayer.Default => "default/" + task.TaskType,
        PuloniaParameterLayer.Preset => "preset/" + presetId,
        PuloniaParameterLayer.Group => context.OverrideSources[source.Override!],
        PuloniaParameterLayer.Node => address,
        PuloniaParameterLayer.ReferencePublic => context.OverrideSources[source.Override!] + "/public",
        PuloniaParameterLayer.Call => "call/" + address,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source.Layer, "未知的参数来源层级。")
    };

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
    /// 计划已由 <see cref="PuloniaTaskValidator.ValidatePlanReferencesAsync"/> 校验，这里直接输出，不再重复校验。
    /// </summary>
    private static void RegisterPlan(BuildContext context, PuloniaTaskPlan plan)
    {
        context.Plans.Add(plan.Id, plan);
        context.PlanJson.Add(plan.Id, PuloniaTaskJson.Write(plan));
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
    /// 只固定 JS 根目录 manifest.json 和 main.js 的指纹；不加载脚本引擎，不扫描其他资源。
    /// </summary>
    private static async Task<string> HashDirectoryAsync(BuildContext context, string path, string address)
    {
        // 与 ComputeJavaScriptVersionAsync 相同的清单，只是单文件哈希经过本次准备的去重缓存。
        var manifest = new List<(string RelativePath, string? Version)>();
        foreach (var file in PuloniaTaskResourceFingerprint.GetJavaScriptResourceFiles(path, context.Cancellation))
            manifest.Add((NormalizeRelativePath(path, file), await HashFileAsync(context, file, address).ConfigureAwait(false)));
        return PuloniaTaskResourceFingerprint.ComputeManifestVersion(manifest);
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
    /// 确认仓库资源已保留，并返回本次准备中该修订的固定快照；同一修订的快照在一次准备内复用。
    /// 缓存的快照不含新保留的范围时重新打开，准备结束后统一释放。
    /// </summary>
    private async Task<ScriptRepositorySnapshot> OpenRetainedSnapshotAsync(BuildContext context,
        ScriptResourceReference reference, string taskType)
    {
        await _repositories.RetainAsync(reference, taskType, context.Cancellation).ConfigureAwait(false);
        var key = (reference.RepositoryId, reference.ApprovedRevision);
        var source = "repo/" + reference.RelativePath;
        if (context.Snapshots.TryGetValue(key, out var cached)
            && (cached.FileExists(source) || cached.DirectoryExists(source)))
            return cached;
        cached?.Dispose();
        var snapshot = await _repositories.OpenApprovedAsync(reference, context.Cancellation).ConfigureAwait(false);
        context.Snapshots[key] = snapshot;
        return snapshot;
    }

    /// <summary>
    /// 在固定快照上计算仓库资源指纹，算法与创建任务和更新检查一致；相同资源在本次准备中只计算一次。
    /// </summary>
    private static string ComputeRepositoryVersion(BuildContext context, ScriptRepositorySnapshot snapshot,
        ScriptResourceReference reference, string taskType, string address)
    {
        context.Cancellation.ThrowIfCancellationRequested();
        var key = (reference.RepositoryId, reference.ApprovedRevision, reference.RelativePath, taskType);
        if (context.RepositoryVersions.TryGetValue(key, out var cached))
            return cached;
        if (context.RepositoryVersions.Count + context.ResourceHashes.Count >= context.Options.MaxNodes)
            throw new PuloniaTaskValidationException(address, "本次资源文件总数超过限制。");
        try
        {
            var version = PuloniaTaskResourceFingerprint.ComputeRepositoryVersion(snapshot, reference.RelativePath, taskType);
            context.RepositoryVersions.Add(key, version);
            return version;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            throw new PuloniaTaskValidationException(address, $"无法读取仓库资源 {reference.RelativePath}：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 仅属于一次准备的缓存与预算，不跨运行共享可变状态；释放时关闭本次打开的固定快照。
    /// </summary>
    private sealed class BuildContext : IDisposable
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
        /// 已固定的本地资源指纹，按完整路径索引。
        /// </summary>
        public Dictionary<string, string> ResourceHashes { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// 已固定的仓库资源指纹，按仓库、修订、相对路径和类型索引。
        /// </summary>
        public Dictionary<(string Repository, string Revision, string Path, string Type), string> RepositoryVersions { get; } = new();
        /// <summary>
        /// 本次准备打开的固定快照，按仓库和修订复用，准备结束后统一释放。
        /// </summary>
        public Dictionary<(string Repository, string Revision), ScriptRepositorySnapshot> Snapshots { get; } = new();
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

        /// <summary>
        /// 释放本次准备打开的全部固定快照；准备结果不持有快照引用。
        /// </summary>
        public void Dispose()
        {
            foreach (var snapshot in Snapshots.Values)
                snapshot.Dispose();
            Snapshots.Clear();
        }
    }
}
