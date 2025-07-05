using System;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Script.Group;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Logging;
using Quartz;

namespace BetterGenshinImpact.Service.Quartz;

/// <summary>
/// 脚本执行任务 - 用于 Quartz.NET 调度执行
/// </summary>
public class ScriptExecutionJob : IJob
{
    private readonly ILogger<ScriptExecutionJob> _logger;
    private readonly IScriptService _scriptService;

    public ScriptExecutionJob(ILogger<ScriptExecutionJob> logger, IScriptService scriptService)
    {
        _logger = logger;
        _scriptService = scriptService;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            var jobDataMap = context.JobDetail.JobDataMap;
            var scriptGroupName = jobDataMap.GetString("ScriptGroupName");
            var scriptGroupData = jobDataMap.GetString("ScriptGroupData");
            
            if (string.IsNullOrEmpty(scriptGroupName) || string.IsNullOrEmpty(scriptGroupData))
            {
                _logger.LogError("任务执行失败：缺少必要的参数 ScriptGroupName 或 ScriptGroupData");
                return;
            }

            _logger.LogInformation("开始执行定时任务：{ScriptGroupName}", scriptGroupName);

            // 反序列化脚本组数据
            var scriptGroup = ScriptGroup.FromJson(scriptGroupData);
            if (scriptGroup == null)
            {
                _logger.LogError("任务执行失败：无法反序列化脚本组数据");
                return;
            }

            // 获取有效的项目列表
            var enabledProjects = scriptGroup.Projects.Where(p => p.Status == "Enabled").ToList();
            if (enabledProjects.Count == 0)
            {
                _logger.LogWarning("脚本组 {ScriptGroupName} 没有启用的项目", scriptGroupName);
                return;
            }

            // 执行脚本组
            await _scriptService.RunMulti(enabledProjects, scriptGroupName, null);
            
            _logger.LogInformation("定时任务执行完成：{ScriptGroupName}", scriptGroupName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "定时任务执行异常：{Message}", ex.Message);
        }
    }
}