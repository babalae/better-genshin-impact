using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Infrastructure.Network;

/// <summary>
/// 所有命名客户端共用的默认行为：补上 BetterGI 的 UA；请求失败或返回非成功状态码时以 Debug 级别记录。
/// 日志只记录方法、主机、状态码和耗时，不记录路径、查询参数和请求头（部分渠道的令牌在 URL 路径里）
/// </summary>
public sealed class BgiHttpDefaultsHandler(ILogger<BgiHttpDefaultsHandler> logger) : DelegatingHandler
{
    /// <summary>
    /// 默认 UA：BetterGI/{版本}
    /// </summary>
    private static readonly ProductInfoHeaderValue DefaultUserAgent =
        ProductInfoHeaderValue.TryParse($"BetterGI/{Global.Version}", out var userAgent)
            ? userAgent
            : new ProductInfoHeaderValue("BetterGI", "1.0");

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.UserAgent.Count == 0)
        {
            request.Headers.UserAgent.Add(DefaultUserAgent);
        }

        var start = Stopwatch.GetTimestamp();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("HTTP {Method} {Host} 返回 {StatusCode}，耗时 {ElapsedMs:F0} ms",
                    request.Method, request.RequestUri?.Host, (int)response.StatusCode, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }

            return response;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("HTTP {Method} {Host} 失败：{Error}，耗时 {ElapsedMs:F0} ms",
                request.Method, request.RequestUri?.Host, e.GetType().Name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            throw;
        }
    }
}
