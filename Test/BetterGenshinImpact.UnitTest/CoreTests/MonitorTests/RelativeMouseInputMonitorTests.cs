using BetterGenshinImpact.Core.Monitor;
using Microsoft.Extensions.Logging.Abstractions;
using Vanara.PInvoke;

namespace BetterGenshinImpact.UnitTest.CoreTests.MonitorTests;

public class RelativeMouseInputMonitorTests
{
    [Fact]
    public void MultipleSubscriptions_ShouldShareOneCaptureLifecycle()
    {
        using var monitor = new TestRelativeMouseInputMonitor();
        var firstCallCount = 0;
        var secondCallCount = 0;

        var firstSubscription = monitor.Subscribe((_, _) => firstCallCount++);
        var secondSubscription = monitor.Subscribe((_, _) => secondCallCount++);

        Assert.Equal(1, monitor.StartCount);

        monitor.Emit(3, -2);
        Assert.Equal(1, firstCallCount);
        Assert.Equal(1, secondCallCount);

        firstSubscription.Dispose();
        Assert.Equal(0, monitor.StopCount);

        monitor.Emit(1, 1);
        Assert.Equal(1, firstCallCount);
        Assert.Equal(2, secondCallCount);

        secondSubscription.Dispose();
        Assert.Equal(1, monitor.StopCount);
    }

    [Fact]
    public void SubscriptionDisposal_ShouldBeIdempotentAndAllowRestart()
    {
        using var monitor = new TestRelativeMouseInputMonitor();

        var firstSubscription = monitor.Subscribe((_, _) => { });
        firstSubscription.Dispose();
        firstSubscription.Dispose();

        Assert.Equal(1, monitor.StartCount);
        Assert.Equal(1, monitor.StopCount);

        var secondSubscription = monitor.Subscribe((_, _) => { });
        Assert.Equal(2, monitor.StartCount);

        secondSubscription.Dispose();
        Assert.Equal(2, monitor.StopCount);
    }

    [Fact]
    public void ThrowingSubscriber_ShouldNotBlockOtherSubscribers()
    {
        using var monitor = new TestRelativeMouseInputMonitor();
        var successfulCallCount = 0;

        using var throwingSubscription = monitor.Subscribe((_, _) => throw new InvalidOperationException());
        using var successfulSubscription = monitor.Subscribe((_, _) => successfulCallCount++);

        monitor.Emit(1, 2);

        Assert.Equal(1, successfulCallCount);
    }

    [Fact]
    public void StartFailure_ShouldRollbackSubscription()
    {
        using var monitor = new TestRelativeMouseInputMonitor
        {
            ThrowOnNextStart = true
        };

        Assert.Throws<InvalidOperationException>(() => monitor.Subscribe((_, _) => { }));
        Assert.Equal(1, monitor.StartCount);

        using var subscription = monitor.Subscribe((_, _) => { });
        Assert.Equal(2, monitor.StartCount);
    }

    [Fact]
    public void Subscriber_ShouldBeAbleToUnsubscribeInsideCallback()
    {
        using var monitor = new TestRelativeMouseInputMonitor();
        IDisposable? subscription = null;
        subscription = monitor.Subscribe((_, _) => subscription!.Dispose());

        monitor.Emit(1, 1);

        Assert.Equal(1, monitor.StopCount);
        using var restartedSubscription = monitor.Subscribe((_, _) => { });
        Assert.Equal(2, monitor.StartCount);
    }

    [Fact]
    public async Task SubscribeDuringStop_ShouldWaitAndRestartAfterStopCompletes()
    {
        using var monitor = new TestRelativeMouseInputMonitor
        {
            BlockStop = true
        };
        var firstSubscription = monitor.Subscribe((_, _) => { });

        var stopTask = Task.Run(firstSubscription.Dispose);
        Assert.True(monitor.StopEntered.Wait(TimeSpan.FromSeconds(1)));

        var subscribeTask = Task.Run(() => monitor.Subscribe((_, _) => { }));
        await Task.Delay(50);
        Assert.False(subscribeTask.IsCompleted);

        monitor.AllowStop.Set();
        await stopTask;
        using var secondSubscription = await subscribeTask;

        Assert.Equal(2, monitor.StartCount);
        Assert.Equal(1, monitor.StopCount);
    }

