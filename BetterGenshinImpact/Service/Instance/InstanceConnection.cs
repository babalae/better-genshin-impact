using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Monitor;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Instance;

internal sealed class InstanceConnection : IAsyncDisposable
{
    private readonly PipeStream _stream;
    private readonly InstanceService _owner;
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
    private long _coalescedDeltaX;
    private long _coalescedDeltaY;
    private DateTime _coalescedTimestamp;
    private ulong _nextMouseSequence;
    private int _disposed;
    private int _receiveLoopExited;

    internal InstanceConnection(PipeStream stream, InstanceService owner, ILogger logger)
    {
        _stream = stream;
        _owner = owner;
        _logger = logger;
    }

    internal InstanceDescriptor? RemoteDescriptor { get; set; }

    internal Task Completion => _receiveTask ?? Task.CompletedTask;

    internal void Start(CancellationToken cancellationToken)
    {
        var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellationTokenSource.Token);
        _receiveTask = ReceiveLoopAsync(linkedCancellationTokenSource);
        _mouseWriterTask = RelativeMouseWriterLoopAsync(linkedCancellationTokenSource.Token);
    }

    internal async Task<InstanceIpcEnvelope> SendRequestAsync(
        string operation,
        Guid sourceInstanceId,
        object? data,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var request = InstanceIpcEnvelope.Request(operation, sourceInstanceId, data);
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
            linkedCancellationTokenSource.Dispose();
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
