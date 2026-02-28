using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear.Triggers;
using BetterGenshinImpact.Model.Gear.Triggers.QuartzJob;
using BetterGenshinImpact.Service.GearTask;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace BetterGenshinImpact.Service;

/// <summary>
/// Quartz.NET 调度器服务
/// </summary>
public class QuartzSchedulerService(ILogger<QuartzSchedulerService> logger,
    ISchedulerFactory schedulerFactory,
    GearTriggerStorageService triggerStorageService) : IHostedService
{
    private readonly ILogger<QuartzSchedulerService> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var (timedTriggers, _) = await triggerStorageService.LoadTriggersAsync();

        var allData = timedTriggers
            .Where(t => t.IsEnabled && !string.IsNullOrWhiteSpace(t.CronExpression))
            .Select(t => t.ToTrigger())
            .OfType<QuartzCronGearTrigger>()
            .ToList();

        var jobsDictionary = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>();

        foreach (var data in allData)
        {
            if (string.IsNullOrEmpty(data.CronExpression) || !data.IsEnabled)
            {
                continue;
            }

            var jobDataMap = new JobDataMap
            {
                { "TriggerName", data.Name },
                { "TriggerId", System.Guid.NewGuid().ToString() },
                { "ShouldInterruptOthers", false },
                { "TaskDefinitionName", data.TaskDefinitionName }
            };

            var job = JobBuilder.Create<QuartzGearTaskJob>()
                .WithIdentity($"job:{data.Name}", "gear")
                .UsingJobData(jobDataMap)
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity($"trigger:{data.Name}", "gear")
                .WithCronSchedule(data.CronExpression)
                .ForJob(job)
                .Build();

            jobsDictionary.Add(job, new HashSet<ITrigger> { trigger });
        }

        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        if (jobsDictionary.Count > 0)
        {
            await scheduler.ScheduleJobs(jobsDictionary, replace: true, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

}