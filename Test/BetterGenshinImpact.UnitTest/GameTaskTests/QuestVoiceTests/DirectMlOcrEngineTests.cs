using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>原生边界替身验证生产引擎所有权；真实DirectML另由独立组件基准验证。</summary>
public sealed class DirectMlOcrEngineTests
{
    /// <summary>固定模型身份，仅工厂替身不读取实际文件。</summary>
    private static OcrSettings Settings => new("controlled.onnx", "");
    /// <summary>通过生产串行入口提交一个最小ROI。</summary>
    private static Task<(IReadOnlyList<OcrText> Texts, OcrModelInfo Model)> Run(DirectMlOcrEngine engine, CancellationToken token = default)
        => engine.RecognizeAsync([new(new float[3 * 48 * 320], 320)], Settings, "controlled-profile", token);

    /// <summary>多批复用，并在关闭后拒绝请求且重复释放不增加计数。</summary>
    [Fact]
    public async Task ReusesSessionAndRejectsRequestsAfterIdempotentDispose()
    {
        var backend = new Native();
        var creates = 0;
        var engine = new DirectMlOcrEngine((_, _, _) => { creates++; return backend; });
        await Run(engine); await Run(engine);
        Assert.Equal(1, creates); Assert.Equal(2, backend.Runs);
        engine.Dispose(); engine.Dispose();
        Assert.Equal(1, backend.Disposals);
        Assert.Equal(1, engine.Diagnostics[QuestVoiceMetric.Initialization].Count);
        Assert.Equal(1, engine.Diagnostics[QuestVoiceMetric.Disposal].Count);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => Run(engine));
    }

    /// <summary>初始化异常不会保留部分会话，下次同一配置可重试。</summary>
    [Fact]
    public async Task InitializationFailureCanRetryWithoutDisposingUnownedResources()
    {
        var backend = new Native();
        var creates = 0;
        using var engine = new DirectMlOcrEngine((_, _, _) =>
            ++creates == 1 ? throw new OcrException(OcrErrorCode.ModelInitialization, "controlled init failure") : backend);
        await Assert.ThrowsAsync<OcrException>(() => Run(engine));
        await Run(engine);
        Assert.Equal(2, creates); Assert.Equal(0, backend.Disposals);
    }

    /// <summary>原生错误或取消均废弃会话，下批新建，不复用Terminate状态。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledNativeCallRetiresBeforeNextBatch(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var first = new Native { OnRun = token =>
        {
            if (cancel) { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            throw new OcrException(OcrErrorCode.Inference, "controlled native failure");
        }};
        var second = new Native();
        var creates = 0;
        using var engine = new DirectMlOcrEngine((_, _, _) => ++creates == 1 ? first : second);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(engine, cancellation.Token));
        else await Assert.ThrowsAsync<OcrException>(() => Run(engine));
        Assert.Equal(1, first.Disposals);
        await Run(engine); await Run(engine);
        Assert.Equal(2, creates); Assert.Equal(2, second.Runs);
        Assert.Equal(1, engine.Diagnostics[QuestVoiceMetric.Retired].Count);
    }

    /// <summary>关闭等待非协作在途Run归还，排队请求不使用已释放会话。</summary>
    [Fact]
    public async Task DisposeDrainsInFlightRunAndQueuedWorkCannotRun()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var backend = new Native { OnRun = _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); } };
        var metrics = new QuestVoiceMetrics();
        var engine = new DirectMlOcrEngine((_, _, _) => backend, metrics);
        var first = Run(engine);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var queued = Run(engine);
        var disposing = Task.Run(engine.Dispose);
        try
        {
            var watch = Stopwatch.StartNew();
            while (metrics.Snapshot()[QuestVoiceMetric.Closing].Count == 0 && watch.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Yield();
            Assert.Equal(1, metrics.Snapshot()[QuestVoiceMetric.Closing].Count);
            Assert.Equal(0, backend.Disposals);
            Assert.False(disposing.IsCompleted);
        }
        finally { release.Set(); }
        await first; await disposing.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
        Assert.Equal(1, backend.Runs); Assert.Equal(1, backend.Disposals);
        Assert.False(backend.DisposedWhileRunning);
        engine.Dispose();
    }

    /// <summary>只取消gate等待者不会废弃另一个请求正在使用的会话。</summary>
    [Fact]
    public async Task CancelledWaiterDoesNotRetireOwnersSession()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var backend = new Native { OnRun = _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); } };
        using var engine = new DirectMlOcrEngine((_, _, _) => backend);
        var active = Run(engine);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var waiting = Run(engine, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(0, backend.Disposals);
        release.Set(); await active;
        backend.OnRun = null; await Run(engine);
        Assert.Equal(1, engine.Diagnostics[QuestVoiceMetric.Initialization].Count);
    }

    /// <summary>同一引擎禁止热切换设备；新运行轮次仍能新建。</summary>
    [Fact]
    public async Task ModelAndDeviceAreFrozenUntilNewEngine()
    {
        using var engine = new DirectMlOcrEngine((_, _, _) => new Native());
        await Run(engine);
        await Assert.ThrowsAsync<OcrException>(() => engine.RecognizeAsync([], Settings with { DeviceId = 1 }, "", default));
        await Assert.ThrowsAsync<OcrException>(() => engine.RecognizeAsync([], Settings with { ModelPath = "other.onnx" }, "", default));
        await Run(engine);
        Assert.Equal(1, engine.Diagnostics[QuestVoiceMetric.Initialization].Count);
        using var restarted = new DirectMlOcrEngine((_, _, _) => new Native());
        await Run(restarted);
        Assert.Equal(1, restarted.Diagnostics[QuestVoiceMetric.Initialization].Count);
    }

    /// <summary>并行记录正确累计，另一轮计数不共享。</summary>
    [Fact]
    public void MetricsAreAtomicAndScopedToOneRun()
    {
        var metrics = new QuestVoiceMetrics();
        Parallel.For(0, 1000, _ => metrics.Record(QuestVoiceMetric.NativeRun, Stopwatch.Frequency));
        Assert.Equal(new QuestVoiceMeasurement(1000, 1_000_000, 1000), metrics.Snapshot()[QuestVoiceMetric.NativeRun]);
        Assert.Equal(0, new QuestVoiceMetrics().Snapshot()[QuestVoiceMetric.NativeRun].Count);
    }

    /// <summary>受控原生外部边界，不运行GPU，也不将替身的Model当作DML证明。</summary>
    private sealed class Native : IDirectMlOcrSession
    {
        /// <summary>测试动作可阻塞、取消或抛错。</summary>
        public Action<CancellationToken>? OnRun { get; set; }
        /// <summary>实际进入Run次数。</summary>
        public int Runs { get; private set; }
        /// <summary>原生释放次数。</summary>
        public int Disposals { get; private set; }
        /// <summary>是否错误地在Run在途时释放。</summary>
        public bool DisposedWhileRunning { get; private set; }
        /// <summary>在途标记。</summary>
        private bool _running;
        /// <summary>替身元数据。</summary>
        public OcrModelInfo Model => new("controlled", "hash", "fake-native", 0, null);
        /// <summary>执行控制动作并确保在途标记可靠归还。</summary>
        public IReadOnlyList<OcrText> Run(IReadOnlyList<OcrTensor> tensors, CancellationToken token)
        {
            _running = true; Runs++;
            try { OnRun?.Invoke(token); return [new("固定结果", .99)]; }
            finally { _running = false; }
        }
        /// <summary>记录所有权错误与重复释放。</summary>
        public void Dispose() { DisposedWhileRunning |= _running; Disposals++; }
    }
}
