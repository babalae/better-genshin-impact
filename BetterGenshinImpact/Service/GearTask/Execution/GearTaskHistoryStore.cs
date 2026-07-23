using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.GearTask.Execution;

/// <summary>
/// GearTask 历史记录存储接口。
/// </summary>
public interface IGearTaskHistoryStore
{
    Task SaveAsync(GearTaskExecutionRecord record);

    Task<GearTaskExecutionRecord?> LoadAsync(string taskDefinitionFileKey, string recordId);

    Task<IReadOnlyList<GearTaskExecutionRecord>> LoadLatestAsync(string taskDefinitionFileKey, int count);

    Task TrimAsync(string taskDefinitionFileKey, int keepCount);
}

/// <summary>
/// 基于磁盘 JSON 文件的历史记录存储。
/// 一个执行记录对应一个文件，按任务定义分目录存放。
/// </summary>
public sealed class GearTaskHistoryStore : IGearTaskHistoryStore
{
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        DateFormatString = "yyyy-MM-dd HH:mm:ss.fff",
    };

    private readonly string _historyRootPath = Path.Combine(AppContext.BaseDirectory, "User", "task_v2", "history");

    public GearTaskHistoryStore()
    {
        Directory.CreateDirectory(_historyRootPath);
    }

    public async Task SaveAsync(GearTaskExecutionRecord record)
    {
        var filePath = GetRecordFilePath(record.TaskDefinitionFileKey, record.RecordId, record.StartTime);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        // 先写临时文件再覆盖正式文件，避免进程中断时把历史记录写坏。
        var json = JsonConvert.SerializeObject(record, _jsonSettings);
        var tempPath = filePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, filePath, true);
    }

    public async Task<GearTaskExecutionRecord?> LoadAsync(string taskDefinitionFileKey, string recordId)
    {
        var directory = GetDefinitionDirectory(taskDefinitionFileKey);
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var filePath = Directory.GetFiles(directory, $"*_{recordId}.json").OrderByDescending(f => f).FirstOrDefault();
        if (filePath == null)
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(filePath);
        return JsonConvert.DeserializeObject<GearTaskExecutionRecord>(json, _jsonSettings);
    }

    public async Task<IReadOnlyList<GearTaskExecutionRecord>> LoadLatestAsync(string taskDefinitionFileKey, int count)
    {
        var directory = GetDefinitionDirectory(taskDefinitionFileKey);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var files = Directory.GetFiles(directory, "*.json")
            .OrderByDescending(Path.GetFileNameWithoutExtension)
            .Take(count)
            .ToList();

        var list = new List<GearTaskExecutionRecord>(files.Count);
        foreach (var file in files)
        {
            var json = await File.ReadAllTextAsync(file);
            var record = JsonConvert.DeserializeObject<GearTaskExecutionRecord>(json, _jsonSettings);
            if (record != null)
            {
                list.Add(record);
            }
        }

        return list;
    }

    public Task TrimAsync(string taskDefinitionFileKey, int keepCount)
    {
        var directory = GetDefinitionDirectory(taskDefinitionFileKey);
        if (!Directory.Exists(directory))
        {
            return Task.CompletedTask;
        }

        // 文件名以开始时间开头，按文件名倒序即可近似得到最近执行记录。
        var files = Directory.GetFiles(directory, "*.json")
            .OrderByDescending(Path.GetFileNameWithoutExtension)
            .Skip(keepCount)
            .ToList();

        foreach (var file in files)
        {
            File.Delete(file);
        }

        return Task.CompletedTask;
    }

    public string GetDefinitionDirectory(string taskDefinitionFileKey)
    {
        return Path.Combine(_historyRootPath, taskDefinitionFileKey);
    }

    private string GetRecordFilePath(string taskDefinitionFileKey, string recordId, DateTime startTime)
    {
        return Path.Combine(GetDefinitionDirectory(taskDefinitionFileKey), $"{startTime:yyyyMMddHHmmss}_{recordId}.json");
    }
}
