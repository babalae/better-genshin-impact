using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear;
using BetterGenshinImpact.Model.Gear.Tasks;
using BetterGenshinImpact.Model.Gear.Parameter;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 齿轮任务转换器，负责将 GearTaskData 转换为可执行的 BaseGearTask
/// </summary>
public class GearTaskConverter
{
    private readonly ILogger<GearTaskConverter> _logger;
    private readonly GearTaskFactory _taskFactory;

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
    /// <returns>转换后的任务</returns>
    public async Task<BaseGearTask> ConvertTaskDataAsync(GearTaskData taskData, BaseGearTask? parent = null)
    {
        if (taskData == null)
        {
            throw new ArgumentNullException(nameof(taskData));
        }

        try
        {
            BaseGearTask task;
            
            // 如果是目录类型或者任务被禁用，创建容器任务
            if (taskData.IsDirectory || !taskData.IsEnabled)
            {
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
                task = await _taskFactory.CreateTaskAsync(taskData);
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
                        var childTask = await ConvertTaskDataAsync(childData, task);
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

/// <summary>
/// 容器任务，用于目录类型或禁用的任务
/// </summary>
internal class ContainerGearTask : BaseGearTask
{
    public override async Task Run(CancellationToken ct)
    {
        // 容器任务本身不执行任何操作，只是作为子任务的容器
        await Task.CompletedTask;
    }
}

/// <summary>
/// 错误任务，用于转换失败的任务占位符
/// </summary>
internal class ErrorGearTask : BaseGearTask
{
    private readonly string _errorMessage;

    public ErrorGearTask(string errorMessage)
    {
        _errorMessage = errorMessage;
    }

    public override async Task Run(CancellationToken ct)
    {
        throw new InvalidOperationException($"任务转换失败: {_errorMessage}");
    }
}