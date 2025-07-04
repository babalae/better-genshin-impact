using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Group;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Quartz;

/// <summary>
/// 动态任务管理示例服务
/// 提供动态添加、删除、管理定时任务的示例方法
/// </summary>
public class DynamicTaskExampleService
{
    private readonly SchedulerManager _schedulerManager;
    private readonly ILogger<DynamicTaskExampleService> _logger;

    public DynamicTaskExampleService(SchedulerManager schedulerManager, ILogger<DynamicTaskExampleService> logger)
    {
        _schedulerManager = schedulerManager;
        _logger = logger;
    }

    /// <summary>
    /// 示例：为脚本组添加定时任务
    /// </summary>
    /// <param name="scriptGroup">脚本组</param>
    /// <param name="cronExpression">Cron表达式</param>
    /// <returns>任务是否添加成功</returns>
    public async Task<bool> AddScriptGroupScheduleAsync(ScriptGroup scriptGroup, string cronExpression)
    {
        try
        {
            // 检查脚本组是否有效
            if (scriptGroup?.Projects == null || !scriptGroup.Projects.Any())
            {
                _logger.LogWarning("脚本组 {ScriptGroupName} 没有有效的项目", scriptGroup?.Name);
                return false;
            }

            // 检查是否有启用的项目
            var enabledProjects = scriptGroup.Projects.Where(p => p.Status == "Enabled").ToList();
            if (enabledProjects.Count == 0)
            {
                _logger.LogWarning("脚本组 {ScriptGroupName} 没有启用的项目", scriptGroup.Name);
                return false;
            }

            // 添加定时任务
            return await _schedulerManager.AddScheduledTaskAsync(scriptGroup, cronExpression);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加脚本组定时任务失败：{Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 示例：批量添加多个脚本组的定时任务
    /// </summary>
    /// <param name="scriptGroups">脚本组列表</param>
    /// <param name="defaultCronExpression">默认Cron表达式</param>
    /// <returns>成功添加的任务数量</returns>
    public async Task<int> AddMultipleScriptGroupSchedulesAsync(List<ScriptGroup> scriptGroups, string defaultCronExpression = "0 0 0 * * ? *")
    {
        int successCount = 0;
        
        foreach (var scriptGroup in scriptGroups)
        {
            try
            {
                // 使用脚本组的调度配置或默认配置
                var cronExpression = !string.IsNullOrEmpty(scriptGroup.Projects?.FirstOrDefault()?.Schedule) 
                    ? SchedulerManager.ConvertScheduleToCron(scriptGroup.Projects.FirstOrDefault()!.Schedule)
                    : defaultCronExpression;

                if (await AddScriptGroupScheduleAsync(scriptGroup, cronExpression))
                {
                    successCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加脚本组 {ScriptGroupName} 定时任务失败：{Message}", scriptGroup.Name, ex.Message);
            }
        }

        _logger.LogInformation("批量添加定时任务完成，成功添加 {SuccessCount}/{TotalCount} 个任务", successCount, scriptGroups.Count);
        return successCount;
    }

    /// <summary>
    /// 示例：根据脚本组名称删除定时任务
    /// </summary>
    /// <param name="scriptGroupName">脚本组名称</param>
    /// <returns>删除的任务数量</returns>
    public async Task<int> RemoveScriptGroupSchedulesAsync(string scriptGroupName)
    {
        try
        {
            var allTasks = await _schedulerManager.GetAllScheduledTasksAsync();
            var tasksToRemove = allTasks.Where(t => t.ScriptGroupName == scriptGroupName).ToList();

            int removedCount = 0;
            foreach (var task in tasksToRemove)
            {
                if (await _schedulerManager.RemoveScheduledTaskAsync(task.JobName))
                {
                    removedCount++;
                }
            }

            _logger.LogInformation("删除脚本组 {ScriptGroupName} 的定时任务完成，成功删除 {RemovedCount} 个任务", scriptGroupName, removedCount);
            return removedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除脚本组 {ScriptGroupName} 定时任务失败：{Message}", scriptGroupName, ex.Message);
            return 0;
        }
    }

    /// <summary>
    /// 示例：创建每日执行的定时任务
    /// </summary>
    /// <param name="scriptGroup">脚本组</param>
    /// <param name="hour">执行小时（0-23）</param>
    /// <param name="minute">执行分钟（0-59）</param>
    /// <returns>任务是否创建成功</returns>
    public async Task<bool> CreateDailyTaskAsync(ScriptGroup scriptGroup, int hour = 0, int minute = 0)
    {
        if (hour < 0 || hour > 23 || minute < 0 || minute > 59)
        {
            _logger.LogError("时间参数无效：小时 {Hour}，分钟 {Minute}", hour, minute);
            return false;
        }

        var cronExpression = $"0 {minute} {hour} * * ? *";
        return await AddScriptGroupScheduleAsync(scriptGroup, cronExpression);
    }

    /// <summary>
    /// 示例：创建每周执行的定时任务
    /// </summary>
    /// <param name="scriptGroup">脚本组</param>
    /// <param name="dayOfWeek">星期几（1=周一，7=周日）</param>
    /// <param name="hour">执行小时（0-23）</param>
    /// <param name="minute">执行分钟（0-59）</param>
    /// <returns>任务是否创建成功</returns>
    public async Task<bool> CreateWeeklyTaskAsync(ScriptGroup scriptGroup, int dayOfWeek, int hour = 0, int minute = 0)
    {
        if (dayOfWeek < 1 || dayOfWeek > 7 || hour < 0 || hour > 23 || minute < 0 || minute > 59)
        {
            _logger.LogError("参数无效：星期 {DayOfWeek}，小时 {Hour}，分钟 {Minute}", dayOfWeek, hour, minute);
            return false;
        }

        var dayNames = new[] { "", "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
        var cronExpression = $"0 {minute} {hour} ? * {dayNames[dayOfWeek]} *";
        return await AddScriptGroupScheduleAsync(scriptGroup, cronExpression);
    }

    /// <summary>
    /// 示例：创建间隔执行的定时任务
    /// </summary>
    /// <param name="scriptGroup">脚本组</param>
    /// <param name="intervalMinutes">间隔分钟数</param>
    /// <returns>任务是否创建成功</returns>
    public async Task<bool> CreateIntervalTaskAsync(ScriptGroup scriptGroup, int intervalMinutes)
    {
        if (intervalMinutes <= 0)
        {
            _logger.LogError("间隔时间无效：{IntervalMinutes} 分钟", intervalMinutes);
            return false;
        }

        var cronExpression = $"0 0/{intervalMinutes} * * * ? *";
        return await AddScriptGroupScheduleAsync(scriptGroup, cronExpression);
    }

    /// <summary>
    /// 示例：获取定时任务报告
    /// </summary>
    /// <returns>定时任务报告</returns>
    public async Task<ScheduledTaskReport> GetScheduledTaskReportAsync()
    {
        try
        {
            var allTasks = await _schedulerManager.GetAllScheduledTasksAsync();
            
            return new ScheduledTaskReport
            {
                TotalTasks = allTasks.Count,
                ActiveTasks = allTasks.Count(t => t.NextFireTime.HasValue),
                TasksByScriptGroup = allTasks.GroupBy(t => t.ScriptGroupName)
                    .ToDictionary(g => g.Key, g => g.Count()),
                NextExecutions = allTasks.Where(t => t.NextFireTime.HasValue)
                    .OrderBy(t => t.NextFireTime)
                    .Take(10)
                    .Select(t => new NextExecution
                    {
                        JobName = t.JobName,
                        ScriptGroupName = t.ScriptGroupName,
                        NextFireTime = t.NextFireTime!.Value,
                        CronExpression = t.CronExpression
                    })
                    .ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取定时任务报告失败：{Message}", ex.Message);
            return new ScheduledTaskReport();
        }
    }

    /// <summary>
    /// 示例：清理所有定时任务
    /// </summary>
    /// <returns>清理的任务数量</returns>
    public async Task<int> ClearAllScheduledTasksAsync()
    {
        try
        {
            var allTasks = await _schedulerManager.GetAllScheduledTasksAsync();
            int clearedCount = 0;

            foreach (var task in allTasks)
            {
                if (await _schedulerManager.RemoveScheduledTaskAsync(task.JobName))
                {
                    clearedCount++;
                }
            }

            _logger.LogInformation("清理定时任务完成，成功清理 {ClearedCount} 个任务", clearedCount);
            return clearedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理定时任务失败：{Message}", ex.Message);
            return 0;
        }
    }
}

/// <summary>
/// 定时任务报告
/// </summary>
public class ScheduledTaskReport
{
    public int TotalTasks { get; set; }
    public int ActiveTasks { get; set; }
    public Dictionary<string, int> TasksByScriptGroup { get; set; } = new();
    public List<NextExecution> NextExecutions { get; set; } = new();
}

/// <summary>
/// 下次执行信息
/// </summary>
public class NextExecution
{
    public string JobName { get; set; } = "";
    public string ScriptGroupName { get; set; } = "";
    public DateTime NextFireTime { get; set; }
    public string CronExpression { get; set; } = "";
}