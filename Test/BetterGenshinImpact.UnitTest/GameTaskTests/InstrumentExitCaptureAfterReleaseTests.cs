using System.Diagnostics;
using BetterGenshinImpact.GameTask.Common.Job;
using Fischless.GameCapture;
using OpenCvSharp;
using Vanara.PInvoke;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

public class InstrumentExitCaptureAfterReleaseTests
{
    private static readonly TimeSpan SettleAfterRelease = TimeSpan.FromMilliseconds(60);

    [Fact]
    public void WindowRelativeCaptureRemainsUsableAfterTranslationButNotResize()
    {
        using var frame = new GameCaptureFrame(new Mat(6, 8, MatType.CV_8UC3), new RECT(10, 20, 18, 26));
        Assert.True(InstrumentExitRecovery.CaptureFitsViewport(frame, new RECT(15, 31, 23, 37)));
        Assert.False(InstrumentExitRecovery.CaptureFitsViewport(frame, new RECT(10, 20, 19, 26)));
    }

    [Fact]
    public async Task FramesOlderThanSixtyMillisecondsAfterRelease_AreDisposedAndReplacedByTheFirstSettledFrame()
    {
        var releasedAt = Stopwatch.GetElapsedTime(0);
        var capturedBeforeRelease = CreateFrame(releasedAt - TimeSpan.FromMilliseconds(40));
        var capturedAfterRelease = CreateFrame(releasedAt + TimeSpan.FromMilliseconds(30));
        var capturedBeforeSettle = CreateFrame(releasedAt + TimeSpan.FromMilliseconds(59));
        var settled = CreateFrame(releasedAt + SettleAfterRelease);
        var capture = new FakeGameCapture(capturedBeforeRelease, capturedAfterRelease, capturedBeforeSettle, settled);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var result = await InstrumentExitRecovery.CaptureAfterReleaseAsync(capture, releasedAt, timeout.Token);

        Assert.Same(settled, result);
        Assert.True(capturedBeforeRelease.Frame.IsDisposed);
        Assert.True(capturedAfterRelease.Frame.IsDisposed);
        Assert.True(capturedBeforeSettle.Frame.IsDisposed);
        Assert.False(settled.Frame.IsDisposed);
        Assert.Equal(4, capture.CaptureCount);
        settled.Dispose();
    }

    [Fact]
    public async Task AlreadyCancelledToken_DoesNotTakeAnyFrame()
    {
        var releasedAt = Stopwatch.GetElapsedTime(0);
        var cached = CreateFrame(releasedAt - TimeSpan.FromMilliseconds(40));
        var capture = new FakeGameCapture(cached);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => InstrumentExitRecovery.CaptureAfterReleaseAsync(capture, releasedAt, cts.Token));

        Assert.Equal(0, capture.CaptureCount);
        Assert.False(cached.Frame.IsDisposed);
        cached.Dispose();
    }

    [Fact]
    public async Task CancellationTriggeredByTheCachedFrame_StopsBeforeTakingAnotherFrame()
    {
        var releasedAt = Stopwatch.GetElapsedTime(0);
        var cached = CreateFrame(releasedAt + TimeSpan.FromMilliseconds(30));
        using var cts = new CancellationTokenSource();
        var capture = new FakeGameCapture(cached) { OnCapture = cts.Cancel };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => InstrumentExitRecovery.CaptureAfterReleaseAsync(capture, releasedAt, cts.Token));

        Assert.Equal(1, capture.CaptureCount);
        Assert.True(cached.Frame.IsDisposed);
    }

    private static GameCaptureFrame CreateFrame(TimeSpan systemRelativeTime)
    {
        return new GameCaptureFrame(new Mat(2, 2, MatType.CV_8UC1, Scalar.Black))
        {
            SystemRelativeTime = systemRelativeTime
        };
    }

    private sealed class FakeGameCapture(params GameCaptureFrame[] frames) : IGameCapture
    {
        private readonly Queue<GameCaptureFrame> _pending = new(frames);
        private int _captureCount;

        public int CaptureCount => Volatile.Read(ref _captureCount);

        public Action? OnCapture { get; init; }

        public bool IsCapturing => true;

        public void Start(nint hWnd, Dictionary<string, object>? settings = null)
        {
        }

        public GameCaptureFrame? Capture()
        {
            var count = Interlocked.Increment(ref _captureCount);
            if (count > frames.Length)
                throw new InvalidOperationException($"目标帧已交还，不应再取第 {count} 帧");
            var frame = _pending.Dequeue();
            OnCapture?.Invoke();
            return frame;
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
}
