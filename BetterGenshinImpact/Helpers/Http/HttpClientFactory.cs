using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;

namespace BetterGenshinImpact.Helpers.Http;

public static class HttpClientFactory
{
    private static readonly ConcurrentDictionary<string, Lazy<HttpClient>> Clients = new();

    /// <summary>获取进程共享客户端，调用方不能释放或在使用后修改其配置。</summary>
    public static HttpClient GetClient(string key, Func<HttpClient> factory)
    {
        return Clients.GetOrAdd(key, _ => new Lazy<HttpClient>(factory, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public static HttpClient GetCommonSendClient()
    {
        return GetClient("common", () => CreateClient());
    }

    /// <summary>创建由调用方负责释放的独立客户端，保留独立的 Cookie 和请求头。</summary>
    public static HttpClient CreateClient(TimeSpan? timeout = null,
        Action<SocketsHttpHandler>? configureHandler = null, bool useSystemProxy = false)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = true,
            Proxy = useSystemProxy ? ProxyService.Instance.OriginalProxy : ProxyService.Instance.Proxy
        };
        try
        {
            configureHandler?.Invoke(handler);
            var client = new HttpClient(handler);
            if (timeout.HasValue)
            {
                client.Timeout = timeout.Value;
            }

            return client;
        }
        catch
        {
            handler.Dispose();
            throw;
        }
    }
}
