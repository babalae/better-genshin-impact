using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear.Triggers;
using BetterGenshinImpact.Service.GearTask.Model;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.GearTask;

public interface ITriggerHistoryService
{
    event EventHandler HistoryChanged;
    Task AddRecordAsync(TriggerExecutionRecord record);
    Task UpdateRecordAsync(TriggerExecutionRecord record);
    Task<List<TriggerExecutionRecord>> GetHistoryAsync();
    Task ClearHistoryAsync();
}

public class TriggerHistoryService : ITriggerHistoryService
{
    private readonly string _historyFilePath;
    private readonly ILogger<TriggerHistoryService> _logger;
    private readonly JsonSerializerSettings _jsonSettings;
    private List<TriggerExecutionRecord> _cachedHistory;
    private const int MaxHistoryCount = 200;

    public event EventHandler? HistoryChanged;

    public TriggerHistoryService(ILogger<TriggerHistoryService> logger)
    {
        _logger = logger;
        // 使用 GearTaskPaths 定义的路径
        var historyDir = GearTaskPaths.TaskHistoryPath;
        _historyFilePath = Path.Combine(historyDir, "trigger_history.json");
        
        // 确保目录存在
        if (!Directory.Exists(historyDir))
        {
            Directory.CreateDirectory(historyDir);
        }

        _jsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            DateFormatString = "yyyy-MM-dd HH:mm:ss",
            NullValueHandling = NullValueHandling.Ignore
        };
        
        _cachedHistory = new List<TriggerExecutionRecord>();
        // 初始化时加载
        _ = LoadHistoryFromFileAsync();
    }

    private async Task LoadHistoryFromFileAsync()
    {
        try
        {
            if (File.Exists(_historyFilePath))
            {
                var json = await File.ReadAllTextAsync(_historyFilePath);
                var history = JsonConvert.DeserializeObject<List<TriggerExecutionRecord>>(json, _jsonSettings);
                if (history != null)
                {
                    lock (_cachedHistory)
                    {
                        _cachedHistory = history;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载触发器历史记录失败");
        }
    }

    private async Task SaveHistoryAsync()
    {
        try
        {
            string json;
            lock (_cachedHistory)
            {
                // 保持最大记录数
                if (_cachedHistory.Count > MaxHistoryCount)
                {
                    _cachedHistory = _cachedHistory.OrderByDescending(x => x.StartTime).Take(MaxHistoryCount).ToList();
                }
                json = JsonConvert.SerializeObject(_cachedHistory, _jsonSettings);
            }
            await File.WriteAllTextAsync(_historyFilePath, json);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存触发器历史记录失败");
        }
    }

    public async Task AddRecordAsync(TriggerExecutionRecord record)
    {
        lock (_cachedHistory)
        {
            _cachedHistory.Insert(0, record);
        }
        await SaveHistoryAsync();
    }

    public async Task UpdateRecordAsync(TriggerExecutionRecord record)
    {
        lock (_cachedHistory)
        {
            var index = _cachedHistory.FindIndex(r => r.Id == record.Id);
            if (index != -1)
            {
                _cachedHistory[index] = record;
            }
        }
        await SaveHistoryAsync();
    }

    public async Task<List<TriggerExecutionRecord>> GetHistoryAsync()
    {
        // 如果缓存为空但文件存在，尝试重新加载
        if (_cachedHistory.Count == 0 && File.Exists(_historyFilePath))
        {
             await LoadHistoryFromFileAsync();
        }
        
        lock (_cachedHistory)
        {
            return _cachedHistory.OrderByDescending(x => x.StartTime).ToList();
        }
    }

    public async Task ClearHistoryAsync()
    {
        lock (_cachedHistory)
        {
            _cachedHistory.Clear();
        }
        await SaveHistoryAsync();
    }
}
