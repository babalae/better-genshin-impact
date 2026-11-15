using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask.MapMask;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>通过独立任务门验证实际并发、取消回收和异常隔离，不依赖识图耗时猜测执行顺序。</summary>
public class BigMapParallelStagesTests
{
    /// <summary>模拟无法立即退出的原生分支，结果必须先发布，图像仍须保留到该分支退出。</summary>
    [Fact]
    public async Task RunAsync_PublishesBeforeSlowLoserExits_AndRetainsFrameResources()
    {
        var entered = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var winnerGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nativeGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new TaskCompletionSource<BigMapStageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var frame = new Mat(8, 8, MatType.CV_8UC1, Scalar.All(42));
        var retainedPixel = 0;
        // 三个分支由独立任务门控制，资源验证不依赖实际 OpenCV 运算耗时。
        async Task<BigMapStageResult> Stage(int index, CancellationToken token)
        {
            entered[index].SetResult();
            if (index == 0)
            {
                await winnerGate.Task.WaitAsync(token);
                return new(Result("L3-Anchors"), "成功");
            }
            if (index == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            // 模拟已经进入的原生函数：即使收到取消也要等到此调用完成。
            await nativeGate.Task;
            Assert.False(frame.IsDisposed);
            Interlocked.Exchange(ref retainedPixel, frame.At<byte>(0, 0));
            return new(null, "原生调用完成");
        }
        // 任务全部退出后才释放帧，提前发布回调不能转移或结束帧的所有权。
        async Task<BigMapStageResult> RunFrame()
        {
            using (frame)
            {
                return await BigMapParallelStages.RunAsync(t => Stage(0, t), t => Stage(1, t),
                    t => Stage(2, t), false, default, value => published.TrySetResult(value));
            }
        }
        var running = RunFrame();
        try
        {
            await Task.WhenAll(entered.Select(t => t.Task)).WaitAsync(TimeSpan.FromSeconds(5));
            winnerGate.SetResult();
            var ready = await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("L3-Anchors", ready.Result?.Via);
            Assert.Contains("处理中", ready.Diagnostics);
            Assert.Contains("排队", ready.Diagnostics);
            Assert.False(running.IsCompleted);
            Assert.False(frame.IsDisposed);
        }
        finally
        {
            winnerGate.TrySetResult();
            nativeGate.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal(42, retainedPixel);
        Assert.True(frame.IsDisposed);
    }

    /// <summary>地下先发布未确认楼层的坐标，L5 成功后升级楼层，两次发布都不能被慢分支收尾拖住。</summary>
    [Fact]
    public async Task RunAsync_UndergroundPublishesCoordinatesThenLayer_BeforeCleanup()
    {
        var surfaceEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLayer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSurface = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coordinates = new TaskCompletionSource<BigMapStageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var layer = new TaskCompletionSource<BigMapStageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = BigMapParallelStages.RunAsync(
            _ => Task.FromResult(new BigMapStageResult(Result("L3-Anchors", null), "坐标通过")),
            async _ =>
            {
                surfaceEntered.SetResult();
                await releaseSurface.Task;
                return new BigMapStageResult(null, "原生调用完成");
            },
            async _ =>
            {
                await releaseLayer.Task;
                return new BigMapStageResult(Result("L5-LayerSIFT", -1), "层通过");
            }, true, default, value =>
            {
                if (value.Result?.Via == "L3-Anchors") coordinates.TrySetResult(value);
                if (value.Result?.Via == "L5-LayerSIFT") layer.TrySetResult(value);
            });
        try
        {
            var ready = await coordinates.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(ready.Result?.Floor);
            Assert.False(running.IsCompleted);
            await surfaceEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            releaseLayer.SetResult();
            var confirmed = await layer.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(-1, confirmed.Result?.Floor);
            Assert.False(running.IsCompleted);
        }
        finally
        {
            releaseLayer.TrySetResult();
            releaseSurface.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>已发布平面坐标后发生移动，旧帧迟到的楼层结果不得再次发布。</summary>
    [Fact]
    public async Task RunAsync_CancelAfterEarlyPublication_SuppressesLateLayer()
    {
        using var cancellation = new CancellationTokenSource();
        var layerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLayer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var running = BigMapParallelStages.RunAsync(
            _ => Task.FromResult(new BigMapStageResult(Result("L3-Anchors", null), "坐标通过")),
            _ => Task.FromResult(new BigMapStageResult(null, "未匹配")),
            async _ =>
            {
                layerEntered.SetResult();
                await releaseLayer.Task;
                return new BigMapStageResult(Result("L5-LayerSIFT", -1), "迟到楼层");
            }, true, cancellation.Token, _ =>
            {
                Interlocked.Increment(ref count);
                published.TrySetResult();
            });
        try
        {
            await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await layerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            cancellation.Cancel();
            releaseLayer.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        Assert.Equal(1, count);
    }

    /// <summary>构造不同来源的已验证视口，调度测试不依赖地图素材。</summary>
    private static MapMatchResult Result(string via, int? floor = 0) =>
        new(new Rect(100, 200, 960, 540), new Point2f(1, 2), floor, null, 1, via);

    /// <summary>三个分支必须同时进入，地表成功后其它两个分支取消且完成资源回收。</summary>
    [Fact]
    public async Task RunAsync_StartsAllStagesAndDrainsCanceledLosers()
    {
        var entered = Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = 0;
        async Task<BigMapStageResult> Stage(int index, CancellationToken token)
        {
            entered[index].SetResult();
            try
            {
                if (index == 0)
                {
                    await release.Task.WaitAsync(token);
                    return new(Result("L3-Anchors"), "成功");
                }
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new(null, "不应到达");
            }
            finally
            {
                Interlocked.Increment(ref exited);
            }
        }
        var running = BigMapParallelStages.RunAsync(t => Stage(0, t), t => Stage(1, t), t => Stage(2, t), false, default);
        await Task.WhenAll(entered.Select(t => t.Task)).WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult();
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("L3-Anchors", result.Result?.Via);
        Assert.Equal(3, exited);
        Assert.Contains("L4: 已取消", result.Diagnostics);
        Assert.Contains("L5: 已取消", result.Diagnostics);
    }

    /// <summary>地下不能把先完成的平面定位直接当成已确认楼层，须等待独立 L5 核验。</summary>
    [Fact]
    public async Task RunAsync_UndergroundWaitsForLayerConfirmation()
    {
        var layerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLayer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = BigMapParallelStages.RunAsync(
            _ => Task.FromResult(new BigMapStageResult(Result("L3-Anchors", null), "坐标通过")),
            _ => Task.FromResult(new BigMapStageResult(Result("L4-SIFT"), "地表背景")),
            async token =>
            {
                layerEntered.SetResult();
                await releaseLayer.Task.WaitAsync(token);
                return new BigMapStageResult(Result("L5-LayerSIFT", -1), "层通过");
            }, true, default);
        await layerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(running.IsCompleted);
        releaseLayer.SetResult();
        var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("L5-LayerSIFT", result.Result?.Via);
        Assert.Equal(-1, result.Result?.Floor);
    }

    /// <summary>移动取消整帧后所有分支退出，后续一帧仍可重新运行。</summary>
    [Fact]
    public async Task RunAsync_CallerCancellation_DrainsAllStagesAndAllowsRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var exited = 0;
        async Task<BigMapStageResult> Stage(CancellationToken token)
        {
            if (Interlocked.Increment(ref count) == 3)
            {
                entered.SetResult();
            }
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new(null, "不应到达");
            }
            finally
            {
                Interlocked.Increment(ref exited);
            }
        }
        var running = BigMapParallelStages.RunAsync(Stage, Stage, Stage, true, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(3, exited);
        var retry = await BigMapParallelStages.RunAsync(
            _ => Task.FromResult(new BigMapStageResult(Result("L3-Anchors"), "重试通过")),
            _ => Task.FromResult(new BigMapStageResult(null, "未匹配")),
            _ => Task.FromResult(new BigMapStageResult(null, "未匹配")), false, default);
        Assert.NotNull(retry.Result);
    }

    /// <summary>一个分支抛异常不能让其它有效分支失去发布机会。</summary>
    [Fact]
    public async Task RunAsync_StageFault_DoesNotBlockOtherStages()
    {
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = await BigMapParallelStages.RunAsync(
            _ =>
            {
                failed.SetResult();
                throw new InvalidOperationException("test failure");
            },
            async _ =>
            {
                await failed.Task;
                return new BigMapStageResult(Result("L4-SIFT"), "成功");
            },
            _ => Task.FromResult(new BigMapStageResult(null, "未匹配")), false, default);
        Assert.Equal("L4-SIFT", result.Result?.Via);
        Assert.Contains("L3: 分支异常", result.Diagnostics);
    }
}
