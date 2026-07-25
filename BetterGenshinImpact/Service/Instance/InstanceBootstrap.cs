using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;

namespace BetterGenshinImpact.Service.Instance;

public sealed class InstanceBootstrap : IDisposable
{
    private NamedPipeServerStream? _firstServer;

    private InstanceBootstrap(InstanceContext context, NamedPipeServerStream firstServer)
    {
        Context = context;
        _firstServer = firstServer;
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
        var instanceId = options.RequestedInstanceId ?? Guid.NewGuid();
        var sessionPipeName = InstancePipeNames.ForSession(Process.GetCurrentProcess().SessionId);
        NamedPipeServerStream firstServer;
        string pipeName;

        if (options.InstanceType == BetterGiInstanceType.Primary
            && !Environment.GetCommandLineArgs().Contains("--no-single", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                firstServer = InstancePipeFactory.CreateServer(sessionPipeName, firstPipeInstance: true);
                pipeName = sessionPipeName;
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException)
            {
                var forwarded = ForwardActivationAsync(
                        sessionPipeName,
                        instanceId,
                        Environment.GetCommandLineArgs(),
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                if (!forwarded)
                {
                    Trace.TraceError(
                        "无法将启动请求转发给现有 BetterGI，命名管道：{0}",
                        sessionPipeName);
                }
                Environment.Exit(forwarded ? 0 : 0xFFFF);
                throw new InvalidOperationException("当前实例已将启动请求转发给现有 BetterGI。");
            }
        }
        else if (options.InstanceType == BetterGiInstanceType.ChildSession)
        {
            try
            {
                firstServer = InstancePipeFactory.CreateServer(sessionPipeName, firstPipeInstance: true);
                pipeName = sessionPipeName;
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException)
            {
                pipeName = InstancePipeNames.ForInstance(instanceId);
                firstServer = InstancePipeFactory.CreateServer(pipeName, firstPipeInstance: true);
            }
        }
        else
        {
            pipeName = InstancePipeNames.ForInstance(instanceId);
            firstServer = InstancePipeFactory.CreateServer(pipeName, firstPipeInstance: true);
        }

        Current = new InstanceBootstrap(
            new InstanceContext(
                instanceId,
                options.InstanceType,
                pipeName,
                options.ParentInstanceId,
                options.ParentPipeName),
            firstServer);
    }

    internal NamedPipeServerStream TakeFirstServer()
    {
        return Interlocked.Exchange(ref _firstServer, null)
               ?? throw new InvalidOperationException("首个命名管道服务端实例已被接管。");
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _firstServer, null)?.Dispose();
    }

    private static async Task<bool> ForwardActivationAsync(
        string pipeName,
        Guid sourceInstanceId,
        string[] args,
        CancellationToken cancellationToken)
    {
        var request = InstanceIpcEnvelope.Request(
            InstanceOperations.ActivationForward,
            sourceInstanceId,
            new ActivationForwardRequest { Arguments = args });
        var retryDelays = new[]
        {
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(500)
        };

        foreach (var retryDelay in retryDelays)
        {
            if (retryDelay > TimeSpan.Zero)
            {
                await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await using var client = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.WriteThrough);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
                await InstanceIpcProtocol.WriteJsonAsync(client, request, timeout.Token).ConfigureAwait(false);
                var frame = await InstanceIpcProtocol.ReadFrameAsync(client, timeout.Token).ConfigureAwait(false);
                if (frame is null)
                {
                    continue;
                }

                var response = InstanceIpcProtocol.ReadJson(frame.Value);
                return response.Operation == InstanceOperations.Response
                       && response.RequestId == request.RequestId
                       && response.Success == true;
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or TimeoutException
                                              or OperationCanceledException)
            {
                Debug.WriteLine(exception);
                Trace.TraceWarning(
                    "转发 BetterGI 启动请求失败，命名管道：{0}，原因：{1}",
                    pipeName,
                    exception.GetBaseException().Message);
            }
        }

        return false;
    }
}

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

internal sealed class ActivationForwardRequest
{
    public string[] Arguments { get; init; } = [];
}
