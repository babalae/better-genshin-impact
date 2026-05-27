using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Model.Gear.Triggers;
using BetterGenshinImpact.Model.Gear.Triggers.QuartzJob;
using BetterGenshinImpact.ViewModel.Pages.Component;
using BetterGenshinImpact.Service.GearTask;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;

namespace BetterGenshinImpact.Service;

/// <summary>
/// Quartz.NET 调度器服务
/// </summary>
public class QuartzSchedulerService(ILogger<QuartzSchedulerService> logger,
    ISchedulerFactory schedulerFactory,
    GearTriggerStorageService triggerStorageService) : IHostedService
{
    private readonly ILogger<QuartzSchedulerService> _logger = logger;
    private const string GearGroupName = "gear";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var (timedTriggers, _) = await triggerStorageService.LoadTriggersAsync();
        await SyncTimedTriggersAsync(timedTriggers, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task SyncTimedTriggersAsync(IEnumerable<GearTriggerViewModel> timedTriggers, CancellationToken cancellationToken = default)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        var existingJobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(GearGroupName), cancellationToken);

        if (existingJobKeys.Count > 0)
        {
            await scheduler.DeleteJobs(existingJobKeys.ToList(), cancellationToken);
        }

        var jobsDictionary = BuildJobsDictionary(timedTriggers);
        if (jobsDictionary.Count > 0)
        {
            await scheduler.ScheduleJobs(jobsDictionary, replace: true, cancellationToken);
        }
    }

    private static Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> BuildJobsDictionary(IEnumerable<GearTriggerViewModel> timedTriggers)
    {
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
                .WithIdentity($"job:{data.Name}", GearGroupName)
                .UsingJobData(jobDataMap)
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity($"trigger:{data.Name}", GearGroupName)
                .WithCronSchedule(data.CronExpression)
                .ForJob(job)
                .Build();

            jobsDictionary.Add(job, new HashSet<ITrigger> { trigger });
        }

        return jobsDictionary;
    }

}
