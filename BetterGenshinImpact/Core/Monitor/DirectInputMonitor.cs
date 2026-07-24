using Microsoft.Extensions.Logging;
using SharpDX.DirectInput;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Monitor;

public sealed class DirectInputMonitor(ILogger<DirectInputMonitor> logger) : RelativeMouseInputMonitorBase(logger)
{
    private const int Interval = 5;
    private readonly object _captureLock = new();
    private CaptureContext? _captureContext;

    protected override void StartCore()
    {
        var directInput = new DirectInput();
        Mouse? mouse = null;
        try
        {
            mouse = new Mouse(directInput);
            mouse.SetCooperativeLevel(
                IntPtr.Zero,
                CooperativeLevel.Background | CooperativeLevel.NonExclusive);
            mouse.Acquire();

            var context = new CaptureContext(directInput, mouse);
            lock (_captureLock)
            {
                _captureContext = context;
            }

            context.CaptureTask = Task.Run(() => Capture(context));
        }
        catch
        {
            mouse?.Dispose();
            directInput.Dispose();
            throw;
        }
    }

    protected override void StopCore()
    {
        CaptureContext? context;
        lock (_captureLock)
        {
            context = _captureContext;
            _captureContext = null;
        }

        if (context == null)
        {
            return;
        }

        context.CancellationTokenSource.Cancel();

        var captureTask = context.CaptureTask;
        if (captureTask != null &&
            Environment.CurrentManagedThreadId != context.CaptureThreadId)
        {
            try
            {
                captureTask.Wait();
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(x => x is OperationCanceledException))
            {
                // 正常取消
            }
        }
    }

    private void Capture(CaptureContext context)
    {
        context.CaptureThreadId = Environment.CurrentManagedThreadId;
        try
        {
            while (!context.CancellationTokenSource.IsCancellationRequested)
            {
                context.Mouse.Acquire();
                MouseState state = context.Mouse.GetCurrentState();
                // Debug.WriteLine($"{state.X} {state.Y} {state.Buttons[0]} {state.Buttons[1]}");
                if (state is not { X: 0, Y: 0 })
                {
                    var timestamp = unchecked(Kernel32.GetTickCount() - (uint)Interval);
                    Publish(new RelativeMouseMoveEventArgs(state.X, state.Y, timestamp));
                }

                if (context.CancellationTokenSource.Token.WaitHandle.WaitOne(Interval))
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            if (!context.CancellationTokenSource.IsCancellationRequested)
            {
                Logger.LogError(ex, "DirectInput 相对鼠标捕获异常终止");
            }
        }
        finally
        {
            context.Mouse.Dispose();
            context.DirectInput.Dispose();
            context.CancellationTokenSource.Dispose();
        }
    }

    private sealed class CaptureContext(DirectInput directInput, Mouse mouse)
    {
        public DirectInput DirectInput { get; } = directInput;

        public Mouse Mouse { get; } = mouse;

        public CancellationTokenSource CancellationTokenSource { get; } = new();

        public Task? CaptureTask { get; set; }

        public int CaptureThreadId { get; set; }
    }
}
