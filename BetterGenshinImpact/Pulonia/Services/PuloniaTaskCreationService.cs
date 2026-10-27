using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Repositories;
using BetterGenshinImpact.Pulonia.Models;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Pulonia.Services;

/// <summary>
/// 按新增任务表单的输入建立任务节点：合并默认值校验参数、固定资源版本，并生成单资源、目录引用或展开后的任务树。
/// 资源指纹统一由 <see cref="PuloniaTaskResourceFingerprint"/> 计算；本类与界面无关，可直接单元测试。
/// </summary>
/// <remarks>
/// 内部等待不使用 ConfigureAwait(false)：进度回调与延续回到调用方的同步上下文，界面可以直接更新状态文本。
/// </remarks>
public sealed class PuloniaTaskCreationService
{
    /// <summary>
    /// 批量添加目录时只收集 JSON 资源。
    /// </summary>
    private const string DirectoryResourcePattern = "*.json";

    /// <summary>
    /// 目录中没有可添加资源时的提示。
    /// </summary>
    private const string EmptyDirectoryMessage = "选中的目录中没有可添加的 JSON 资源。";

    /// <summary>
    /// 提供本地目录枚举和仓库读取存储。
    /// </summary>
    private readonly PuloniaTaskResourceCatalog _resourceCatalog;

    /// <summary>
    /// 使用创建弹窗共用的资源目录建立服务。
    /// </summary>
    public PuloniaTaskCreationService(PuloniaTaskResourceCatalog resourceCatalog)
    {
        _resourceCatalog = resourceCatalog ?? throw new ArgumentNullException(nameof(resourceCatalog));
    }

    /// <summary>
    /// 选中的资源是否按目录批量添加；只有地图追踪路线目录和录制目录支持，JS 项目目录按单个资源处理。
    /// </summary>
    public static bool IsDirectoryImport(string? taskType, PuloniaTaskResourceDescriptor? resource)
        => resource?.IsDirectory == true && taskType is "pathing" or "keymouse";

    /// <summary>
    /// 校验参数并建立最终任务节点；仓库资源先保留所选版本，成功后才生成引用，计划不会保存未保留的资源。
    /// </summary>
    /// <param name="request">表单收集的名称、能力、资源与显式参数。</param>
    /// <param name="reportProgress">展开目录时逐项回调（当前序号，总数），在固定该项版本前调用。</param>
    /// <exception cref="PuloniaTaskValidationException">合并后的参数不符合能力 Schema。</exception>
    /// <exception cref="FormatException">必填参数为空，或目录中没有可添加的资源。</exception>
    public async Task<PuloniaTask> BuildAsync(PuloniaTaskCreationRequest request, Action<int, int>? reportProgress = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        // 1. 节点只保存相对默认值的变化，校验时按运行时的合并方式得到有效参数
        ValidateParameters(request.Definition, request.Overrides);

        // 2. Shell、进程内 C# 等无资源任务只保存名称与显式参数
        if (request.Resource is not { } resource)
        {
            return new PuloniaTask
            {
                Name = request.Name,
                TaskType = request.Definition.TaskType,
                Parameters = TaskParameters(request),
                Settings = SharedSettings(request)
            };
        }

        // 3. 地图追踪与 JS 资源均来自仓库索引；其余资源（当前为录制回放）来自本地资源目录
        return resource.Resource is { } reference
            ? await BuildRepositoryTaskAsync(request, reference, IsDirectoryImport(request.Definition.TaskType, resource), reportProgress)
            : await BuildLocalTaskAsync(request, resource, reportProgress);
    }

    /// <summary>
    /// 合并能力默认值与显式参数后按 Schema 校验，再补充 Schema 子集尚不能表达的非空约束。
    /// </summary>
    private static void ValidateParameters(PuloniaTaskDefinition definition, JObject overrides)
    {
        var effectiveParameters = (JObject)definition.DefaultParameters.DeepClone();
        foreach (var property in overrides.Properties())
            effectiveParameters[property.Name] = property.Value.DeepClone();
        PuloniaTaskValidator.ValidateParameters(effectiveParameters, definition.ParameterSchema, "task/parameters");

        var requiredKey = definition.TaskType switch { "shell" => "file_name", "csharp" => "operation", _ => null };
        if (requiredKey is not null && string.IsNullOrWhiteSpace(effectiveParameters.Value<string>(requiredKey)))
            throw new FormatException($"“{PuloniaTaskFieldNames.GetDisplayName(requiredKey)}”不能为空。");
    }

