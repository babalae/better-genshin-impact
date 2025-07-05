using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace BetterGenshinImpact.Service.Quartz;

/// <summary>
/// Quartz.NET 调度服务 - 管理定时任务的生命周期
/// </summary>
public class QuartzHostedService : IHostedService
{
    private readonly IScheduler _scheduler;
    private readonly ILogger<QuartzHostedService> _logger;

    public QuartzHostedService(IScheduler scheduler, ILogger<QuartzHostedService> logger)
    {
        _scheduler = scheduler;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("启动 Quartz.NET 调度服务");
            await _scheduler.Start(cancellationToken);
            _logger.LogInformation("Quartz.NET 调度服务启动成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动 Quartz.NET 调度服务失败：{Message}", ex.Message);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("停止 Quartz.NET 调度服务");
            await _scheduler.Shutdown(cancellationToken);
            _logger.LogInformation("Quartz.NET 调度服务停止成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止 Quartz.NET 调度服务失败：{Message}", ex.Message);
        }
    }
}