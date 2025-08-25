using BetterGenshinImpact.Model.Gear;
using BetterGenshinImpact.ViewModel.Pages.Component;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 齿轮任务存储服务，负责任务定义的 JSON 持久化
/// </summary>
public class GearTaskStorageService
{
    private readonly ILogger<GearTaskStorageService> _logger;
    private readonly string _taskStoragePath;
    private readonly JsonSerializerSettings _jsonSettings;

    public GearTaskStorageService(ILogger<GearTaskStorageService> logger)
    {
        _logger = logger;
        _taskStoragePath = Path.Combine(Global.Absolute("User"), "task_v2", "list");
        
        // 确保目录存在
        Directory.CreateDirectory(_taskStoragePath);
        
        // 配置 JSON 序列化设置
        _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            DateFormatString = "yyyy-MM-dd HH:mm:ss",
            NullValueHandling = NullValueHandling.Ignore
        };
    }

    /// <summary>
    /// 保存任务定义到 JSON 文件
    /// </summary>
    /// <param name="taskDefinition">任务定义</param>
    /// <returns></returns>
    public async Task SaveTaskDefinitionAsync(GearTaskDefinitionViewModel taskDefinition)
    {
        try
        {
            var data = ConvertToData(taskDefinition);
            var fileName = GetSafeFileName(taskDefinition.Name) + ".json";
            var filePath = Path.Combine(_taskStoragePath, fileName);
            
            var json = JsonConvert.SerializeObject(data, _jsonSettings);
            await File.WriteAllTextAsync(filePath, json);
            
            _logger.LogInformation("任务定义 '{TaskName}' 已保存到 {FilePath}", taskDefinition.Name, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存任务定义 '{TaskName}' 时发生错误", taskDefinition.Name);
            throw;
        }
    }

    /// <summary>
    /// 从 JSON 文件加载任务定义
    /// </summary>
    /// <param name="fileName">文件名（不含扩展名）</param>
    /// <returns></returns>
    public async Task<GearTaskDefinitionViewModel?> LoadTaskDefinitionAsync(string fileName)
    {
        try
        {
            var filePath = Path.Combine(_taskStoragePath, fileName + ".json");
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("任务定义文件不存在: {FilePath}", filePath);
                return null;
            }
            
            var json = await File.ReadAllTextAsync(filePath);
            var data = JsonConvert.DeserializeObject<GearTaskDefinitionData>(json, _jsonSettings);
            
            if (data == null)
            {
                _logger.LogWarning("无法反序列化任务定义文件: {FilePath}", filePath);
                return null;
            }
            
            var viewModel = ConvertToViewModel(data);
            _logger.LogInformation("任务定义 '{TaskName}' 已从 {FilePath} 加载", data.Name, filePath);
            
            return viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载任务定义文件 '{FileName}' 时发生错误", fileName);
            throw;
        }
    }

    /// <summary>
    /// 加载所有任务定义
    /// </summary>
    /// <returns></returns>
    public async Task<List<GearTaskDefinitionViewModel>> LoadAllTaskDefinitionsAsync()
    {
        var taskDefinitions = new List<GearTaskDefinitionViewModel>();
        
        try
        {
            var jsonFiles = Directory.GetFiles(_taskStoragePath, "*.json");
            
            foreach (var filePath in jsonFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    var data = JsonConvert.DeserializeObject<GearTaskDefinitionData>(json, _jsonSettings);
                    
                    if (data != null)
                    {
                        var viewModel = ConvertToViewModel(data);
                        taskDefinitions.Add(viewModel);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "加载任务定义文件 '{FilePath}' 时发生错误", filePath);
                }
            }
            
            _logger.LogInformation("已加载 {Count} 个任务定义", taskDefinitions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载任务定义列表时发生错误");
        }
        
        return taskDefinitions;
    }

    /// <summary>
    /// 删除任务定义文件
    /// </summary>
    /// <param name="taskName">任务名称</param>
    /// <returns></returns>
    public async Task DeleteTaskDefinitionAsync(string taskName)
    {
        try
        {
            var fileName = GetSafeFileName(taskName) + ".json";
            var filePath = Path.Combine(_taskStoragePath, fileName);
            
            if (File.Exists(filePath))
            {
                await Task.Run(() => File.Delete(filePath));
                _logger.LogInformation("任务定义文件已删除: {FilePath}", filePath);
            }
            else
            {
                _logger.LogWarning("要删除的任务定义文件不存在: {FilePath}", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除任务定义 '{TaskName}' 时发生错误", taskName);
            throw;
        }
    }

    /// <summary>
    /// 重命名任务定义文件
    /// </summary>
    /// <param name="oldName">旧名称</param>
    /// <param name="newName">新名称</param>
    /// <returns></returns>
    public async Task RenameTaskDefinitionAsync(string oldName, string newName)
    {
        try
        {
            var oldFileName = GetSafeFileName(oldName) + ".json";
            var newFileName = GetSafeFileName(newName) + ".json";
            var oldFilePath = Path.Combine(_taskStoragePath, oldFileName);
            var newFilePath = Path.Combine(_taskStoragePath, newFileName);
            
            if (File.Exists(oldFilePath))
            {
                await Task.Run(() => File.Move(oldFilePath, newFilePath));
                _logger.LogInformation("任务定义文件已重命名: {OldPath} -> {NewPath}", oldFilePath, newFilePath);
            }
            else
            {
                _logger.LogWarning("要重命名的任务定义文件不存在: {FilePath}", oldFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重命名任务定义 '{OldName}' -> '{NewName}' 时发生错误", oldName, newName);
            throw;
        }
    }

    /// <summary>
    /// 将 ViewModel 转换为数据模型
    /// </summary>
    /// <param name="viewModel">视图模型</param>
    /// <returns></returns>
    private GearTaskDefinitionData ConvertToData(GearTaskDefinitionViewModel viewModel)
    {
        return new GearTaskDefinitionData
        {
            Name = viewModel.Name,
            Description = viewModel.Description,
            CreatedTime = viewModel.CreatedTime,
            ModifiedTime = viewModel.ModifiedTime,
            RootTask = viewModel.RootTask != null ? ConvertTaskToData(viewModel.RootTask) : null
        };
    }

    /// <summary>
    /// 将任务 ViewModel 转换为数据模型
    /// </summary>
    /// <param name="viewModel">任务视图模型</param>
    /// <returns></returns>
    private GearTaskData ConvertTaskToData(GearTaskViewModel viewModel)
    {
        return new GearTaskData
        {
            Name = viewModel.Name,
            Description = viewModel.Description,
            TaskType = viewModel.TaskType,
            IsEnabled = viewModel.IsEnabled,
            IsDirectory = viewModel.IsDirectory,
            Parameters = viewModel.Parameters,
            CreatedTime = viewModel.CreatedTime,
            ModifiedTime = viewModel.ModifiedTime,
            Priority = viewModel.Priority,
            Tags = viewModel.Tags,
            Children = viewModel.Children.Select(ConvertTaskToData).ToList()
        };
    }

    /// <summary>
    /// 将数据模型转换为 ViewModel
    /// </summary>
    /// <param name="data">数据模型</param>
    /// <returns></returns>
    private GearTaskDefinitionViewModel ConvertToViewModel(GearTaskDefinitionData data)
    {
        var viewModel = new GearTaskDefinitionViewModel
        {
            Name = data.Name,
            Description = data.Description,
            CreatedTime = data.CreatedTime,
            ModifiedTime = data.ModifiedTime,
            RootTask = data.RootTask != null ? ConvertTaskToViewModel(data.RootTask) : null
        };
        
        return viewModel;
    }

    /// <summary>
    /// 将任务数据模型转换为 ViewModel
    /// </summary>
    /// <param name="data">任务数据模型</param>
    /// <returns></returns>
    private GearTaskViewModel ConvertTaskToViewModel(GearTaskData data)
    {
        var viewModel = new GearTaskViewModel
        {
            Name = data.Name,
            Description = data.Description,
            TaskType = data.TaskType,
            IsEnabled = data.IsEnabled,
            IsDirectory = data.IsDirectory,
            Parameters = data.Parameters,
            CreatedTime = data.CreatedTime,
            ModifiedTime = data.ModifiedTime,
            Priority = data.Priority,
            Tags = data.Tags
        };
        
        // 递归转换子任务
        foreach (var childData in data.Children)
        {
            viewModel.Children.Add(ConvertTaskToViewModel(childData));
        }
        
        return viewModel;
    }

    /// <summary>
    /// 获取安全的文件名（移除非法字符）
    /// </summary>
    /// <param name="name">原始名称</param>
    /// <returns></returns>
    private string GetSafeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(safeName) ? "unnamed_task" : safeName;
    }
}