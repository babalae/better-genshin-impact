using BetterGenshinImpact.GameTask;
using Fischless.GameCapture;
using Microsoft.Extensions.Time.Testing;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.RuntimeTests;

public class CaptureServiceTests
{
    private static readonly DateTimeOffset DefaultNow = DateTimeOffset.Parse("2026-04-26T00:00:00+00:00");

    [Fact]
    public void StartAndRestart_ShouldIncrementCaptureVersion_AndReuseStartContext()
    {
        var firstCapture = new FakeGameCapture();
        var secondCapture = new FakeGameCapture();
        var service = CreateService(new Queue<IGameCapture>([firstCapture, secondCapture]));

        service.Start((nint)42, CaptureModes.BitBlt);

        Assert.Same(firstCapture, service.GameCapture);
        Assert.Equal(1, service.CaptureVersion);
        Assert.Equal((nint)42, firstCapture.LastStartHandle);
        Assert.Equal(1, firstCapture.StartCallCount);

        var restarted = service.Restart("unit test");

        Assert.True(restarted);
        Assert.Same(secondCapture, service.GameCapture);
        Assert.Equal(2, service.CaptureVersion);
        Assert.Equal(1, firstCapture.StopCallCount);
        Assert.Equal((nint)42, secondCapture.LastStartHandle);
    }

    [Fact]
    public void Restart_ShouldBeRateLimitedUntilThrottleWindowExpires()
    {
        var clock = new FakeTimeProvider(DefaultNow);
        var firstCapture = new FakeGameCapture();
        var secondCapture = new FakeGameCapture();
        var thirdCapture = new FakeGameCapture();
        var service = CreateService(new Queue<IGameCapture>([firstCapture, secondCapture, thirdCapture]), clock);

        service.Start((nint)5, CaptureModes.BitBlt);

        Assert.True(service.Restart("first restart"));
        Assert.Equal(2, service.CaptureVersion);
        Assert.Same(secondCapture, service.GameCapture);

        Assert.False(service.Restart("throttled restart"));
        Assert.Equal(2, service.CaptureVersion);
        Assert.Same(secondCapture, service.GameCapture);

        clock.Advance(TimeSpan.FromMilliseconds(301));

        Assert.True(service.Restart("restart after throttle window"));
        Assert.Equal(3, service.CaptureVersion);
        Assert.Same(thirdCapture, service.GameCapture);
    }

    [Fact]
    public void CaptureNoRetry_ShouldRaiseUnavailableOnlyOnce_UntilRecovered()
    {
        var capture = new FakeGameCapture();
        capture.EnqueueCaptureResult(null);
        capture.EnqueueCaptureResult(null);
        capture.EnqueueCaptureResult(new Mat(1, 1, MatType.CV_8UC1, Scalar.All(1)));

        var service = CreateService(new Queue<IGameCapture>([capture]));
        var unavailableCount = 0;
        var recoveredCount = 0;
        service.CaptureUnavailable += (_, _) => unavailableCount++;
        service.CaptureRecovered += (_, _) => recoveredCount++;

        service.Start((nint)7, CaptureModes.BitBlt);

        Assert.Null(service.CaptureNoRetry());
        Assert.Null(service.CaptureNoRetry());

        using var image = service.CaptureNoRetry();

        Assert.NotNull(image);
        Assert.Equal(1, unavailableCount);
        Assert.Equal(1, recoveredCount);
    }

    [Fact]
    public void CaptureNoRetry_ShouldTreatCaptureExceptionsAsFailures()
    {
        var capture = new FakeGameCapture { ThrowOnCapture = true };
        var service = CreateService(new Queue<IGameCapture>([capture]));
        var unavailableCount = 0;
        service.CaptureUnavailable += (_, _) => unavailableCount++;

        service.Start((nint)8, CaptureModes.BitBlt);

        Mat? image = null;
        var exception = Record.Exception(() => image = service.CaptureNoRetry());

        Assert.Null(exception);
        Assert.Null(image);
        Assert.Equal(1, unavailableCount);
        Assert.Null(service.CaptureNoRetry());
        Assert.Equal(1, unavailableCount);
    }

