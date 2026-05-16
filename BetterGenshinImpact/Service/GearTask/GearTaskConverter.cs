using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using BetterGenshinImpact.Model.Gear;
using BetterGenshinImpact.Model.Gear.Tasks;
using BetterGenshinImpact.Model.Gear.Parameter;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Service.GearTask;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 齿轮任务转换器，负责将 GearTaskData 转换为可执行的 BaseGearTask
/// </summary>
public class GearTaskConverter
{
    private const string PathingRepoFolderPlaceholder = "{pathingRepoFolder}";

    private readonly ILogger<GearTaskConverter> _logger;
    private readonly GearTaskFactory _taskFactory;
    private readonly object _mirrorLock = new();
    private bool _pathingRepoMirrorInitialized;
    private readonly HashSet<string> _exportedPathingRepoDirectories = new(StringComparer.OrdinalIgnoreCase);

    public GearTaskConverter(ILogger<GearTaskConverter> logger, GearTaskFactory taskFactory)
    {
        _logger = logger;
        _taskFactory = taskFactory;
    }

    /// <summary>
    /// 将 GearTaskDefinitionData 转换为可执行的任务列表
    /// </summary>
    /// <param name="taskDefinition">任务定义数据</param>
    /// <returns>可执行的任务列表</returns>
    public async Task<List<BaseGearTask>> ConvertTaskDefinitionAsync(GearTaskDefinitionData taskDefinition)
    {
        if (taskDefinition?.RootTask == null)
        {
            throw new ArgumentException("任务定义或根任务不能为空");
        }

        var tasks = new List<BaseGearTask>();
        
        try
        {
            _logger.LogInformation("开始转换任务定义: {TaskDefinitionName}", taskDefinition.Name);
            
            var rootTask = await ConvertTaskDataAsync(taskDefinition.RootTask);
            tasks.Add(rootTask);
            
            _logger.LogInformation("任务定义转换完成: {TaskDefinitionName}, 共 {TaskCount} 个任务", 
                taskDefinition.Name, CountTotalTasks(rootTask));
            
            return tasks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "转换任务定义失败: {TaskDefinitionName}", taskDefinition.Name);
            throw;
        }
    }

    /// <summary>
    /// 将单个 GearTaskData 转换为 BaseGearTask（包括子任务）
    /// </summary>
    /// <param name="taskData">任务数据</param>
    /// <param name="parent">父任务</param>
    /// <param name="inheritedGroupConfig">从父任务组继承下来的配置</param>
    /// <returns>转换后的任务</returns>
    public async Task<BaseGearTask> ConvertTaskDataAsync(GearTaskData taskData, BaseGearTask? parent = null, ScriptGroupConfig? inheritedGroupConfig = null)
    {
        if (taskData == null)
        {
            throw new ArgumentNullException(nameof(taskData));
        }

        try
        {
            var currentGroupConfig = GearTaskGroupConfigHelper.Deserialize(taskData.GroupConfigJson) ?? inheritedGroupConfig;
            BaseGearTask task;
            
            // 如果是目录类型或者任务被禁用，创建容器任务
            if (taskData.IsDirectory || !taskData.IsEnabled)
            {
                if (taskData.IsDirectory && taskData.IsEnabled)
                {
                    MaterializePathingReferenceIfNeeded(taskData);
                }

                task = new ContainerGearTask
                {
                    Name = taskData.Name,
                    Type = taskData.TaskType ?? "container",
                    Enabled = taskData.IsEnabled,
                    Father = parent
                };
                
                _logger.LogDebug("创建容器任务: {TaskName} (IsDirectory: {IsDirectory}, IsEnabled: {IsEnabled})", 
                    taskData.Name, taskData.IsDirectory, taskData.IsEnabled);
            }
            else
            {
                // 使用工厂创建具体的任务实例
                var preparedTaskData = PrepareTaskDataForExecution(taskData, currentGroupConfig);
                task = await _taskFactory.CreateTaskAsync(preparedTaskData);
                task.Father = parent;
                
                _logger.LogDebug("创建具体任务: {TaskName} ({TaskType})", taskData.Name, taskData.TaskType);
            }

            // 递归处理子任务
            if (taskData.Children?.Count > 0)
            {
                _logger.LogDebug("处理子任务: {TaskName}, 子任务数量: {ChildCount}", 
                    taskData.Name, taskData.Children.Count);
                
                foreach (var childData in taskData.Children)
                {
                    try
                    {
                        var childTask = await ConvertTaskDataAsync(childData, task, currentGroupConfig);
                        task.Children.Add(childTask);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "转换子任务失败: {ChildTaskName}, 父任务: {ParentTaskName}", 
                            childData.Name, taskData.Name);
                        
                        // 创建错误任务占位符
                        var errorTask = new ErrorGearTask(ex.Message)
                        {
                            Name = childData.Name,
                            Type = "error",
                            Enabled = false,
                            Father = task
                        };
                        task.Children.Add(errorTask);
                    }
                }
            }

            return task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "转换任务数据失败: {TaskName}", taskData.Name);
            
            // 返回错误任务
            return new ErrorGearTask(ex.Message)
            {
                Name = taskData.Name,
                Type = "error",
                Enabled = false,
                Father = parent
            };
        }
    }

    /// <summary>
    /// 验证任务数据的完整性
    /// </summary>
    /// <param name="taskData">任务数据</param>
    /// <returns>验证结果</returns>
    public TaskValidationResult ValidateTaskData(GearTaskData taskData)
    {
        var result = new TaskValidationResult { IsValid = true };
        var errors = new List<string>();
        var warnings = new List<string>();

        if (taskData == null)
        {
            errors.Add("任务数据不能为空");
            result.IsValid = false;
        }
        else
        {
            // 验证基本属性
            if (string.IsNullOrWhiteSpace(taskData.Name))
            {
                errors.Add("任务名称不能为空");
                result.IsValid = false;
            }

            if (!taskData.IsDirectory && string.IsNullOrWhiteSpace(taskData.TaskType))
            {
                errors.Add("非目录任务必须指定类型");
                result.IsValid = false;
            }

            // 验证任务类型
            if (!string.IsNullOrWhiteSpace(taskData.TaskType) && 
                !GearTaskFactory.IsTaskTypeSupported(taskData.TaskType))
            {
                errors.Add($"不支持的任务类型: {taskData.TaskType}");
                result.IsValid = false;
            }

            // 验证参数
            if (!taskData.IsDirectory && taskData.IsEnabled)
            {
                try
                {
                    ValidateTaskParameters(taskData);
                }
                catch (Exception ex)
                {
                    warnings.Add($"参数验证警告: {ex.Message}");
                }
            }

            // 递归验证子任务
            if (taskData.Children?.Count > 0)
            {
                foreach (var child in taskData.Children)
                {
                    var childResult = ValidateTaskData(child);
                    if (!childResult.IsValid)
                    {
                        errors.AddRange(childResult.Errors.Select(e => $"子任务 '{child.Name}': {e}"));
                        result.IsValid = false;
                    }
                    warnings.AddRange(childResult.Warnings.Select(w => $"子任务 '{child.Name}': {w}"));
                }
            }
        }

        result.Errors = errors;
        result.Warnings = warnings;
        return result;
    }

    /// <summary>
    /// 验证任务参数
    /// </summary>
    /// <param name="taskData">任务数据</param>
    private void ValidateTaskParameters(GearTaskData taskData)
    {
        if (taskData.Parameters == null)
        {
            return;
        }

        // 根据任务类型验证参数
        switch (taskData.TaskType?.ToLowerInvariant())
        {
            case "javascript":
                ValidateJsonParameter<JavascriptGearTaskParams>(taskData.Parameters, "FolderName");
                break;
            case "pathing":
                ValidateJsonParameter<PathingGearTaskParams>(taskData.Parameters, "Path");
                break;
            case "csharp":
            case "csharpreflection":
                ValidateJsonParameter<CSharpReflectionGearTaskParams>(taskData.Parameters, "MethodPath");
                break;
        }
    }

    /// <summary>
    /// 验证 JSON 参数中的必需字段
    /// </summary>
    private void ValidateJsonParameter<T>(object parameters, string requiredField)
    {
        try
        {
            string json = parameters is string str ? str : JsonConvert.SerializeObject(parameters);
            var obj = JsonConvert.DeserializeObject<T>(json);
            
            var property = typeof(T).GetProperty(requiredField);
            if (property != null)
            {
                var value = property.GetValue(obj);
                if (value == null || (value is string strValue && string.IsNullOrWhiteSpace(strValue)))
                {
                    throw new ArgumentException($"缺少必需参数: {requiredField}");
                }
            }
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"参数 JSON 格式错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 统计任务总数（包括子任务）
    /// </summary>
    /// <param name="task">根任务</param>
    /// <returns>任务总数</returns>
    private int CountTotalTasks(BaseGearTask task)
    {
        int count = 1;
        if (task.Children?.Count > 0)
        {
            count += task.Children.Sum(CountTotalTasks);
        }
        return count;
    }

    private void MaterializePathingReferenceIfNeeded(GearTaskData taskData)
    {
        if (taskData.Children.Count > 0 || string.IsNullOrWhiteSpace(taskData.Path))
        {
            return;
        }

        if (!TryExtractPathingRepoRelativePath(taskData.Path, out var repoRelativePath))
        {
            return;
        }

        var children = BuildPathingReferenceChildren(repoRelativePath);
        if (children.Count == 0)
        {
            _logger.LogWarning("引用目录为空或不存在: {Path}", taskData.Path);
            return;
        }

        taskData.Children = children;
        _logger.LogDebug("已展开地图追踪引用节点: {NodeName}, 子节点数量: {ChildCount}", taskData.Name, children.Count);
    }

    private List<GearTaskData> BuildPathingReferenceChildren(string repoRelativePath)
    {
        var result = new List<GearTaskData>();
        EnsurePathingRepoDirectoryExported(repoRelativePath);
        var children = ScriptRepoUpdater.Instance.GetChildrenFromCenterRepo(repoRelativePath);
        foreach (var entry in children)
        {
            if (entry.IsDirectory)
            {
                result.Add(new GearTaskData
                {
                    Name = entry.Name,
                    TaskType = string.Empty,
                    IsEnabled = true,
                    IsDirectory = true,
                    Path = BuildPathingPlaceholderPath(entry.RelativePath, true),
                    Parameters = "{}",
                });
                continue;
            }

            if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parameters = new PathingGearTaskParams
            {
                Path = BuildPathingPlaceholderPath(entry.RelativePath, false),
            };
            result.Add(new GearTaskData
            {
                Name = Path.GetFileNameWithoutExtension(entry.Name),
                TaskType = "Pathing",
                IsEnabled = true,
                IsDirectory = false,
                Path = BuildPathingPlaceholderPath(entry.RelativePath, false),
                Parameters = JsonConvert.SerializeObject(parameters),
            });
        }

        return result;
    }

    private GearTaskData PrepareTaskDataForExecution(GearTaskData taskData, ScriptGroupConfig? groupConfig)
    {
        if (string.Equals(taskData.TaskType, "Javascript", StringComparison.OrdinalIgnoreCase))
        {
            return PrepareJavascriptTaskDataForExecution(taskData, groupConfig);
        }

        if (!string.Equals(taskData.TaskType, "Pathing", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(taskData.TaskType, "Shell", StringComparison.OrdinalIgnoreCase) &&
                groupConfig?.EnableShellConfig == true)
            {
                return CreateCopiedTaskData(taskData, JsonConvert.SerializeObject(groupConfig.ShellConfig));
            }

            return taskData;
        }

        return PreparePathingTaskDataForExecution(taskData, groupConfig);
    }

    private GearTaskData PrepareJavascriptTaskDataForExecution(GearTaskData taskData, ScriptGroupConfig? groupConfig)
    {
        var parameters = DeserializeJavascriptParams(taskData.Parameters);
        if (string.IsNullOrWhiteSpace(parameters.FolderName))
        {
            parameters.FolderName = ExtractTaskFolderName(taskData.Path);
        }

        if (parameters.PathingPartyConfig == null && groupConfig != null)
        {
            parameters.PathingPartyConfig = groupConfig.PathingConfig;
        }

        return CreateCopiedTaskData(taskData, JsonConvert.SerializeObject(parameters));
    }

    private GearTaskData PreparePathingTaskDataForExecution(GearTaskData taskData, ScriptGroupConfig? groupConfig)
    {
        var parameters = DeserializePathingParams(taskData.Parameters);
        if (parameters.PathingPartyConfig == null && groupConfig != null)
        {
            parameters.PathingPartyConfig = groupConfig.PathingConfig;
        }

        if (!string.IsNullOrWhiteSpace(parameters.Path)
            && !TryExtractPathingRepoRelativePath(parameters.Path, out _))
        {
            return CreateCopiedTaskData(taskData, JsonConvert.SerializeObject(parameters));
        }

        if (string.IsNullOrWhiteSpace(taskData.Path))
        {
            return CreateCopiedTaskData(taskData, JsonConvert.SerializeObject(parameters));
        }

        if (TryExtractPathingRepoRelativePath(taskData.Path, out var repoRelativePath)
            && repoRelativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            parameters.Path = GetPathingExecutionFilePath(repoRelativePath);
        }
        else
        {
            parameters.Path = taskData.Path.Trim().TrimEnd('\\', '/');
        }

        return CreateCopiedTaskData(taskData, JsonConvert.SerializeObject(parameters));
    }

    private PathingGearTaskParams DeserializePathingParams(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
        {
            return new PathingGearTaskParams();
        }

        try
        {
            return JsonConvert.DeserializeObject<PathingGearTaskParams>(parametersJson) ?? new PathingGearTaskParams();
        }
        catch
        {
            return new PathingGearTaskParams();
        }
    }

    private JavascriptGearTaskParams DeserializeJavascriptParams(string? parametersJson)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
        {
            return new JavascriptGearTaskParams();
        }

        try
        {
            return JsonConvert.DeserializeObject<JavascriptGearTaskParams>(parametersJson) ?? new JavascriptGearTaskParams();
        }
        catch
        {
            return new JavascriptGearTaskParams();
        }
    }

    private static GearTaskData CreateCopiedTaskData(GearTaskData taskData, string parametersJson)
    {
        return new GearTaskData
        {
            Name = taskData.Name,
            TaskType = taskData.TaskType,
            Path = taskData.Path,
            IsEnabled = taskData.IsEnabled,
            IsDirectory = taskData.IsDirectory,
            IsExpanded = taskData.IsExpanded,
            GroupConfigJson = taskData.GroupConfigJson,
            Parameters = parametersJson,
            CreatedTime = taskData.CreatedTime,
            ModifiedTime = taskData.ModifiedTime,
            Priority = taskData.Priority,
            Children = taskData.Children
        };
    }

    private static string ExtractTaskFolderName(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return string.Empty;
        }

        var trimmedPath = sourcePath.Trim().TrimEnd('\\', '/');
        if (string.IsNullOrWhiteSpace(trimmedPath))
        {
            return string.Empty;
        }

        var lastSeparatorIndex = Math.Max(trimmedPath.LastIndexOf('\\'), trimmedPath.LastIndexOf('/'));
        return lastSeparatorIndex >= 0 && lastSeparatorIndex < trimmedPath.Length - 1
            ? trimmedPath[(lastSeparatorIndex + 1)..]
            : trimmedPath;
    }

    private static string BuildPathingPlaceholderPath(string repoRelativePath, bool isDirectory)
    {
        var normalized = repoRelativePath.Replace('/', Path.DirectorySeparatorChar);
        if (normalized.StartsWith("pathing\\", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["pathing\\".Length..];
        }
        else if (string.Equals(normalized, "pathing", StringComparison.OrdinalIgnoreCase))
        {
            normalized = string.Empty;
        }

        var path = string.IsNullOrEmpty(normalized)
            ? PathingRepoFolderPlaceholder
            : $@"{PathingRepoFolderPlaceholder}\{normalized}";

        if (isDirectory && !path.EndsWith('\\'))
        {
            path += "\\";
        }

        return path;
    }

    private bool TryExtractPathingRepoRelativePath(string sourcePath, out string repoRelativePath)
    {
        repoRelativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return false;
        }

        var normalized = sourcePath.Replace('\\', '/').Trim();
        if (!normalized.StartsWith(PathingRepoFolderPlaceholder, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = normalized[PathingRepoFolderPlaceholder.Length..].Trim('/');
        repoRelativePath = string.IsNullOrEmpty(relative)
            ? "pathing"
            : $"pathing/{relative}";
        return true;
    }

    private string GetPathingExecutionFilePath(string repoRelativeJsonPath)
    {
        // Pathing 文件改为按需导出，避免首次转换时全量镜像仓库

        var normalized = repoRelativeJsonPath.Replace('\\', '/').Trim('/');
        var target = GetPathingMirrorPath(normalized);
        if (File.Exists(target))
        {
            return target;
        }

        lock (_mirrorLock)
        {
            if (File.Exists(target))
            {
                return target;
            }

            var exportDirectory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(exportDirectory))
            {
                Directory.CreateDirectory(exportDirectory);
            }

            if (!ScriptRepoUpdater.Instance.ExportFileFromCenterRepo(normalized, target))
            {
                throw new FileNotFoundException($"仓库中不存在地图追踪文件: {normalized}");
            }

            return target;
        }

        // 兜底：镜像中不存在时按需写入
        var content = ScriptRepoUpdater.Instance.ReadFileFromCenterRepo(normalized);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new FileNotFoundException($"仓库中不存在地图追踪文件: {normalized}");
        }

        var dir = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(target, content);
        return target;
    }

    private void EnsurePathingRepoDirectoryExported(string repoRelativePath)
    {
        var normalized = repoRelativePath.Replace('\\', '/').Trim('/');
        if (IsPathingRepoDirectoryExported(normalized))
        {
            return;
        }

        lock (_mirrorLock)
        {
            if (IsPathingRepoDirectoryExported(normalized))
            {
                return;
            }

            var targetDirectory = GetPathingMirrorPath(normalized);
            if (string.Equals(normalized, "pathing", StringComparison.OrdinalIgnoreCase))
            {
                var mirrorRoot = GetPathingRepoMirrorRoot();
                if (Directory.Exists(mirrorRoot))
                {
                    Directory.Delete(mirrorRoot, true);
                }
            }
            else if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, true);
            }

            Directory.CreateDirectory(targetDirectory);
            ScriptRepoUpdater.Instance.ExportFilesFromCenterRepo(normalized, targetDirectory, ".json");
            _exportedPathingRepoDirectories.Add(normalized);
        }
    }

    private bool IsPathingRepoDirectoryExported(string repoRelativePath)
    {
        foreach (var exportedDirectory in _exportedPathingRepoDirectories)
        {
            if (string.Equals(exportedDirectory, repoRelativePath, StringComparison.OrdinalIgnoreCase)
                || repoRelativePath.StartsWith(exportedDirectory + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetPathingMirrorPath(string repoRelativePath)
    {
        var normalized = repoRelativePath.Replace('\\', '/').Trim('/');
        var relativeUnderPathing = normalized.StartsWith("pathing/", StringComparison.OrdinalIgnoreCase)
            ? normalized["pathing/".Length..]
            : normalized == "pathing"
                ? string.Empty
                : normalized;

        var mirrorRoot = GetPathingRepoMirrorRoot();
        return string.IsNullOrEmpty(relativeUnderPathing)
            ? mirrorRoot
            : Path.Combine(mirrorRoot, relativeUnderPathing.Replace('/', Path.DirectorySeparatorChar));
    }

    private void EnsurePathingRepoMirrorInitialized()
    {
        if (_pathingRepoMirrorInitialized)
        {
            return;
        }

        lock (_mirrorLock)
        {
            if (_pathingRepoMirrorInitialized)
            {
                return;
            }

            var mirrorRoot = GetPathingRepoMirrorRoot();
            if (Directory.Exists(mirrorRoot))
            {
                Directory.Delete(mirrorRoot, true);
            }
            Directory.CreateDirectory(mirrorRoot);

            MirrorPathingJsonRecursively("pathing", mirrorRoot);
            _pathingRepoMirrorInitialized = true;
        }
    }

    private void MirrorPathingJsonRecursively(string repoRelativePath, string localPath)
    {
        var entries = ScriptRepoUpdater.Instance.GetChildrenFromCenterRepo(repoRelativePath);
        foreach (var entry in entries)
        {
            if (entry.IsDirectory)
            {
                var dirPath = Path.Combine(localPath, entry.Name);
                Directory.CreateDirectory(dirPath);
                MirrorPathingJsonRecursively(entry.RelativePath, dirPath);
                continue;
            }

            if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = ScriptRepoUpdater.Instance.ReadFileFromCenterRepo(entry.RelativePath);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var filePath = Path.Combine(localPath, entry.Name);
            File.WriteAllText(filePath, content);
        }
    }

    private static string GetPathingRepoMirrorRoot()
    {
        return Global.Absolute(@"User\Temp\GearTask\PathingRepoMirror");
    }
}

/// <summary>
/// 任务验证结果
/// </summary>
public class TaskValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
