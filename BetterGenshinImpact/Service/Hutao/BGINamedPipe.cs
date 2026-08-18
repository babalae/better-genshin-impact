using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Hutao.Handlers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Hutao;

/// <summary>
/// BetterGI 侧的命名管道<b>服务端</b>,监听 <c>BetterGenshinImpact.NamedPipe</c>,处理胡桃(S→B)发来的请求。
/// 方向与 <see cref="HutaoNamedPipe"/> 相反:后者是 BetterGI 作为客户端连向胡桃。
/// </summary>
internal sealed class BGINamedPipe : IHostedService, IDisposable
{
    private const string PipeName = "BetterGenshinImpact.NamedPipe";

    private readonly IPipeRequestHandler[] requestHandlers;
    private readonly ILogger<BGINamedPipe> logger;
    private readonly CancellationTokenSource serverTokenSource = new();
    private readonly List<NamedPipeServerStream> activeStreams = [];

    public BGINamedPipe(IEnumerable<IPipeRequestHandler> requestHandlers, ILogger<BGINamedPipe> logger)
    {
        this.requestHandlers = requestHandlers.ToArray();
        this.logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = AcceptLoopAsync(serverTokenSource.Token);
        logger.LogInformation("Hutao 命名管道服务端已启动：{PipeName}", PipeName);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        serverTokenSource.Cancel();
        DisposeActiveStreams();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        serverTokenSource.Cancel();
        serverTokenSource.Dispose();
        DisposeActiveStreams();
    }

    private void DisposeActiveStreams()
    {
        lock (activeStreams)
        {
            foreach (NamedPipeServerStream stream in activeStreams)
            {
                stream.Dispose();
            }

            activeStreams.Clear();
        }
    }

    private static NamedPipeServerStream CreatePipeServerStream()
    {
        PipeSecurity? pipeSecurity = default;

        if (RuntimeHelper.IsElevated)
        {
            SecurityIdentifier everyOne = new(WellKnownSidType.WorldSid, null);
            pipeSecurity = new();
            pipeSecurity.AddAccessRule(new PipeAccessRule(everyOne, PipeAccessRights.FullControl, AccessControlType.Allow));
        }

        return NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            0,
            0,
            pipeSecurity);
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream stream = CreatePipeServerStream();
                try
                {
                    await stream.WaitForConnectionAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    stream.Dispose();
                    break;
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    stream.Dispose();
                    if (!token.IsCancellationRequested)
                    {
                        logger.LogError(exception, "Hutao 命名管道服务端等待连接异常");
                    }

                    continue;
                }

                logger.LogInformation("Hutao pipe session created");
                lock (activeStreams)
                {
                    activeStreams.Add(stream);
                }

                // 会话处理放到独立任务,避免单个慢请求(如阻塞在 UI 调度)拖住 accept 循环。
                _ = Task.Run(() =>
                {
                    try
                    {
                        RunPacketSession(stream, token);
                    }
                    finally
                    {
                        lock (activeStreams)
                        {
                            activeStreams.Remove(stream);
                        }

                        stream.Dispose();
                    }
                });
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            if (!token.IsCancellationRequested)
            {
                logger.LogError(exception, "Hutao 命名管道服务端监听异常终止");
            }
        }
    }

    private void RunPacketSession(NamedPipeServerStream stream, CancellationToken token)
    {
        try
        {
            while (stream.IsConnected && !token.IsCancellationRequested)
            {
                stream.ReadPacket(out PipePacketHeader header);
                switch ((header.Type, header.Command))
                {
                    case (PipePacketType.Request, PipePacketCommand.SnapHutaoToBetterGenshinImpactRequest):
                        if (stream.ReadJsonContent<PipeRequest<JsonElement>>(in header) is { } request)
                        {
                            DispatchHutaoRequest(stream, request);
                        }

                        break;

                    case (PipePacketType.SessionTermination, _):
                        stream.Disconnect();
                        return;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException or JsonException)
        {
            // 会话异常结束（正常断开或协议错误），交由 AcceptLoop 重新等待连接。
        }
        catch (Exception exception)
        {
            // 处理请求时抛出的未预期异常（如 Start 失败）不得击穿监听循环，记录后结束会话即可。
            logger.LogError(exception, "处理 Hutao 管道请求时发生未预期异常");
        }
    }

    private void DispatchHutaoRequest(NamedPipeServerStream stream, PipeRequest<JsonElement> request)
    {
        foreach (IPipeRequestHandler handler in requestHandlers)
        {
            if (handler.CanHandle(request.Kind))
            {
                handler.HandleRequest(stream, request);
                return;
            }
        }

        // 未匹配任何 handler 也要回一个空响应,否则对端(胡桃)会一直阻塞等待。
        stream.WriteResponse(new PipeResponse<object?> { Kind = PipeResponseKind.None, Data = null });
    }
}
