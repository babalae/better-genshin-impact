using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Instance.MessageHandlers;

namespace BetterGenshinImpact.Service.Instance;

public sealed class InstanceBootstrap : IDisposable
{
    private NamedPipeServerStream? _firstServer;
    private InitialRootConnection? _firstRootConnection;

    private InstanceBootstrap(
        InstanceContext context,
        NamedPipeServerStream? firstServer,
        InitialRootConnection? firstRootConnection)
    {
        Context = context;
        _firstServer = firstServer;
        _firstRootConnection = firstRootConnection;
    }

    public static InstanceBootstrap Current { get; private set; } = null!;

    public InstanceContext Context { get; }

    internal static void Initialize()
    {
        if (Current is not null)
        {
            return;
        }

        var options = CommandLineOptions.Instance;
        var rootPipeName = InstancePipeNames.ForCurrentUser();
        var currentSessionId = Process.GetCurrentProcess().SessionId;

        if (options.HasExplicitInstanceType)
        {
            var initialConnection = TryOpenRootConnectionAsync(
                    rootPipeName,
                    options.InstanceType,
                    options.RestartFromProcessId,
                    Environment.GetCommandLineArgs(),
                    [TimeSpan.Zero],
                    TimeSpan.FromMilliseconds(200),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (initialConnection?.Response.Disposition
                == ConnectionOpenDisposition.ActivationForwarded)
            {
                initialConnection.Client.Dispose();
                Environment.Exit(0);
                throw new InvalidOperationException("当前实例已将启动请求转发给现有 BetterGI。");
            }

            Current = new InstanceBootstrap(
                new InstanceContext(
                    initialConnection?.Response.AssignedType ?? options.InstanceType,
                    rootPipeName,
                    initialConnection?.Response.RootSessionId),
                firstServer: null,
                initialConnection);
            return;
        }

        WaitForRestartSource(options.RestartFromProcessId);
        try
        {
            var firstServer = InstancePipeFactory.CreateServer(
                rootPipeName,
                firstPipeInstance: true);
            Current = new InstanceBootstrap(
                new InstanceContext(
                    BetterGiInstanceType.Primary,
                    rootPipeName,
                    currentSessionId),
                firstServer,
                firstRootConnection: null);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException)
        {
            var initialConnection = TryOpenRootConnectionAsync(
                    rootPipeName,
                    BetterGiInstanceType.Primary,
                    options.RestartFromProcessId,
                    Environment.GetCommandLineArgs(),
                    [
                        TimeSpan.Zero,
                        TimeSpan.FromMilliseconds(200),
                        TimeSpan.FromMilliseconds(500)
                    ],
                    TimeSpan.FromSeconds(2),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            if (initialConnection is null)
            {
                Trace.TraceError(
                    "无法连接当前用户的 BetterGI 根管道：{0}",
                    rootPipeName);
                Environment.Exit(0xFFFF);
                throw new InvalidOperationException("无法连接当前用户的 BetterGI 根实例。");
            }

            if (initialConnection.Response.Disposition
                == ConnectionOpenDisposition.ActivationForwarded)
            {
                initialConnection.Client.Dispose();
                Environment.Exit(0);
                throw new InvalidOperationException("当前实例已将启动请求转发给现有 BetterGI。");
            }

            Current = new InstanceBootstrap(
                new InstanceContext(
                    initialConnection.Response.AssignedType,
                    rootPipeName,
                    initialConnection.Response.RootSessionId),
                firstServer: null,
                initialConnection);
        }
    }

    internal NamedPipeServerStream? TakeFirstServer()
    {
        return Interlocked.Exchange(ref _firstServer, null);
    }

    internal InitialRootConnection? TakeFirstRootConnection()
    {
        return Interlocked.Exchange(ref _firstRootConnection, null);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _firstServer, null)?.Dispose();
        Interlocked.Exchange(ref _firstRootConnection, null)?.Client.Dispose();
    }

    private static void WaitForRestartSource(int? restartFromProcessId)
    {
        if (restartFromProcessId is null || restartFromProcessId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(restartFromProcessId.Value);
            if (!process.WaitForExit(milliseconds: 15_000))
            {
                throw new TimeoutException(
                    $"等待旧 BetterGI 进程 {restartFromProcessId.Value} 退出超时。");
            }
        }
        catch (ArgumentException)
        {
            // 旧进程已经退出。
        }
    }

    private static async Task<InitialRootConnection?> TryOpenRootConnectionAsync(
        string pipeName,
        BetterGiInstanceType requestedType,
        int? restartFromProcessId,
        string[] args,
        IReadOnlyList<TimeSpan> retryDelays,
        TimeSpan connectTimeout,
        CancellationToken cancellationToken)
    {
        var request = InstanceIpcEnvelope.Request(
            InstanceOperations.ConnectionOpen,
            new ConnectionOpenRequest
            {
                RequestedType = requestedType,
                RestartFromProcessId = restartFromProcessId,
                Arguments = args
            });

        foreach (var retryDelay in retryDelays)
        {
            if (retryDelay > TimeSpan.Zero)
            {
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var client = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.WriteThrough);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(connectTimeout);
                try
                {
                    await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
                    await InstanceIpcProtocol.WriteJsonAsync(
                        client,
                        request,
                        timeout.Token).ConfigureAwait(false);
                    var frame = await InstanceIpcProtocol.ReadFrameAsync(
                        client,
                        timeout.Token).ConfigureAwait(false);
                    if (frame is null)
                    {
                        client.Dispose();
                        continue;
                    }

                    var response = InstanceIpcProtocol.ReadJson(frame.Value);
                    if (response.Operation != InstanceOperations.Response
                        || response.RequestId != request.RequestId)
                    {
                        client.Dispose();
                        continue;
                    }
                    if (response.Success != true)
                    {
                        client.Dispose();
                        throw new InvalidOperationException(
                            response.ErrorMessage ?? response.ErrorCode ?? "根实例拒绝连接。");
                    }

                    var openResponse =
                        response.Data?.ToObject<ConnectionOpenResponse>(
                            InstanceIpcProtocol.Serializer)
                        ?? throw new InvalidDataException("根实例连接响应缺少数据。");
                    return new InitialRootConnection(client, openResponse);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or TimeoutException
                                              or OperationCanceledException)
            {
                Debug.WriteLine(exception);
                Trace.TraceWarning(
                    "连接 BetterGI 根管道失败，命名管道：{0}，原因：{1}",
                    pipeName,
                    exception.GetBaseException().Message);
            }
        }

        return null;
    }
}

internal sealed record InitialRootConnection(
    NamedPipeClientStream Client,
    ConnectionOpenResponse Response);

internal static class InstancePipeFactory
{
    internal static NamedPipeServerStream CreateServer(string pipeName, bool firstPipeInstance)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var ownerSid = identity.User
                       ?? throw new InvalidOperationException("无法取得当前 Windows 用户 SID。");
        var networkSid = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(ownerSid);
        security.AddAccessRule(new PipeAccessRule(
            networkSid,
            PipeAccessRights.FullControl,
            AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(
            ownerSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        var options = PipeOptions.Asynchronous | PipeOptions.WriteThrough;
        if (firstPipeInstance)
        {
            options |= PipeOptions.FirstPipeInstance;
        }

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            options,
            inBufferSize: 16 * 1024,
            outBufferSize: 16 * 1024,
            security);
    }
}
