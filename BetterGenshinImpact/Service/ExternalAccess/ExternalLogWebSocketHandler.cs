using BetterGenshinImpact.Service.ExternalAccess.Logging;
using Microsoft.AspNetCore.Http;
using Serilog.Events;
using System;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.ExternalAccess;

/// <summary>
/// 处理实时日志 WebSocket 连接、历史回放和连接清理。
/// </summary>
public sealed class ExternalLogWebSocketHandler(ExternalLogHub logHub)
{
    /// <summary>
    /// WebSocket 消息使用的 camelCase JSON 配置。
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 接受并维护一个实时日志 WebSocket 连接。
    /// </summary>
    /// <param name="context">当前 HTTP 请求上下文。</param>
    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("该端点仅接受 WebSocket 连接。", context.RequestAborted);
            return;
        }

        if (!TryParseMinimumLevel(context.Request.Query["minimumLevel"], out var minimumLevel))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("minimumLevel 不是有效的 Serilog 日志级别。", context.RequestAborted);
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var subscription = logHub.Subscribe(minimumLevel);
        using var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

        try
        {
            // WebSocket 允许一收一发并行：接收任务负责及时感知客户端关闭，发送任务负责日志推送。
            var sendTask = SendLogsAsync(socket, subscription, connectionCancellation.Token);
            var receiveTask = ReceiveUntilCloseAsync(socket, connectionCancellation.Token);
            await Task.WhenAny(sendTask, receiveTask).ConfigureAwait(false);
            await connectionCancellation.CancelAsync().ConfigureAwait(false);
            await AwaitConnectionTaskAsync(sendTask).ConfigureAwait(false);
            await AwaitConnectionTaskAsync(receiveTask).ConfigureAwait(false);
        }
        finally
        {
            logHub.Unsubscribe(subscription.Id);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseOutputAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "BetterGI 日志流已关闭。",
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (WebSocketException)
                {
                    // 对端已经断开，不需要让清理异常影响服务。
                }
            }
        }
    }

    /// <summary>
    /// 解析客户端要求的最低日志级别。
    /// </summary>
    /// <param name="value">查询参数值。</param>
    /// <param name="minimumLevel">解析后的最低日志级别。</param>
    /// <returns>参数是否合法。</returns>
    private static bool TryParseMinimumLevel(string? value, out LogEventLevel minimumLevel)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            minimumLevel = LogEventLevel.Information;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out minimumLevel)
               && Enum.IsDefined(minimumLevel);
    }

    /// <summary>
    /// 先发送历史快照，再持续发送实时日志和丢弃通知。
    /// </summary>
    /// <param name="socket">客户端 WebSocket。</param>
    /// <param name="subscription">日志订阅。</param>
    /// <param name="cancellationToken">连接取消令牌。</param>
    private static async Task SendLogsAsync(
        WebSocket socket,
        ExternalLogSubscription subscription,
        CancellationToken cancellationToken)
    {
        foreach (var logEvent in subscription.Snapshot)
        {
            await SendJsonAsync(socket, logEvent, cancellationToken).ConfigureAwait(false);
        }

        await foreach (var logEvent in subscription.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var droppedCount = subscription.TakeDroppedCount();
            if (droppedCount > 0)
            {
                await SendJsonAsync(
                    socket,
                    new ExternalLogDroppedDto { Count = droppedCount },
                    cancellationToken).ConfigureAwait(false);
            }

            await SendJsonAsync(socket, logEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 持续接收并忽略客户端数据，只处理关闭帧和断开状态。
    /// </summary>
    /// <param name="socket">客户端 WebSocket。</param>
    /// <param name="cancellationToken">连接取消令牌。</param>
    private static async Task ReceiveUntilCloseAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }
        }
    }

    /// <summary>
    /// 将对象序列化为一条完整的 UTF-8 WebSocket 文本消息。
    /// </summary>
    /// <param name="socket">客户端 WebSocket。</param>
    /// <param name="value">待发送对象。</param>
    /// <param name="cancellationToken">连接取消令牌。</param>
    private static Task SendJsonAsync<T>(WebSocket socket, T value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        return socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    /// <summary>
    /// 忽略连接结束时预期出现的取消和 WebSocket 断开异常。
    /// </summary>
    /// <param name="task">连接收发任务。</param>
    private static async Task AwaitConnectionTaskAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
        {
            // 客户端断开或应用停止均属于正常连接结束。
        }
    }
}
