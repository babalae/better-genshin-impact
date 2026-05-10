using BetterGenshinImpact.Service.GearTask.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.Service.GearTask;

/// <summary>
/// 齿轮任务服务注册扩展方法
/// </summary>
public static class GearTaskServiceExtensions
{
    /// <summary>
    /// 注册齿轮任务相关服务
    /// </summary>
    public static IServiceCollection AddGearTaskServices(this IServiceCollection services)
    {
        services.AddSingleton<GearTaskStorageService>();
        services.AddSingleton<GearTaskFactory>();
        services.AddSingleton<GearTaskConverter>();

        services.AddSingleton<IGearTaskEventBus, GearTaskEventBus>();
        services.AddSingleton<IGearTaskHistoryStore, GearTaskHistoryStore>();
        services.AddHostedService<GearTaskHistoryRecorder>();

        services.AddSingleton<IGearTaskExecutionRunner, GearTaskExecutionRunner>();
        services.AddSingleton<GearTaskExecutor>();

        return services;
    }
}
