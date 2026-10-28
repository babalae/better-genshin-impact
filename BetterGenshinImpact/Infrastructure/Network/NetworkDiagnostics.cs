using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Infrastructure.Network;

/// <summary>
/// 连接测试目标
/// </summary>
/// <param name="Name">展示名称</param>
/// <param name="Url">请求地址，只要能收到任意 HTTP 响应就视为可以连接</param>
public sealed record NetworkTestTarget(string Name, Uri Url);

/// <summary>
/// 一个目标的连接测试结果
/// </summary>
/// <param name="Name">目标名称</param>
/// <param name="Success">是否能连接</param>
/// <param name="Elapsed">耗时</param>
/// <param name="Error">失败原因</param>
public sealed record NetworkTestResult(string Name, bool Success, TimeSpan Elapsed, string? Error)
{
    /// <summary>
    /// 展示文字，例如“✓ CNB 45 ms”“✗ GitHub：连接超时”
    /// </summary>
    public string Text => Success ? $"✓ {Name} {Elapsed.TotalMilliseconds:F0} ms" : $"✗ {Name}：{Error}";
}

/// <summary>
/// 网络连接测试：通过默认客户端（使用当前代理设置）并发请求几个常用服务
/// </summary>
public sealed class NetworkDiagnostics(IHttpClientFactory httpClientFactory)
{
    /// <summary>
    /// 单个目标的超时
    /// </summary>
    private static readonly TimeSpan TargetTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// 测试目标：国内仓库、GitHub、米游社
    /// </summary>
    public static IReadOnlyList<NetworkTestTarget> Targets { get; } =
    [
        new("CNB", new Uri("https://cnb.cool/")),
        new("GitHub", new Uri("https://api.github.com/")),
        new("米游社", new Uri("https://bbs-api.miyoushe.com/")),
    ];

    /// <summary>
    /// 并发测试全部目标
    /// </summary>
    public async Task<IReadOnlyList<NetworkTestResult>> TestAsync(CancellationToken ct)
    {
        var results = await Task.WhenAll(Targets.Select(target => TestAsync(target, ct)));
        return results;
    }

    /// <summary>
    /// 测试一个目标，把常见失败原因翻译成用户能理解的文字
    /// </summary>
    private async Task<NetworkTestResult> TestAsync(NetworkTestTarget target, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TargetTimeout);
        var start = Stopwatch.GetTimestamp();
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientNames.Default);
            using var response = await client.GetAsync(target.Url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return new NetworkTestResult(target.Name, true, Stopwatch.GetElapsedTime(start), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail("连接超时");
        }
        catch (HttpRequestException e)
        {
            return Fail(Describe(e));
        }

        NetworkTestResult Fail(string error) => new(target.Name, false, Stopwatch.GetElapsedTime(start), error);
    }

    /// <summary>
    /// 失败原因：无法解析主机、连接被拒绝或其他错误
    /// </summary>
    private static string Describe(HttpRequestException e)
    {
        if (e.InnerException is SocketException socket)
        {
            return socket.SocketErrorCode switch
            {
                SocketError.HostNotFound or SocketError.NoData => "无法解析主机",
                SocketError.ConnectionRefused => "连接被拒绝，请检查代理地址和端口",
                SocketError.TimedOut => "连接超时",
                _ => socket.Message,
            };
        }

        return e.Message;
    }
}