    [Fact]
    public void Factory_ShouldReturnSharedInstanceForEachType()
    {
        using var directInputMonitor =
            new DirectInputMonitor(NullLogger<DirectInputMonitor>.Instance);
        using var rawInputMonitor =
            new RawInputMonitor(NullLogger<RawInputMonitor>.Instance);
        var factory = new RelativeMouseInputMonitorFactory(directInputMonitor, rawInputMonitor);

        Assert.Same(
            directInputMonitor,
            factory.Get(RelativeMouseInputType.DirectInput));
        Assert.Same(
            directInputMonitor,
            factory.Get(RelativeMouseInputType.DirectInput));
        Assert.Same(
            rawInputMonitor,
            factory.Get(RelativeMouseInputType.RawInput));
        Assert.NotSame(
            factory.Get(RelativeMouseInputType.DirectInput),
            factory.Get(RelativeMouseInputType.RawInput));
    }

    [Fact]
    public void RawInputParser_ShouldReturnSignedRelativeMovement()
    {
        var rawInput = CreateMouseRawInput(
            User32.MouseState.MOUSE_MOVE_RELATIVE,
            -7,
            11);

        var result = RawInputMonitor.TryGetRelativeMovement(
            rawInput,
            out int deltaX,
            out int deltaY);

        Assert.True(result);
        Assert.Equal(-7, deltaX);
        Assert.Equal(11, deltaY);
    }

    [Fact]
    public void RawInputParser_ShouldIgnoreAbsoluteMovement()
    {
        var rawInput = CreateMouseRawInput(
            User32.MouseState.MOUSE_MOVE_ABSOLUTE,
            100,
            200);

        Assert.False(RawInputMonitor.TryGetRelativeMovement(
            rawInput,
            out _,
            out _));
    }

    [Fact]
    public void RawInputParser_ShouldIgnoreNonMouseAndZeroMovement()
    {
        var keyboardInput = new User32.RAWINPUT
        {
            header = new User32.RAWINPUTHEADER
            {
                dwType = User32.RIM_TYPE.RIM_TYPEKEYBOARD
            }
        };
        var buttonOnlyInput = CreateMouseRawInput(
            User32.MouseState.MOUSE_MOVE_RELATIVE,
            0,
            0,
            User32.RI_MOUSE.RI_MOUSE_LEFT_BUTTON_DOWN);

        Assert.False(RawInputMonitor.TryGetRelativeMovement(
            keyboardInput,
            out _,
            out _));
        Assert.False(RawInputMonitor.TryGetRelativeMovement(
            buttonOnlyInput,
            out _,
            out _));
    }

    private static User32.RAWINPUT CreateMouseRawInput(
        User32.MouseState mouseState,
        int deltaX,
        int deltaY,
        User32.RI_MOUSE buttonFlags = 0)
    {
        return new User32.RAWINPUT
        {
            header = new User32.RAWINPUTHEADER
            {
                dwType = User32.RIM_TYPE.RIM_TYPEMOUSE
            },
            data = new User32.RAWINPUT.DATA
            {
                mouse = new User32.RAWMOUSE
                {
                    usFlags = mouseState,
                    usButtonFlags = buttonFlags,
                    lLastX = deltaX,
                    lLastY = deltaY
                }
            }
        };
    }

    private sealed class TestRelativeMouseInputMonitor()
        : RelativeMouseInputMonitorBase(NullLogger.Instance)
    {
        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public bool ThrowOnNextStart { get; set; }

        public bool BlockStop { get; set; }

        public ManualResetEventSlim StopEntered { get; } = new();

        public ManualResetEventSlim AllowStop { get; } = new();

        public void Emit(int deltaX, int deltaY)
        {
            Publish(new RelativeMouseMoveEventArgs(deltaX, deltaY, 1));
        }

        protected override void StartCore()
        {
            StartCount++;
            if (!ThrowOnNextStart)
            {
                return;
            }

            ThrowOnNextStart = false;
            throw new InvalidOperationException("Start failed");
        }

        protected override void StopCore()
        {
            StopCount++;
            StopEntered.Set();
            if (BlockStop)
            {
                AllowStop.Wait();
            }
        }
    }
}
