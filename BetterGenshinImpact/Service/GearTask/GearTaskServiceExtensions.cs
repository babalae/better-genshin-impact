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
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddGearTaskServices(this IServiceCollection services)
    {
        // 注册核心服务
        services.AddSingleton<GearTaskStorageService>();
        services.AddSingleton<GearTaskFactory>();
        services.AddSingleton<GearTaskConverter>();
        services.AddTransient<GearTaskExecutionManager>();
        services.AddTransient<GearTaskExecutor>();
        
        return services;
    }
}
