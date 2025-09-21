using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear.Triggers;
using BetterGenshinImpact.Model.Gear.Triggers.QuartzJob;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace BetterGenshinImpact.Service;

/// <summary>
/// Quartz.NET 调度器服务
/// </summary>
public class QuartzSchedulerService(ILogger<QuartzSchedulerService> logger, ISchedulerFactory schedulerFactory) : IHostedService
{
    private ILogger<QuartzSchedulerService> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        List<QuartzCronGearTrigger> allData = new List<QuartzCronGearTrigger>();
        
        Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> jobsDictionary = new();
        foreach (var data in allData)
        {
            if (string.IsNullOrEmpty(data.CronExpression))
            {
                continue;
            }
            
            var triggerSet = new HashSet<ITrigger>();
            IJobDetail job = JobBuilder.Create<QuartzGearTaskJob>()
                .UsingJobData("jobData", data.ToString())
                .Build();
            ITrigger trigger = TriggerBuilder.Create()
                .WithCronSchedule(data.CronExpression)
                .ForJob(job)
                .Build();
            triggerSet.Add(trigger);
            jobsDictionary.Add(job, triggerSet);
        }

        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        await scheduler.ScheduleJobs(jobsDictionary, replace: true, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
    }
}