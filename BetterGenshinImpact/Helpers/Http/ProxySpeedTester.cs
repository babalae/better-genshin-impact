using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Helpers.Http;

public class ProxySpeedTester
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static readonly List<string> ProxyUrls =
    [
        "{0}",
        "https://mirror.ghproxy.com/{0}",
        "https://hub.gitmirror.com/{0}",
        "https://ghproxy.cc/{0}",
        "https://www.ghproxy.cc/{0}",
        "https://ghproxy.cn/{0}",
        "https://ghproxy.net/{0}"
    ];

    /// <summary>
    /// 获得最快的代理地址
    /// </summary>
    /// <param name="target"></param>
    /// <returns>最快的代理地址,结果</returns>
    public static async Task<(string, string)> GetFastestProxyAsync(string target)
    {
        return await GetFastestProxyAsync(ProxyUrls, target);
    }

    public static async Task<(string, string)> GetFastestProxyAsync(List<string> proxyAddresses, string target)
    {
        var tasks = new List<Task<(string, string)>>();
        var cts = new CancellationTokenSource();

        foreach (var proxy in proxyAddresses)
        {
            // 如果目标地址为空且代理地址为默认地址，则跳过
            if (string.IsNullOrEmpty(target) && proxy == "{0}")
            {
                continue;
            }

            tasks.Add(TestProxyAsync(proxy, target, cts.Token));
        }

        var firstCompletedTask = await Task.WhenAny(tasks);
        await cts.CancelAsync(); // 取消所有其他请求

        try
        {
            return await firstCompletedTask; // 返回第一个完成的代理地址
        }
        catch (OperationCanceledException)
        {
            return (string.Empty, string.Empty); // 如果第一个任务被取消，返回空
        }
    }

    private static async Task<(string, string)> TestProxyAsync(string proxyAddress, string target, CancellationToken cancellationToken)
    {
        // 模拟代理测试请求
        var response = await _httpClient.GetAsync(string.Format(proxyAddress, target), cancellationToken);
        response.EnsureSuccessStatusCode();
        return (proxyAddress, response.Content.ToString()!);
    }
}