    [Fact]
    public void CaptureWithRetry_ShouldRestartAndRecoverWithinRetryWindow()
    {
        var firstCapture = new FakeGameCapture();
        firstCapture.EnqueueCaptureResult(null);
        firstCapture.EnqueueCaptureResult(null);

        var secondCapture = new FakeGameCapture();
        secondCapture.EnqueueCaptureResult(new Mat(1, 1, MatType.CV_8UC1, Scalar.All(1)));

        var service = CreateService(new Queue<IGameCapture>([firstCapture, secondCapture]));
        service.Start((nint)9, CaptureModes.BitBlt);

        using var image = service.CaptureWithRetry(3, 0);

        Assert.NotNull(image);
        Assert.Equal(2, service.CaptureVersion);
        Assert.Equal(1, firstCapture.StopCallCount);
        Assert.Same(secondCapture, service.GameCapture);
    }

    [Fact]
    public void Start_ShouldRemainRecoverable_WhenCaptureStartThrows()
    {
        var failedCapture = new FakeGameCapture { ThrowOnStart = true };
        var recoveredCapture = new FakeGameCapture();
        var service = CreateService(new Queue<IGameCapture>([failedCapture, recoveredCapture]));
        var unavailableCount = 0;
        var recoveredCount = 0;
        service.CaptureUnavailable += (_, _) => unavailableCount++;
        service.CaptureRecovered += (_, _) => recoveredCount++;

        service.Start((nint)10, CaptureModes.BitBlt);

        Assert.Null(service.GameCapture);
        Assert.False(service.IsCapturing);
        Assert.Equal(0, service.CaptureVersion);
        Assert.Equal(1, unavailableCount);

        Assert.True(service.Restart("recover after start failure"));
        Assert.Same(recoveredCapture, service.GameCapture);
        Assert.True(service.IsCapturing);
        Assert.Equal(1, service.CaptureVersion);
        Assert.Equal(1, recoveredCount);
    }

    [Fact]
    public void CaptureNoRetry_ShouldRaisePermanentFailure_AfterFiveSecondsOfContinuousFailure()
    {
        var clock = new FakeTimeProvider(DefaultNow);
        var capture = new FakeGameCapture();
        capture.EnqueueCaptureResult(null);
        capture.EnqueueCaptureResult(null);

        var service = CreateService(new Queue<IGameCapture>([capture]), clock);
        var permanentFailureCount = 0;
        service.PermanentFailure += (_, _) => permanentFailureCount++;

        service.Start((nint)11, CaptureModes.BitBlt);

        Assert.Null(service.CaptureNoRetry());
        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Null(service.CaptureNoRetry());

        Assert.Equal(1, permanentFailureCount);
    }

    [Fact]
    public void PermanentFailureWindow_ShouldResetAfterRecovery()
    {
        var clock = new FakeTimeProvider(DefaultNow);
        var capture = new FakeGameCapture();
        capture.EnqueueCaptureResult(null);
        capture.EnqueueCaptureResult(null);
        capture.EnqueueCaptureResult(new Mat(1, 1, MatType.CV_8UC1, Scalar.All(1)));
        capture.EnqueueCaptureResult(null);
        capture.EnqueueCaptureResult(null);
        capture.EnqueueCaptureResult(null);

        var service = CreateService(new Queue<IGameCapture>([capture]), clock);
        var permanentFailureCount = 0;
        var recoveredCount = 0;
        service.PermanentFailure += (_, _) => permanentFailureCount++;
        service.CaptureRecovered += (_, _) => recoveredCount++;

        service.Start((nint)12, CaptureModes.BitBlt);

        Assert.Null(service.CaptureNoRetry());
        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Null(service.CaptureNoRetry());

        using var recoveredImage = service.CaptureNoRetry();

        Assert.NotNull(recoveredImage);
        Assert.Equal(1, permanentFailureCount);
        Assert.Equal(1, recoveredCount);

        Assert.Null(service.CaptureNoRetry());

        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(1, permanentFailureCount);
        Assert.Null(service.CaptureNoRetry());

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(service.CaptureNoRetry());
        Assert.Equal(2, permanentFailureCount);
    }

