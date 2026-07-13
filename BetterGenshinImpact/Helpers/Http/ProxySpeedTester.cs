using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.Helpers.Http;

public class ProxySpeedTester
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// 代理地址已经无法使用在 babalae 的组织下
    /// </summary>
    public static readonly List<string> ProxyUrls =
    [
        "{0}",
        "https://hub.gitmirror.com/{0}",
        "https://ghproxy.net/{0}",
        "https://ghproxy.cc/{0}",
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
        List<string> urlList = [];
        foreach (var proxy in proxyAddresses)
        {
            // 如果目标地址为空且代理地址为默认地址，则跳过
            if (string.IsNullOrEmpty(target) && proxy == "{0}")
            {
                continue;
            }

            urlList.Add(string.Format(proxy, target));
        }

        var (fastUrl, resContent) = await GetFastestUrlAsync(urlList);

        foreach (var proxy in proxyAddresses)
        {
            // 如果目标地址为空且代理地址为默认地址，则跳过
            if (string.IsNullOrEmpty(target) && proxy == "{0}")
            {
                continue;
            }

            if (fastUrl == string.Format(proxy, target))
            {
                return (proxy, resContent);
            }
        }

        return (string.Empty, string.Empty); // 如果没有成功的结果，返回空
    }

    public static async Task<(string, string)> GetFastestUrlAsync(List<string> urlList)
    {
        var tasks = new List<Task<(string, string, bool)>>(); // 修改为包含成功标志的元组
        var cts = new CancellationTokenSource();

        foreach (var url in urlList)
        {
            tasks.Add(TestUrlAsync(url, cts.Token));
        }

        while (tasks.Count > 0)
        {
            var firstCompletedTask = await Task.WhenAny(tasks);
            tasks.Remove(firstCompletedTask);

            var result = await firstCompletedTask;
            if (result.Item3) // 检查是否成功
            {
                await cts.CancelAsync(); // 取消所有其他请求
                return (result.Item1, result.Item2); // 返回第一个成功的地址
            }
        }

        return (string.Empty, string.Empty); // 如果没有成功的结果，返回空
    }

    private static async Task<(string, string, bool)> TestUrlAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            // 模拟代理测试请求
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            Debug.WriteLine("SpeedTester", $"Info: {url} - {response.StatusCode} - {content.Length} bytes");
            var json = JObject.Parse(content);
            return (url, content, true);
        }
        catch (Exception e)
        {
            Debug.WriteLine("SpeedTester", $"Info: {url} - {e.Message}");
            return (url, e.Message, false);
        }
    }
}