    /// <summary>
    /// 为本地资源建立单文件节点、目录引用或展开的任务树。
    /// </summary>
    private async Task<PuloniaTask> BuildLocalTaskAsync(PuloniaTaskCreationRequest request,
        PuloniaTaskResourceDescriptor resource, Action<int, int>? reportProgress)
    {
        var definition = request.Definition;
        if (!IsDirectoryImport(definition.TaskType, resource))
        {
            // 本地索引只提供录制回放文件与目录，目录一律批量添加；JS 项目目录只来自仓库，不再走本地目录指纹。
            return new PuloniaTask
            {
                Name = request.Name,
                TaskType = definition.TaskType,
                Path = resource.RelativePath,
                ResourceVersion = await PuloniaTaskResourceFingerprint.ComputeFileVersionAsync(resource.FullPath),
                Parameters = TaskParameters(request),
                Settings = SharedSettings(request)
            };
        }

        var files = await _resourceCatalog.GetDirectoryFilesAsync(resource.FullPath, DirectoryResourcePattern,
            request.IncludeSubdirectories);
        if (files.Count == 0)
            throw new FormatException(EmptyDirectoryMessage);

        if (request.DirectoryImportMode == PuloniaTaskDirectoryImportMode.Reference)
        {
            var reference = new PuloniaTask
            {
                Name = request.Name,
                TaskType = "group",
                Source = new PuloniaTaskSource
                {
                    Kind = "directory",
                    Path = resource.FullPath,
                    TaskType = definition.TaskType,
                    Recursive = request.IncludeSubdirectories,
                    Version = await PuloniaTaskResourceFingerprint.ComputeDirectoryVersionAsync(resource.FullPath, files)
                }
            };
            ApplyReferenceSettings(reference, request);
            return reference;
        }

        var group = CreateExpandedGroup(request);
        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            reportProgress?.Invoke(index + 1, files.Count);
            var leaf = new PuloniaTask
            {
                Name = Path.GetFileNameWithoutExtension(file),
                TaskType = definition.TaskType,
                Path = Path.GetRelativePath(definition.ResourceBaseDirectory!, file),
                ResourceVersion = await PuloniaTaskResourceFingerprint.ComputeFileVersionAsync(file),
                Parameters = TaskParameters(request)
            };
            AddExpandedLeaf(group, request.DirectoryImportMode, Path.GetRelativePath(resource.FullPath, file), leaf);
        }
        return group;
    }

    /// <summary>
    /// 为仓库资源建立任务节点：先保留所选版本，再在固定快照上计算与运行准备、更新检查一致的指纹。
    /// 目录清单与顺序和运行准备共用，确认后不会因列举方式不同而判为内容变化。
    /// </summary>
    private async Task<PuloniaTask> BuildRepositoryTaskAsync(PuloniaTaskCreationRequest request,
        ScriptResourceReference reference, bool isDirectoryImport, Action<int, int>? reportProgress)
    {
        var repositories = _resourceCatalog.Repositories;
        var definition = request.Definition;
        var taskType = definition.TaskType;
        await repositories.RetainAsync(reference, taskType);
        using var snapshot = await repositories.OpenApprovedAsync(reference);
        if (!isDirectoryImport)
        {
            var version = await Task.Run(() => PuloniaTaskResourceFingerprint.ComputeRepositoryVersion(snapshot,
                reference.RelativePath, taskType));
            return new PuloniaTask
            {
                Name = request.Name,
                TaskType = taskType,
                Resource = reference,
                ResourceVersion = version,
                Parameters = TaskParameters(request),
                Settings = SharedSettings(request)
            };
        }

        var recursive = request.IncludeSubdirectories;
        var files = await Task.Run(() => ScriptRepositoryStore.ListDirectoryResources(snapshot, reference.RelativePath, recursive));
        if (files.Count == 0)
            throw new FormatException(EmptyDirectoryMessage);
        if (request.DirectoryImportMode == PuloniaTaskDirectoryImportMode.Reference)
        {
            var version = await Task.Run(() => PuloniaTaskResourceFingerprint.ComputeRepositoryVersion(snapshot,
                reference.RelativePath, taskType, recursive, directory: true));
            var directoryReference = new PuloniaTask
            {
                Name = request.Name,
                TaskType = "group",
                Source = new PuloniaTaskSource
                {
                    Kind = "directory",
                    Resource = reference,
                    TaskType = taskType,
                    Recursive = recursive,
                    Version = version
                }
            };
            ApplyReferenceSettings(directoryReference, request);
            return directoryReference;
        }

        var group = CreateExpandedGroup(request);
        for (var index = 0; index < files.Count; index++)
        {
            var relative = files[index];
            reportProgress?.Invoke(index + 1, files.Count);
            var child = reference.WithPath(reference.RelativePath + "/" + relative);
            var leaf = new PuloniaTask
            {
                Name = Path.GetFileNameWithoutExtension(relative),
                TaskType = taskType,
                Resource = child,
                ResourceVersion = await Task.Run(() => PuloniaTaskResourceFingerprint.ComputeRepositoryVersion(snapshot,
                    child.RelativePath, taskType)),
                Parameters = TaskParameters(request)
            };
            AddExpandedLeaf(group, request.DirectoryImportMode, relative, leaf);
        }
        return group;
    }

    /// <summary>
    /// 建立展开目录使用的根分组：表单中属于共享设置的项写在根分组上，由全部子任务继承；任务参数写在每个子任务上。
    /// </summary>
    private static PuloniaTask CreateExpandedGroup(PuloniaTaskCreationRequest request)
        => new() { Name = request.Name, TaskType = "group", Settings = SharedSettings(request) };

    /// <summary>
    /// 目录引用展开的任务只能继承共享设置，表单中的显式设置写在引用分组上；不能表示的任务参数明确拒绝。
    /// </summary>
    /// <exception cref="FormatException">表单中有不属于共享设置的任务参数。</exception>
    private static void ApplyReferenceSettings(PuloniaTask reference, PuloniaTaskCreationRequest request)
    {
        var parameters = TaskParameters(request);
        if (parameters.Count > 0)
            throw new FormatException("目录引用不能保存任务参数：" + string.Join("、", parameters.Properties()
                .Select(property => PuloniaTaskFieldNames.GetDisplayName(property.Name))) + "。请改用展开方式添加。");
        reference.Settings = SharedSettings(request);
    }

    /// <summary>
    /// 表单显式设置中只属于本任务的任务参数（不由共享设置覆盖的顶层参数）。
    /// </summary>
    private static JObject TaskParameters(PuloniaTaskCreationRequest request)
    {
        var shared = request.Definition.SettingBindings.Values.ToHashSet(StringComparer.Ordinal);
        return new JObject(request.Overrides.Properties().Where(property => !shared.Contains(property.Name))
            .Select(property => new JProperty(property.Name, property.Value.DeepClone())));
    }

    /// <summary>
    /// 表单显式设置中属于共享设置的项，按设置项 ID 写入节点自身的共享设置。
    /// 只处理参数路径就是顶层参数名的设置项；嵌套对象参数不在创建表单中出现。
    /// </summary>
    private static JObject SharedSettings(PuloniaTaskCreationRequest request)
    {
        var keyByPath = request.Definition.SettingBindings.ToDictionary(item => item.Value, item => item.Key, StringComparer.Ordinal);
        return new JObject(request.Overrides.Properties()
            .Where(property => keyByPath.ContainsKey(property.Name))
            .Select(property => new JProperty(keyByPath[property.Name], property.Value.DeepClone())));
    }

    /// <summary>
    /// 按添加方式放入展开后的叶子任务：扁平方式直接放入根分组，结构化方式按相对目录建立子分组。
    /// </summary>
    /// <param name="rootGroup">选中目录对应的根分组</param>
    /// <param name="mode">目录添加方式</param>
    /// <param name="relativeFile">相对选中目录的文件路径，正斜杠或反斜杠均可</param>
    /// <param name="leaf">要放入的叶子任务</param>
    private static void AddExpandedLeaf(PuloniaTask rootGroup, PuloniaTaskDirectoryImportMode mode,
        string relativeFile, PuloniaTask leaf)
    {
        if (mode == PuloniaTaskDirectoryImportMode.Flat)
        {
            rootGroup.Children.Add(leaf);
            return;
        }

        // Path.GetDirectoryName 会把正斜杠统一为当前系统分隔符。
        var relativeDirectory = Path.GetDirectoryName(relativeFile);
        var parent = rootGroup;
        if (!string.IsNullOrWhiteSpace(relativeDirectory) && relativeDirectory != ".")
        {
            foreach (var segment in relativeDirectory.Split(Path.DirectorySeparatorChar,
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var childGroup = parent.Children.FirstOrDefault(child => child.TaskType == "group"
                                                                         && child.Name == segment);
                if (childGroup is null)
                {
                    childGroup = new PuloniaTask { Name = segment, TaskType = "group" };
                    parent.Children.Add(childGroup);
                }
                parent = childGroup;
            }
        }
        parent.Children.Add(leaf);
    }
}
