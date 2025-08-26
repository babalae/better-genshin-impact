using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service;

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
    
    /// <summary>
    /// 注册齿轮任务相关服务（带自定义配置）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configureOptions">配置选项</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddGearTaskServices(this IServiceCollection services, 
        Action<GearTaskServiceOptions>? configureOptions = null)
    {
        var options = new GearTaskServiceOptions();
        configureOptions?.Invoke(options);
        
        // 注册配置
        services.AddSingleton(options);
        
        // 注册核心服务
        return services.AddGearTaskServices();
    }
}

/// <summary>
/// 齿轮任务服务配置选项
/// </summary>
public class GearTaskServiceOptions
{
    /// <summary>
    /// 是否启用详细日志记录
    /// </summary>
    public bool EnableVerboseLogging { get; set; } = false;
    
    /// <summary>
    /// 任务执行超时时间（毫秒），0 表示无超时
    /// </summary>
    public int TaskExecutionTimeoutMs { get; set; } = 0;
    
    /// <summary>
    /// 是否在任务失败时继续执行后续任务
    /// </summary>
    public bool ContinueOnTaskFailure { get; set; } = false;
    
    /// <summary>
    /// 最大并发任务数，0 表示无限制
    /// </summary>
    public int MaxConcurrentTasks { get; set; } = 1;
    
    /// <summary>
    /// 任务存储路径，null 表示使用默认路径
    /// </summary>
    public string? TaskStoragePath { get; set; }
}