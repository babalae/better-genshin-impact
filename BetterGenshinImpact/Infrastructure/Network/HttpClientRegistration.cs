using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.Infrastructure.Network;

/// <summary>
/// 注册程序代理、命名 HttpClient 和网络诊断。各命名客户端的超时与重定向策略只在这里定义
/// </summary>
public static class HttpClientRegistration
{
    /// <summary>
    /// 注册网络基础设施
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="proxy">程序代理，调用方应已用它接管 HttpClient.DefaultProxy</param>
    public static IServiceCollection AddBgiHttpClients(this IServiceCollection services, AppWebProxy proxy)
    {
        services.AddSingleton(proxy);
        services.AddTransient<BgiHttpDefaultsHandler>();
        services.AddSingleton<NetworkDiagnostics>();

        AddClient(services, proxy, HttpClientNames.Default, TimeSpan.FromSeconds(30));
        AddClient(services, proxy, HttpClientNames.GameApi, TimeSpan.FromSeconds(30));
        AddClient(services, proxy, HttpClientNames.Images, TimeSpan.FromSeconds(15));
        AddClient(services, proxy, HttpClientNames.Download, Timeout.InfiniteTimeSpan, automaticDecompression: false);
        AddClient(services, proxy, HttpClientNames.Notification, TimeSpan.FromSeconds(30));
        AddClient(services, proxy, HttpClientNames.Script, TimeSpan.FromSeconds(60), allowAutoRedirect: false);
        AddClient(services, proxy, HttpClientNames.AiProvider, TimeSpan.FromSeconds(20), allowAutoRedirect: false);
        return services;
    }

    /// <summary>
    /// 注册一个命名客户端；移除框架默认的请求日志，避免每个请求都写两条 Information 日志
    /// </summary>
    private static void AddClient(IServiceCollection services, AppWebProxy proxy, string name, TimeSpan timeout,
        bool automaticDecompression = true, bool allowAutoRedirect = true)
    {
        services.AddHttpClient(name, client => client.Timeout = timeout)
            .ConfigurePrimaryHttpMessageHandler(() => BgiHttpHandlers.CreatePrimary(proxy, automaticDecompression, allowAutoRedirect))
            .AddHttpMessageHandler<BgiHttpDefaultsHandler>()
            .RemoveAllLoggers();
    }
}
