using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Helpers.Http;

public static class ProxyConnectionTester
{
    public static async Task<ProxyConnectionTestResult> TestAsync(Uri proxy, Uri target,
        CancellationToken cancellationToken = default, TimeSpan? timeout = null)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
        using var client = HttpClientFactory.CreateClient(Timeout.InfiniteTimeSpan, handler =>
        {
            // 测试始终使用输入框的独立代理，不修改当前全局配置，也不携带已有 Cookie。
            handler.Proxy = new WebProxy(proxy);
            handler.UseCookies = false;
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, target);
        request.Headers.UserAgent.ParseAdd("BetterGI-Network-Test/1.0");
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token).ConfigureAwait(false);
            return new(target, stopwatch.ElapsedMilliseconds, response.StatusCode);
        }
        catch (OperationCanceledException)
        {
            return new(target, stopwatch.ElapsedMilliseconds, Canceled: cancellationToken.IsCancellationRequested,
                TimedOut: !cancellationToken.IsCancellationRequested);
        }
        catch (HttpRequestException ex)
        {
            return new(target, stopwatch.ElapsedMilliseconds, Error: ex.Message);
        }
    }
}

public sealed record ProxyConnectionTestResult(Uri Target, long ElapsedMilliseconds,
    HttpStatusCode? StatusCode = null, string? Error = null, bool Canceled = false, bool TimedOut = false);