    [Fact]
    public void Stop_ShouldClearStartContext_AndPreventRestart()
    {
        var capture = new FakeGameCapture();
        var service = CreateService(new Queue<IGameCapture>([capture]));
        service.Start((nint)13, CaptureModes.BitBlt);

        service.Stop();

        Assert.Null(service.GameCapture);
        Assert.False(service.IsCapturing);
        Assert.False(service.Restart("after stop"));
        Assert.Equal(1, capture.StopCallCount);
    }

    [Fact]
    public void Stop_ShouldSwallowStopExceptions_AndClearServiceState()
    {
        var capture = new FakeGameCapture { ThrowOnStop = true };
        var service = CreateService(new Queue<IGameCapture>([capture]));
        service.Start((nint)14, CaptureModes.BitBlt);

        var exception = Record.Exception(() => service.Stop());

        Assert.Null(exception);
        Assert.Null(service.GameCapture);
        Assert.False(service.IsCapturing);
        Assert.False(service.Restart("after failed stop"));
        Assert.Equal(1, capture.StopCallCount);
        Assert.Equal(0, capture.DisposeCallCount);
    }

    [Fact]
    public void Stop_ShouldSwallowDisposeExceptions_AndClearServiceState()
    {
        var capture = new FakeGameCapture { ThrowOnDispose = true };
        var service = CreateService(new Queue<IGameCapture>([capture]));
        service.Start((nint)16, CaptureModes.BitBlt);

        var exception = Record.Exception(() => service.Stop());

        Assert.Null(exception);
        Assert.Null(service.GameCapture);
        Assert.False(service.IsCapturing);
        Assert.False(service.Restart("after failed dispose"));
        Assert.Equal(1, capture.StopCallCount);
        Assert.Equal(1, capture.DisposeCallCount);
    }

    [Fact]
    public void Start_WithZeroHandle_ShouldNotCreateCaptureOrAllowRestart()
    {
        var capture = new FakeGameCapture();
        var service = CreateService(new Queue<IGameCapture>([capture]));

        service.Start(IntPtr.Zero, CaptureModes.BitBlt);

        Assert.Null(service.GameCapture);
        Assert.False(service.IsCapturing);
        Assert.Equal(0, service.CaptureVersion);
        Assert.Equal(0, capture.StartCallCount);
        Assert.False(service.Restart("zero handle"));
    }

    private static CaptureService CreateService(Queue<IGameCapture> captures, FakeTimeProvider? clock = null)
    {
        return new CaptureService(new FakeLogger<CaptureService>())
        {
            CaptureFactory = _ => captures.Dequeue(),
            StartSettingsFactory = () => [],
            Clock = clock ?? new FakeTimeProvider(DefaultNow),
        };
    }

    private sealed class FakeGameCapture : IGameCapture
    {
        private readonly Queue<Mat?> _captureResults = new();

        public bool IsCapturing { get; private set; }

        public bool ThrowOnStart { get; set; }

        public bool ThrowOnCapture { get; set; }

        public bool ThrowOnStop { get; set; }

        public bool ThrowOnDispose { get; set; }

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public int DisposeCallCount { get; private set; }

        public nint LastStartHandle { get; private set; }

        public void EnqueueCaptureResult(Mat? result)
        {
            _captureResults.Enqueue(result);
        }

        public void Start(nint hWnd, Dictionary<string, object>? settings = null)
        {
            LastStartHandle = hWnd;
            StartCallCount++;

            if (ThrowOnStart)
            {
                throw new InvalidOperationException("Start failed");
            }

            IsCapturing = true;
        }

        public Mat? Capture()
        {
            if (ThrowOnCapture)
            {
                throw new InvalidOperationException("Capture failed");
            }

            if (_captureResults.Count == 0)
            {
                return null;
            }

            return _captureResults.Dequeue();
        }

        public void Stop()
        {
            StopCallCount++;

            if (ThrowOnStop)
            {
                throw new InvalidOperationException("Stop failed");
            }

            IsCapturing = false;
        }

        public void Dispose()
        {
            DisposeCallCount++;

            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("Dispose failed");
            }

            while (_captureResults.Count > 0)
            {
                _captureResults.Dequeue()?.Dispose();
            }
        }
    }
}