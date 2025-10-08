using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Model.Gear.Tasks;

/// <summary>
/// 为了和其他Task做区分，使用Gear(齿轮)来作为前缀命名调度器内定义的任务
/// </summary>
public abstract class BaseGearTask : ObservableObject
{
    [JsonIgnore]
    private readonly ILogger<BaseGearTask> _logger = App.GetLogger<BaseGearTask>();

    /// <summary>
    /// 任务名称
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 任务类型
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 任务的文件位置，如果有
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 任务是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 父节点
    /// </summary>
    [JsonIgnore]
    public BaseGearTask? Father { get; set; }

    /// <summary>
    /// 子节点
    /// </summary>
    public List<BaseGearTask> Children { get; set; } = [];

    /// <summary>
    /// 执行任务
    /// </summary>
    public async Task Execute(CancellationToken ct)
    {
        var stopwatch = new Stopwatch();
        try
        {
            _logger.LogInformation("------------------------------");
            stopwatch.Start();
            await Run(ct);
        }
        catch (NormalEndException e)
        {
            throw;
        }
        catch (TaskCanceledException e)
        {
            _logger.LogInformation("取消执行配置组: {Msg}", e.Message);
            throw;
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "执行脚本时发生异常");
            _logger.LogError("执行脚本时发生异常: {Msg}", e.Message);
        }
        finally
        {
            stopwatch.Stop();
            var elapsedTime = TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds);
            _logger.LogInformation("→ 脚本执行结束: {Name}, 耗时: {Minutes}分{Seconds:0.000}秒", Name,
                elapsedTime.Hours * 60 + elapsedTime.Minutes, elapsedTime.TotalSeconds % 60);
            _logger.LogInformation("------------------------------");
        }
    }

    /// <summary>
    /// 执行任务
    /// </summary>
    public abstract Task Run(CancellationToken ct);
    

    public static BaseGearTask ReadFileToBaseGearTasks(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("任务文件路径不能为空", nameof(path));
        }
        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<BaseGearTask>(json) ?? throw new InvalidOperationException("任务数据读取结果为空");
    }
    
}
