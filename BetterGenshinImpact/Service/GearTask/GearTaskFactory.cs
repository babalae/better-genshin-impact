using System;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear;
using BetterGenshinImpact.Model.Gear.Tasks;
using BetterGenshinImpact.Model.Gear.Parameter;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System.IO;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 齿轮任务工厂，根据任务类型和参数创建对应的任务实例
/// </summary>
public class GearTaskFactory
{
    private readonly ILogger<GearTaskFactory> _logger;
    private readonly IServiceProvider _serviceProvider;

    public GearTaskFactory(ILogger<GearTaskFactory> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 根据 GearTaskData 创建对应的任务实例
    /// </summary>
    /// <param name="taskData">任务数据</param>
    /// <returns>创建的任务实例</returns>
    public async Task<BaseGearTask> CreateTaskAsync(GearTaskData taskData)
    {
        if (string.IsNullOrWhiteSpace(taskData.TaskType))
        {
            throw new ArgumentException($"任务类型不能为空: {taskData.Name}");
        }

        try
        {
            var task = taskData.TaskType.ToLowerInvariant() switch
            {
                "javascript" => await CreateJavascriptTaskAsync(taskData),
                "pathing" => await CreatePathingTaskAsync(taskData),
                "csharp" or "csharpreflection" => await CreateCSharpReflectionTaskAsync(taskData),
                "keymouse" => await CreateKeyMouseTaskAsync(taskData),
                "shell" => await CreateShellTaskAsync(taskData),
                _ => throw new NotSupportedException($"不支持的任务类型: {taskData.TaskType}")
            };

            // 设置基本属性
            task.Name = taskData.Name;
            task.Type = taskData.TaskType;
            task.Enabled = taskData.IsEnabled;

            _logger.LogDebug("成功创建任务实例: {TaskName} ({TaskType})", taskData.Name, taskData.TaskType);
            return task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建任务实例失败: {TaskName} ({TaskType})", taskData.Name, taskData.TaskType);
            throw;
        }
    }

    /// <summary>
    /// 创建 JavaScript 任务
    /// </summary>
    private async Task<BaseGearTask> CreateJavascriptTaskAsync(GearTaskData taskData)
    {
        var parameters = DeserializeParameters<JavascriptGearTaskParams>(taskData.Parameters);
        
        if (string.IsNullOrWhiteSpace(parameters.FolderName))
        {
            throw new ArgumentException($"JavaScript 任务缺少 FolderName 参数: {taskData.Name}");
        }

        return new JavascriptGearTask(parameters);
    }

    /// <summary>
    /// 创建路径任务
    /// </summary>
    private async Task<BaseGearTask> CreatePathingTaskAsync(GearTaskData taskData)
    {
        var parameters = DeserializeParameters<PathingGearTaskParams>(taskData.Parameters);
        
        if (string.IsNullOrWhiteSpace(parameters.Path))
        {
            throw new ArgumentException($"Pathing 任务缺少 Path 参数: {taskData.Name}");
        }

        return new PathingGearTask(parameters);
    }

    /// <summary>
    /// 创建 C# 反射任务
    /// </summary>
    private async Task<BaseGearTask> CreateCSharpReflectionTaskAsync(GearTaskData taskData)
    {
        var parameters = DeserializeParameters<CSharpReflectionGearTaskParams>(taskData.Parameters);
        
        if (string.IsNullOrWhiteSpace(parameters.MethodPath))
        {
            throw new ArgumentException($"C# 反射任务缺少 MethodPath 参数: {taskData.Name}");
        }

        return new CSharpReflectionGearTask(parameters);
    }

    /// <summary>
    /// 创建键鼠任务
    /// </summary>
    private async Task<BaseGearTask> CreateKeyMouseTaskAsync(GearTaskData taskData)
    {
        // KeyMouse 任务需要文件路径
        string filePath = string.Empty;
        
        if (taskData.Parameters != null)
        {
            // 尝试从参数中获取路径
            if (taskData.Parameters is string pathStr)
            {
                filePath = pathStr;
            }
            else if (taskData.Parameters.ToString() is string paramStr)
            {
                // 尝试解析 JSON 对象中的路径
                try
                {
                    var paramObj = JsonConvert.DeserializeObject<dynamic>(paramStr);
                    filePath = paramObj?.Path?.ToString() ?? paramObj?.FilePath?.ToString() ?? string.Empty;
                }
                catch
                {
                    filePath = paramStr; // 如果解析失败，直接使用字符串
                }
            }
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException($"KeyMouse 任务缺少文件路径参数: {taskData.Name}");
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"KeyMouse 任务文件不存在: {filePath}");
        }

        return new KeyMouseGearTask(filePath);
    }

    /// <summary>
    /// 创建 Shell 任务
    /// </summary>
    private async Task<BaseGearTask> CreateShellTaskAsync(GearTaskData taskData)
    {
        ShellConfig? shellConfig = null;
        
        if (taskData.Parameters != null)
        {
            try
            {
                shellConfig = DeserializeParameters<ShellConfig>(taskData.Parameters);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析 Shell 配置失败，使用默认配置: {TaskName}", taskData.Name);
                shellConfig = new ShellConfig();
            }
        }

        return new ShellGearTask(shellConfig);
    }

    /// <summary>
    /// 反序列化参数对象
    /// </summary>
    /// <typeparam name="T">参数类型</typeparam>
    /// <param name="parameters">参数对象</param>
    /// <returns>反序列化后的参数</returns>
    private T DeserializeParameters<T>(object? parameters) where T : class, new()
    {
        if (parameters == null)
        {
            return new T();
        }

        try
        {
            // 如果已经是目标类型，直接返回
            if (parameters is T directCast)
            {
                return directCast;
            }

            // 尝试 JSON 反序列化
            string json;
            if (parameters is string jsonStr)
            {
                json = jsonStr;
            }
            else
            {
                json = JsonConvert.SerializeObject(parameters);
            }

            var result = JsonConvert.DeserializeObject<T>(json);
            return result ?? new T();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "反序列化参数失败，使用默认参数: {ParameterType}", typeof(T).Name);
            return new T();
        }
    }

    /// <summary>
    /// 获取支持的任务类型列表
    /// </summary>
    /// <returns>支持的任务类型</returns>
    public static string[] GetSupportedTaskTypes()
    {
        return new[]
        {
            "javascript",
            "pathing", 
            "csharp",
            "csharpreflection",
            "keymouse",
            "shell"
        };
    }

    /// <summary>
    /// 检查任务类型是否受支持
    /// </summary>
    /// <param name="taskType">任务类型</param>
    /// <returns>是否支持</returns>
    public static bool IsTaskTypeSupported(string taskType)
    {
        if (string.IsNullOrWhiteSpace(taskType))
            return false;

        var supportedTypes = GetSupportedTaskTypes();
        return Array.Exists(supportedTypes, t => 
            string.Equals(t, taskType, StringComparison.OrdinalIgnoreCase));
    }
}