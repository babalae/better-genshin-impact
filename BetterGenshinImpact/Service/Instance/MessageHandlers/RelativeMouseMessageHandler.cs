using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.Core.Monitor;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.ChildSession;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Service.Instance.MessageHandlers;

/// <summary>
/// 处理桌面分身相对鼠标消息，并管理主会话中的 Raw Input、鼠标隐藏和区域限制。
/// 消息确认状态与本地捕获放在同一类中，确保只有 ChildSession 确认移动成功后才会启用
/// <see cref="LocalCursorCapture"/>。
/// </summary>
internal sealed class RelativeMouseMessageHandler
{
    private static readonly TimeSpan RelativeMouseFlushInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan RelativeMouseStateInterval = TimeSpan.FromMilliseconds(5);
    private const ushort VirtualKeyMenu = 0x12;
    private const ushort VirtualKeyLeftMenu = 0xA4;
    private const ushort VirtualKeyRightMenu = 0xA5;
    private const ushort RawKeyboardExtendedKeyFlag = 0x02;
    private const int LeftAltMask = 0x01;
    private const int RightAltMask = 0x02;

    private readonly InstanceContext _context;
    private readonly IServiceProvider _serviceProvider;
    private readonly RawInputMonitor _rawInputMonitor;
    private readonly LocalCursorCapture _localCursorCapture;
    private readonly Func<InstanceConnection, bool> _isParentConnection;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Guid, InstanceConnection> _targets = new();
    private readonly object _subscriptionLock = new();
    private readonly object _accumulatorLock = new();

    private IDisposable? _rawMouseInputSubscription;
    private IDisposable? _rawKeyboardInputSubscription;
    private DispatcherTimer? _stateTimer;
    private long _accumulatedX;
    private long _accumulatedY;
    private long _accumulationStartedAt;
    private DateTime _lastTimestamp;
    private int _pressedAltMask;
    private int _handlingConfirmed;
    private int _forwardingEnabled;

    internal RelativeMouseMessageHandler(
        InstanceContext context,
        IServiceProvider serviceProvider,
        RawInputMonitor rawInputMonitor,
        Func<InstanceConnection, bool> isParentConnection,
        ILogger logger)
    {
        _context = context;
        _serviceProvider = serviceProvider;
        _rawInputMonitor = rawInputMonitor;
        _isParentConnection = isParentConnection;
        _logger = logger;
        _localCursorCapture = new LocalCursorCapture(logger);
    }

