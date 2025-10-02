using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Model.Gear;
using BetterGenshinImpact.ViewModel.Pages.Component;
using BetterGenshinImpact.Model.Gear.Triggers;
using BetterGenshinImpact.Service.GearTask.Model;

namespace BetterGenshinImpact.Service.GearTask;

/// <summary>
/// 齿轮触发器存储服务，负责触发器数据的 JSON 持久化
/// </summary>
public class GearTriggerStorageService
{
    private readonly ILogger<GearTriggerStorageService> _logger;
    private readonly string _triggerStoragePath;
    private readonly string _triggerFilePath;
    private readonly JsonSerializerSettings _jsonSettings;

    public GearTriggerStorageService(ILogger<GearTriggerStorageService> logger)
    {
        _logger = logger;
        _triggerStoragePath = GearTaskPaths.TaskTriggerPath;
        _triggerFilePath = Path.Combine(_triggerStoragePath, "triggers.json");
        
        // 确保目录存在
        Directory.CreateDirectory(_triggerStoragePath);
        
        // 配置 JSON 序列化设置
        _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            DateFormatString = "yyyy-MM-dd HH:mm:ss",
            NullValueHandling = NullValueHandling.Ignore
        };
    }

    /// <summary>
    /// 保存所有触发器到 JSON 文件
    /// </summary>
    /// <param name="timedTriggers">定时触发器列表</param>
    /// <param name="hotkeyTriggers">快捷键触发器列表</param>
    /// <returns></returns>
    public async Task SaveTriggersAsync(IEnumerable<GearTriggerViewModel> timedTriggers, IEnumerable<GearTriggerViewModel> hotkeyTriggers)
    {
        try
        {
            var triggerData = new GearTriggerCollectionData
            {
                TimedTriggers = new List<GearTriggerData>(),
                HotkeyTriggers = new List<GearTriggerData>()
            };

            // 转换定时触发器
            foreach (var trigger in timedTriggers)
            {
                triggerData.TimedTriggers.Add(ConvertToData(trigger));
            }

            // 转换快捷键触发器
            foreach (var trigger in hotkeyTriggers)
            {
                triggerData.HotkeyTriggers.Add(ConvertToData(trigger));
            }

            var json = JsonConvert.SerializeObject(triggerData, _jsonSettings);
            await File.WriteAllTextAsync(_triggerFilePath, json);
            
            _logger.LogInformation("触发器数据已保存到 {FilePath}，定时触发器: {TimedCount}，快捷键触发器: {HotkeyCount}", 
                _triggerFilePath, triggerData.TimedTriggers.Count, triggerData.HotkeyTriggers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存触发器数据时发生错误");
            throw;
        }
    }

    /// <summary>
    /// 从 JSON 文件加载所有触发器
    /// </summary>
    /// <returns></returns>
    public async Task<(List<GearTriggerViewModel> TimedTriggers, List<GearTriggerViewModel> HotkeyTriggers)> LoadTriggersAsync()
    {
        var timedTriggers = new List<GearTriggerViewModel>();
        var hotkeyTriggers = new List<GearTriggerViewModel>();

        try
        {
            if (!File.Exists(_triggerFilePath))
            {
                _logger.LogInformation("触发器数据文件不存在，返回空列表: {FilePath}", _triggerFilePath);
                return (timedTriggers, hotkeyTriggers);
            }

            var json = await File.ReadAllTextAsync(_triggerFilePath);
            var triggerData = JsonConvert.DeserializeObject<GearTriggerCollectionData>(json, _jsonSettings);

            if (triggerData == null)
            {
                _logger.LogWarning("无法反序列化触发器数据文件: {FilePath}", _triggerFilePath);
                return (timedTriggers, hotkeyTriggers);
            }

            // 转换定时触发器
            if (triggerData.TimedTriggers != null)
            {
                foreach (var data in triggerData.TimedTriggers)
                {
                    var viewModel = ConvertToViewModel(data);
                    if (viewModel != null)
                    {
                        timedTriggers.Add(viewModel);
                    }
                }
            }

            // 转换快捷键触发器
            if (triggerData.HotkeyTriggers != null)
            {
                foreach (var data in triggerData.HotkeyTriggers)
                {
                    var viewModel = ConvertToViewModel(data);
                    if (viewModel != null)
                    {
                        hotkeyTriggers.Add(viewModel);
                    }
                }
            }

            _logger.LogInformation("触发器数据已从 {FilePath} 加载，定时触发器: {TimedCount}，快捷键触发器: {HotkeyCount}", 
                _triggerFilePath, timedTriggers.Count, hotkeyTriggers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载触发器数据时发生错误");
        }

        return (timedTriggers, hotkeyTriggers);
    }

    /// <summary>
    /// 将 ViewModel 转换为数据模型
    /// </summary>
    private GearTriggerData ConvertToData(GearTriggerViewModel viewModel)
    {
        return new GearTriggerData
        {
            Name = viewModel.Name,
            IsEnabled = viewModel.IsEnabled,
            TriggerType = viewModel.TriggerType.ToString(),
            CronExpression = viewModel.CronExpression,
            Hotkey = viewModel.Hotkey?.ToString() ?? string.Empty,
            TaskDefinitionName = viewModel.TaskDefinitionName,
            CreatedTime = DateTime.Now,
            ModifiedTime = DateTime.Now
        };
    }

    /// <summary>
    /// 将数据模型转换为 ViewModel
    /// </summary>
    private GearTriggerViewModel? ConvertToViewModel(GearTriggerData data)
    {
        try
        {
            if (!Enum.TryParse<TriggerType>(data.TriggerType, out var triggerType))
            {
                _logger.LogWarning("无效的触发器类型: {TriggerType}", data.TriggerType);
                return null;
            }

            var viewModel = new GearTriggerViewModel(data.Name, triggerType)
            {
                IsEnabled = data.IsEnabled,
                CronExpression = data.CronExpression,
                Hotkey = string.IsNullOrEmpty(data.Hotkey) ? null : HotKey.FromString(data.Hotkey),
                TaskDefinitionName = data.TaskDefinitionName
            };

            return viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "转换触发器数据时发生错误: {TriggerName}", data.Name);
            return null;
        }
    }
}

/// <summary>
/// 触发器集合数据模型，用于 JSON 序列化
/// </summary>
public class GearTriggerCollectionData
{
    [JsonProperty("timed_triggers")]
    public List<GearTriggerData> TimedTriggers { get; set; } = new();

    [JsonProperty("hotkey_triggers")]
    public List<GearTriggerData> HotkeyTriggers { get; set; } = new();
}

/// <summary>
/// 触发器数据模型，用于 JSON 序列化
/// </summary>
public class GearTriggerData
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [JsonProperty("trigger_type")]
    public string TriggerType { get; set; } = string.Empty;

    [JsonProperty("cron_expression")]
    public string CronExpression { get; set; } = string.Empty;

    [JsonProperty("hotkey")]
    public string Hotkey { get; set; } = string.Empty;

    [JsonProperty("task_definition_name")]
    public string TaskDefinitionName { get; set; } = string.Empty;

    [JsonProperty("created_time")]
    public DateTime CreatedTime { get; set; } = DateTime.Now;

    [JsonProperty("modified_time")]
    public DateTime ModifiedTime { get; set; } = DateTime.Now;
}