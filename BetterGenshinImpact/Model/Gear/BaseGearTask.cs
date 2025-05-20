using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Model.Gear;

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
    /// 任务的位置相对 User 目录下的路径
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// 任务是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 父节点
    /// </summary>
    public BaseGearTask? Father { get; set; }

    /// <summary>
    /// 子节点
    /// </summary>
    public List<BaseGearTask> Children { get; set; } = [];

    /// <summary>
    /// 执行任务
    /// </summary>
    public async Task Execute()
    {
        var stopwatch = new Stopwatch();
        try
        {
            _logger.LogInformation("------------------------------");
            stopwatch.Start();
            await Run();
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
    public abstract Task Run();
}