    /// <summary>
    /// ChildSession 只有在原神处于前台且整批 SendInput 均成功时才确认已处理。
    /// 返回值会由连接层写回 Primary，作为隐藏和限制本地鼠标的必要条件。
    /// </summary>
    internal bool HandleBatch(
        InstanceConnection connection,
        ulong firstSequence,
        IReadOnlyList<RelativeMouseSample> samples)
    {
        _logger.LogTrace(
            "收到相对鼠标批次：序号 {FirstSequence}，样本数 {SampleCount}",
            firstSequence,
            samples.Count);
        if (_context.InstanceType != BetterGiInstanceType.ChildSession
            || !_isParentConnection(connection)
            || !SystemControl.IsGenshinImpactActiveByProcess())
        {
            return false;
        }

        try
        {
            foreach (var sample in samples)
            {
                Simulation.SendInput.Mouse.MoveMouseBy(sample.DeltaX, sample.DeltaY);
            }
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "向前台原神发送管道相对鼠标移动失败：序号 {FirstSequence}",
                firstSequence);
            return false;
        }
    }

    /// <summary>
    /// Primary 接收 ChildSession 的处理结果。无效连接和 Alt 按下期间的成功响应都会被忽略。
    /// </summary>
    internal void HandleResult(
        InstanceConnection connection,
        RelativeMouseResult result)
    {
        if (_context.InstanceType != BetterGiInstanceType.Primary
            || connection.RemoteDescriptor is not
            {
                InstanceType: BetterGiInstanceType.ChildSession
            } descriptor
            || !_targets.TryGetValue(descriptor.InstanceId, out var target)
            || !ReferenceEquals(target, connection))
        {
            return;
        }

        var handled = result.Handled && Volatile.Read(ref _pressedAltMask) == 0;
        _logger.LogTrace(
            "收到相对鼠标处理结果：末序号 {LastSequence}，已处理 {Handled}",
            result.LastSequence,
            handled);
        SetHandlingConfirmed(handled);
    }

    /// <summary>
    /// 注册需要接收相对鼠标数据的 ChildSession，并启动本地 Raw Input 监听。
    /// 此时不会立即隐藏鼠标，必须先收到一次成功处理回执。
    /// </summary>
    internal InstanceIpcEnvelope HandleSubscribe(
        InstanceConnection connection,
        InstanceIpcEnvelope request)
    {
        if (_context.InstanceType != BetterGiInstanceType.Primary)
        {
            throw new InvalidOperationException("只有 Primary 实例可以转发桌面分身相对鼠标数据。");
        }

        var descriptor = connection.RemoteDescriptor
                         ?? throw new InvalidOperationException("相对鼠标订阅方尚未注册为子实例。");
        if (descriptor.InstanceType != BetterGiInstanceType.ChildSession)
        {
            throw new InvalidOperationException("只有 ChildSession 子实例可以订阅相对鼠标数据。");
        }

        _targets[descriptor.InstanceId] = connection;
        StartForwarding();
        return InstanceIpcEnvelope.Response(
            request,
            _context.InstanceId,
            new RelativeMouseState { IsSubscribed = true });
    }

    internal InstanceIpcEnvelope HandleUnsubscribe(
        InstanceConnection connection,
        InstanceIpcEnvelope request)
    {
        if (connection.RemoteDescriptor is { } descriptor)
        {
            RemoveTarget(descriptor.InstanceId);
        }
        return InstanceIpcEnvelope.Response(
            request,
            _context.InstanceId,
            new RelativeMouseState { IsSubscribed = false });
    }

    /// <summary>
    /// 在子实例注销或连接断开时移除目标；最后一个目标移除后恢复本地鼠标状态。
    /// </summary>
    internal void RemoveTarget(Guid instanceId)
    {
        _targets.TryRemove(instanceId, out _);
        StopIfUnused();
    }

    /// <summary>
    /// 停止整个相对鼠标管线。可从非 UI 线程调用，实际清理会切换到 UI 线程。
    /// </summary>
    internal void Stop()
    {
        Interlocked.Exchange(ref _forwardingEnabled, 0);
        Interlocked.Exchange(ref _handlingConfirmed, 0);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            StopRawInputCapture();
            _localCursorCapture.Release();
            ClearAccumulator();
            return;
        }

        void CleanupOnUiThread()
        {
            _stateTimer?.Stop();
            StopRawInputCapture();
            _localCursorCapture.Release();
            ClearAccumulator();
        }

        if (dispatcher.CheckAccess())
        {
            CleanupOnUiThread();
        }
        else
        {
            _ = dispatcher.BeginInvoke(
                DispatcherPriority.Send,
                new Action(CleanupOnUiThread));
        }
    }

    private void StartForwarding()
    {
        Interlocked.Exchange(ref _forwardingEnabled, 1);
        Interlocked.Exchange(ref _handlingConfirmed, 0);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(new Action(() =>
        {
            if (Volatile.Read(ref _forwardingEnabled) == 0 || _targets.IsEmpty)
            {
                return;
            }

            if (_stateTimer is null)
            {
                _stateTimer = new DispatcherTimer(
                    DispatcherPriority.Input,
                    dispatcher)
                {
                    Interval = RelativeMouseStateInterval
                };
                _stateTimer.Tick += OnStateTimerTick;
            }

            _stateTimer.Start();
            RefreshCaptureState();
        }));
    }

    private void StopIfUnused()
    {
        if (!_targets.IsEmpty)
        {
            return;
        }
        Stop();
    }

    private void SetHandlingConfirmed(bool handled)
    {
        var state = handled ? 1 : 0;
        if (Interlocked.Exchange(ref _handlingConfirmed, state) == state)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null)
        {
            _ = dispatcher.BeginInvoke(
                DispatcherPriority.Send,
                new Action(RefreshCaptureState));
        }
    }

    private void StopRawInputCapture()
    {
        lock (_subscriptionLock)
        {
            _rawMouseInputSubscription?.Dispose();
            _rawMouseInputSubscription = null;
            _rawKeyboardInputSubscription?.Dispose();
            _rawKeyboardInputSubscription = null;
        }

        Interlocked.Exchange(ref _pressedAltMask, 0);
        Interlocked.Exchange(ref _handlingConfirmed, 0);
    }

    private void StopRawMouseInputCapture()
    {
        lock (_subscriptionLock)
        {
            _rawMouseInputSubscription?.Dispose();
            _rawMouseInputSubscription = null;
        }
    }

    private void OnRelativeMouseMoved(object? sender, RelativeMouseMoveEventArgs eventArgs)
    {
        RelativeMouseSample? sampleToSend = null;
        lock (_accumulatorLock)
        {
            var now = Stopwatch.GetTimestamp();
            if (HasDirectionReversed(
                    _accumulatedX,
                    _accumulatedY,
                    eventArgs.DeltaX,
                    eventArgs.DeltaY))
            {
                sampleToSend = TakeAccumulatedSample();
            }

            if (_accumulationStartedAt == 0)
            {
                _accumulationStartedAt = now;
            }

            _accumulatedX += eventArgs.DeltaX;
            _accumulatedY += eventArgs.DeltaY;
            _lastTimestamp = eventArgs.Timestamp;

            if (sampleToSend is null
                && Stopwatch.GetElapsedTime(
                    _accumulationStartedAt,
                    now) >= RelativeMouseFlushInterval)
            {
                sampleToSend = TakeAccumulatedSample();
            }
        }

        if (sampleToSend is not null)
        {
            QueueSample(sampleToSend.Value);
        }
    }

    private void OnStateTimerTick(object? sender, EventArgs eventArgs)
    {
        RefreshCaptureState();
    }

    /// <summary>
    /// 根据 RDP 焦点、Alt 状态和 ChildSession 回执更新捕获状态。
    /// Raw Input 可以先启动以发送首个样本，但隐藏与 ClipCursor 必须等待成功回执。
    /// </summary>
    private void RefreshCaptureState()
    {
        if (Volatile.Read(ref _forwardingEnabled) == 0 || _targets.IsEmpty)
        {
            _stateTimer?.Stop();
            StopRawInputCapture();
            _localCursorCapture.Release();
            ClearAccumulator();
            return;
        }

        try
        {
            lock (_subscriptionLock)
            {
                _rawKeyboardInputSubscription ??=
                    _rawInputMonitor.SubscribeKeyboard(OnRawKeyboardInput);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "启动桌面分身 Raw Input 键盘捕获失败");
            Interlocked.Exchange(ref _handlingConfirmed, 0);
            StopRawMouseInputCapture();
            _localCursorCapture.Release();
            ClearAccumulator();
            return;
        }

        var childSessionService = _serviceProvider.GetService<ChildSessionService>();
        if (childSessionService?.TryGetRelativeMouseCaptureBounds(out var captureBounds) != true)
        {
            Interlocked.Exchange(ref _handlingConfirmed, 0);
            StopRawMouseInputCapture();
            _localCursorCapture.Release();
            ClearAccumulator();
            return;
        }

        if (Volatile.Read(ref _pressedAltMask) != 0)
        {
            Interlocked.Exchange(ref _handlingConfirmed, 0);
            StopRawMouseInputCapture();
            ClearAccumulator();
            try
            {
                _localCursorCapture.ReleaseTemporarily();
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "按住 Alt 时解除本地鼠标限制失败");
            }
            return;
        }

        try
        {
            lock (_subscriptionLock)
            {
                _rawMouseInputSubscription ??= _rawInputMonitor.Subscribe(OnRelativeMouseMoved);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "启动桌面分身 Raw Input 相对鼠标捕获失败");
            Interlocked.Exchange(ref _handlingConfirmed, 0);
            StopRawMouseInputCapture();
            _localCursorCapture.Release();
            ClearAccumulator();
            return;
        }

        if (Volatile.Read(ref _handlingConfirmed) != 0)
        {
            try
            {
                _localCursorCapture.Capture(captureBounds);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "隐藏并限制桌面分身本地鼠标失败");
                _localCursorCapture.Release();
            }
        }
        else
        {
            _localCursorCapture.Release();
        }

        RelativeMouseSample? sampleToSend = null;
        lock (_accumulatorLock)
        {
            if (_accumulationStartedAt != 0
                && Stopwatch.GetElapsedTime(
                    _accumulationStartedAt,
                    Stopwatch.GetTimestamp()) >= RelativeMouseFlushInterval)
            {
                sampleToSend = TakeAccumulatedSample();
            }
        }

        if (sampleToSend is not null)
        {
            SendSampleIfFocused(sampleToSend.Value);
        }
    }

    private void QueueSample(RelativeMouseSample sample)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => SendSampleIfFocused(sample)));
    }

    private void SendSampleIfFocused(RelativeMouseSample sample)
    {
        if (Volatile.Read(ref _forwardingEnabled) == 0
            || _targets.IsEmpty
            || Volatile.Read(ref _pressedAltMask) != 0
            || _serviceProvider.GetService<ChildSessionService>()
                ?.IsRelativeMouseForwardingAvailable() != true)
        {
            return;
        }

        var eventArgs = new RelativeMouseMoveEventArgs(
            sample.DeltaX,
            sample.DeltaY,
            sample.Timestamp);
        foreach (var connection in _targets.Values)
        {
            connection.EnqueueRelativeMouse(eventArgs);
        }
    }

    private void OnRawKeyboardInput(object? sender, RawKeyboardInputEventArgs eventArgs)
    {
        var altMask = GetAltMask(eventArgs);
        if (altMask == 0)
        {
            return;
        }

        int currentMask;
        int nextMask;
        do
        {
            currentMask = Volatile.Read(ref _pressedAltMask);
            nextMask = eventArgs.IsKeyDown
                ? currentMask | altMask
                : currentMask & ~altMask;
            if (nextMask == currentMask)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(
                   ref _pressedAltMask,
                   nextMask,
                   currentMask) != currentMask);

        if (eventArgs.IsKeyDown)
        {
            Interlocked.Exchange(ref _handlingConfirmed, 0);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null)
        {
            _ = dispatcher.BeginInvoke(
                DispatcherPriority.Send,
                new Action(RefreshCaptureState));
        }
    }

    private static int GetAltMask(RawKeyboardInputEventArgs eventArgs)
    {
        return eventArgs.VirtualKey switch
        {
            VirtualKeyLeftMenu => LeftAltMask,
            VirtualKeyRightMenu => RightAltMask,
            VirtualKeyMenu when
                (eventArgs.Flags & RawKeyboardExtendedKeyFlag) != 0 => RightAltMask,
            VirtualKeyMenu => LeftAltMask,
            _ => 0
        };
    }

    private RelativeMouseSample? TakeAccumulatedSample()
    {
        if (_accumulatedX == 0 && _accumulatedY == 0)
        {
            ResetAccumulator();
            return null;
        }

        var sample = new RelativeMouseSample(
            ClampToInt32(_accumulatedX),
            ClampToInt32(_accumulatedY),
            _lastTimestamp);
        ResetAccumulator();
        return sample;
    }

    private void ClearAccumulator()
    {
        lock (_accumulatorLock)
        {
            ResetAccumulator();
        }
    }

    private void ResetAccumulator()
    {
        _accumulatedX = 0;
        _accumulatedY = 0;
        _accumulationStartedAt = 0;
        _lastTimestamp = default;
    }

    private static bool HasDirectionReversed(
        long accumulatedX,
        long accumulatedY,
        int deltaX,
        int deltaY)
    {
        if ((accumulatedX == 0 && accumulatedY == 0)
            || (deltaX == 0 && deltaY == 0))
        {
            return false;
        }

        return (double)accumulatedX * deltaX
               + (double)accumulatedY * deltaY < 0;
    }

    private static int ClampToInt32(long value)
    {
        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }
}
