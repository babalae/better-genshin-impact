using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Monitor;
using Microsoft.Win32.SafeHandles;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Instance;

internal sealed class InstanceConnection : IAsyncDisposable
{
    private readonly PipeStream _stream;
    private readonly IInstanceConnectionOwner _owner;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<InstanceIpcEnvelope>> _pendingRequests = new();
    private readonly Channel<RelativeMouseSample> _relativeMouseSamples =
        Channel.CreateBounded<RelativeMouseSample>(new BoundedChannelOptions(512)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });
    private readonly object _coalescedMouseLock = new();
    private readonly CancellationTokenSource _lifetimeCancellationTokenSource = new();

    private Task? _receiveTask;
    private Task? _mouseWriterTask;
    private CancellationTokenSource? _linkedCancellationTokenSource;
    private long _coalescedDeltaX;
    private long _coalescedDeltaY;
    private DateTime _coalescedTimestamp;
    private ulong _nextMouseSequence;
    private int _disposed;
    private int _receiveLoopExited;
    private string? _clientUserSid;

    internal InstanceConnection(PipeStream stream, IInstanceConnectionOwner owner, ILogger logger)
    {
        _stream = stream;
        _owner = owner;
        _logger = logger;
        if (stream is NamedPipeServerStream server)
        {
            if (InstancePipePeerInfo.TryGetClientProcessAndSession(
                    server.SafePipeHandle,
                    out var processId,
                    out var sessionId))
            {
                ClientProcessId = processId;
                ClientSessionId = sessionId;
            }

            _ = TryResolveClientUserSid();
        }
    }

    internal InstanceEndpoint? RemoteEndpoint { get; set; }

    internal int? ClientProcessId { get; }

    internal int? ClientSessionId { get; }

    /// <summary>
    /// 命名管道客户端的真实 Windows 用户 SID，取不到时为 null（此时必须拒绝授权）
    /// </summary>
    internal string? ClientUserSid => _clientUserSid;

    /// <summary>
    /// 取得并缓存客户端真实 Windows 用户 SID。
    /// <para>
    /// 客户端刚连接尚未写入时模拟可能失败，因此授权前（已收到首个请求帧）再调用一次。
    /// </para>
    /// </summary>
    internal string? TryResolveClientUserSid()
    {
        if (_clientUserSid is not null)
        {
            return _clientUserSid;
        }

        if (_stream is NamedPipeServerStream server
            && InstancePipePeerInfo.TryGetClientUserSid(server.SafePipeHandle, out var userSid))
        {
            _clientUserSid = userSid;
        }

        return _clientUserSid;
    }

    internal Task Completion => _receiveTask ?? Task.CompletedTask;

    internal bool IsStarted => _receiveTask is not null;

    internal void Start(CancellationToken cancellationToken)
    {
        _linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellationTokenSource.Token);
        _receiveTask = ReceiveLoopAsync(_linkedCancellationTokenSource);
        _mouseWriterTask = RelativeMouseWriterLoopAsync(_linkedCancellationTokenSource.Token);
    }

    internal async Task<InstanceIpcEnvelope> SendRequestAsync(
        string operation,
        object? data,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var request = InstanceIpcEnvelope.Request(operation, data);
        var completionSource = new TaskCompletionSource<InstanceIpcEnvelope>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(request.RequestId, completionSource))
        {
            throw new InvalidOperationException($"重复的命名管道请求 ID：{request.RequestId}。");
        }

        try
        {
            await WriteJsonAsync(request, cancellationToken).ConfigureAwait(false);
            return await completionSource.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pendingRequests.TryRemove(request.RequestId, out _);
        }
    }

    internal async Task WriteJsonAsync(
        InstanceIpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InstanceIpcProtocol.WriteJsonAsync(
                _stream,
                envelope,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    internal void EnqueueRelativeMouse(RelativeMouseMoveEventArgs eventArgs)
    {
        var sample = new RelativeMouseSample(
            eventArgs.DeltaX,
            eventArgs.DeltaY,
            eventArgs.Timestamp);
        if (_relativeMouseSamples.Writer.TryWrite(sample))
        {
            return;
        }

        lock (_coalescedMouseLock)
        {
            _coalescedDeltaX += eventArgs.DeltaX;
            _coalescedDeltaY += eventArgs.DeltaY;
            _coalescedTimestamp = eventArgs.Timestamp;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetimeCancellationTokenSource.Cancel();
        _relativeMouseSamples.Writer.TryComplete();
        _stream.Dispose();
        FailPendingRequests(new IOException("命名管道连接已关闭。"));

        var tasks = new[] { _receiveTask, _mouseWriterTask };
        foreach (var task in tasks)
        {
            if (task is null
                || ReferenceEquals(task, _receiveTask)
                && Volatile.Read(ref _receiveLoopExited) != 0)
            {
                continue;
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException
                                              or OperationCanceledException
                                              or ObjectDisposedException)
            {
                // 连接关闭期间出现这些异常属于正常清理流程。
            }
        }

        _writeLock.Dispose();
        _linkedCancellationTokenSource?.Dispose();
        _lifetimeCancellationTokenSource.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationTokenSource linkedCancellationTokenSource)
    {
        try
        {
            while (!linkedCancellationTokenSource.IsCancellationRequested)
            {
                var frame = await InstanceIpcProtocol.ReadFrameAsync(
                    _stream,
                    linkedCancellationTokenSource.Token).ConfigureAwait(false);
                if (frame is null)
                {
                    break;
                }

                if (frame.Value.PayloadType == InstanceIpcPayloadType.RelativeMouseBatch)
                {
                    var batch = InstanceIpcProtocol.ReadRelativeMouseBatch(frame.Value);
                    var handled = _owner.ReceiveRelativeMouseBatch(
                        this,
                        batch.FirstSequence,
                        batch.Samples);
                    var lastSequence = checked(
                        batch.FirstSequence + (ulong)batch.Samples.Length - 1);
                    await WriteRelativeMouseResultAsync(
                        new RelativeMouseResult(lastSequence, handled),
                        linkedCancellationTokenSource.Token).ConfigureAwait(false);
                    continue;
                }

                if (frame.Value.PayloadType == InstanceIpcPayloadType.RelativeMouseResult)
                {
                    _owner.ReceiveRelativeMouseResult(
                        this,
                        InstanceIpcProtocol.ReadRelativeMouseResult(frame.Value));
                    continue;
                }

                var envelope = InstanceIpcProtocol.ReadJson(frame.Value);
                if (envelope.Version != InstanceIpcProtocol.Version)
                {
                    throw new InvalidDataException($"不支持的实例 IPC 版本：{envelope.Version}。");
                }

                if (envelope.Operation == InstanceOperations.Response)
                {
                    if (_pendingRequests.TryGetValue(envelope.RequestId, out var completionSource))
                    {
                        completionSource.TrySetResult(envelope);
                    }
                    continue;
                }

                var response = await _owner.HandleRequestAsync(
                    this,
                    envelope,
                    linkedCancellationTokenSource.Token).ConfigureAwait(false);
                if (response is not null)
                {
                    await WriteJsonAsync(response, linkedCancellationTokenSource.Token).ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception) when (exception is IOException
                                          or EndOfStreamException
                                          or OperationCanceledException
                                          or ObjectDisposedException
                                          or InvalidDataException
                                          or JsonException)
        {
            if (!linkedCancellationTokenSource.IsCancellationRequested)
            {
                _logger.LogDebug(exception, "实例命名管道连接已断开");
            }
        }
        finally
        {
            linkedCancellationTokenSource.Cancel();
            FailPendingRequests(new IOException("命名管道接收循环已结束。"));
            Interlocked.Exchange(ref _receiveLoopExited, 1);
            _owner.ConnectionClosed(this);
        }
    }

    private async Task RelativeMouseWriterLoopAsync(CancellationToken cancellationToken)
    {
        var batch = new List<RelativeMouseSample>(64);
        try
        {
            while (await _relativeMouseSamples.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                batch.Clear();
                while (batch.Count < 64
                       && _relativeMouseSamples.Reader.TryRead(out var sample))
                {
                    batch.Add(sample);
                }

                AppendCoalescedSample(batch);
                if (batch.Count == 0)
                {
                    continue;
                }

                await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!_owner.IsGameMouseModeEnabled)
                    {
                        continue;
                    }

                    var firstSequence = _nextMouseSequence;
                    _nextMouseSequence += checked((ulong)batch.Count);
                    await InstanceIpcProtocol.WriteRelativeMouseBatchAsync(
                        _stream,
                        firstSequence,
                        batch,
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _writeLock.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭。
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            _logger.LogDebug(exception, "相对鼠标命名管道发送循环已结束");
        }
    }

    private async Task WriteRelativeMouseResultAsync(
        RelativeMouseResult result,
        CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InstanceIpcProtocol.WriteRelativeMouseResultAsync(
                _stream,
                result,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void AppendCoalescedSample(List<RelativeMouseSample> batch)
    {
        lock (_coalescedMouseLock)
        {
            if ((_coalescedDeltaX == 0 && _coalescedDeltaY == 0) || batch.Count >= 64)
            {
                return;
            }

            batch.Add(new RelativeMouseSample(
                ClampToInt32(_coalescedDeltaX),
                ClampToInt32(_coalescedDeltaY),
                _coalescedTimestamp));
            _coalescedDeltaX = 0;
            _coalescedDeltaY = 0;
            _coalescedTimestamp = default;
        }
    }

    private static int ClampToInt32(long value)
    {
        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    private void FailPendingRequests(Exception exception)
    {
        foreach (var completionSource in _pendingRequests.Values)
        {
            completionSource.TrySetException(exception);
        }
        _pendingRequests.Clear();
    }
}

internal static class InstancePipePeerInfo
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ProcessIdToSessionId(
        uint processId,
        out uint sessionId);

    internal static bool TryGetClientProcessAndSession(
        SafePipeHandle pipe,
        out int processId,
        out int sessionId)
    {
        if (GetNamedPipeClientProcessId(pipe, out var nativeProcessId)
            && ProcessIdToSessionId(nativeProcessId, out var nativeSessionId)
            && nativeProcessId <= int.MaxValue
            && nativeSessionId <= int.MaxValue)
        {
            processId = (int)nativeProcessId;
            sessionId = (int)nativeSessionId;
            return true;
        }

        processId = 0;
        sessionId = 0;
        return false;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(
        SafePipeHandle pipe,
        out uint serverProcessId);

    /// <summary>
    /// 取得管道服务端进程与会话。客户端用它核对"对端确实是自己期望的 Worker 进程"。
    /// </summary>
    internal static bool TryGetServerProcessAndSession(
        SafePipeHandle pipe,
        out int processId,
        out int sessionId)
    {
        if (GetNamedPipeServerProcessId(pipe, out var nativeProcessId)
            && ProcessIdToSessionId(nativeProcessId, out var nativeSessionId)
            && nativeProcessId <= int.MaxValue
            && nativeSessionId <= int.MaxValue)
        {
            processId = (int)nativeProcessId;
            sessionId = (int)nativeSessionId;
            return true;
        }

        processId = 0;
        sessionId = 0;
        return false;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImpersonateNamedPipeClient(SafePipeHandle pipe);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RevertToSelf();

    /// <summary>
    /// 通过模拟管道客户端取得真实 Windows 用户 SID。
    /// 必须在连接已建立、且未被其他线程复用的线程上调用；失败时返回 false（调用方必须按拒绝处理）。
    /// </summary>
    internal static bool TryGetClientUserSid(SafePipeHandle pipe, out string userSid)
    {
        userSid = string.Empty;
        if (!ImpersonateNamedPipeClient(pipe))
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User;
            if (user is null)
            {
                return false;
            }

            userSid = user.Value;
            return true;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            RevertToSelf();
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenUserInformation = 1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// 取得指定进程所属 Windows 用户 SID（不依赖客户端自报）。
    /// </summary>
    internal static bool TryGetProcessUserSid(int processId, out string userSid)
    {
        userSid = string.Empty;
        var processHandle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (processHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (!OpenProcessToken(processHandle, TokenQuery, out var tokenHandle))
            {
                return false;
            }

            try
            {
                GetTokenInformation(tokenHandle, TokenUserInformation, IntPtr.Zero, 0, out var length);
                if (length == 0)
                {
                    return false;
                }

                var buffer = Marshal.AllocHGlobal((int)length);
                try
                {
                    if (!GetTokenInformation(
                            tokenHandle,
                            TokenUserInformation,
                            buffer,
                            length,
                            out _))
                    {
                        return false;
                    }

                    // TOKEN_USER 的首字段为 SID_AND_ATTRIBUTES.Sid
                    var sidPointer = Marshal.ReadIntPtr(buffer);
                    if (sidPointer == IntPtr.Zero)
                    {
                        return false;
                    }

                    userSid = new SecurityIdentifier(sidPointer).Value;
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }
}
