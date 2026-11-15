using System.Runtime.CompilerServices;
using System.Reflection;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.GameTask.QuickTeleport;
using OpenCvSharp;
using Vanara.PInvoke;
using Rect = System.Windows.Rect;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>覆盖真实调度入口，防止匹配器测试通过但稳定界面不再收到截图。</summary>
public class MapMaskDispatchTests
{
    /// <summary>只读取触发器的界面声明，不启动运行环境、截图器或资源加载。</summary>
    private static ITaskTrigger Trigger<T>() where T : ITaskTrigger
        => (ITaskTrigger)RuntimeHelpers.GetUninitializedObject(typeof(T));

    /// <summary>模拟 worker 观察到的内部场景，避免为测试启动真实截图或输入。</summary>
    private static void SetField(MapMaskTrigger trigger, string name, object value) =>
        typeof(MapMaskTrigger).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(trigger, value);

    /// <summary>停用清空共享快照后，重新启用时必须和视口一起恢复原独立地图类型。</summary>
    [Fact]
    public void MiniMap_Reenabled_RestoresSceneAndRejectsPreviousGeneration()
    {
        using var runtime = TestGameRuntime.Create(new FakeSystemInfo(new RECT(0, 0, 1920, 1080), 1));
        var state = (MaskWindowMapState)runtime.MaskWindowMapState;
        var trigger = new MapMaskTrigger(runtime, new AllConfig());
        SetField(trigger, "_currentMapType", (int)MapTypes.Enkanomiya);
        trigger.OnEnabled(null);
        runtime.MaskWindowMapState.Update(mapType: MapTypes.Enkanomiya);
        trigger.OnDisabled();
        trigger.OnEnabled(null);
        var viewport = new Rect(10, 20, 30, 40);
        Assert.False(trigger.PublishMiniMapComputeResult(MapTypes.Enkanomiya, 0, viewport));
        Assert.True(trigger.PublishMiniMapComputeResult(MapTypes.Enkanomiya, 1, viewport));
        Assert.Equal(MapTypes.Enkanomiya, state.Current.MapType);
        Assert.Equal(viewport, state.Current.MiniMapViewport);
    }

    /// <summary>等待场景提交锁的小地图结果，取得锁后必须重新校验，不能覆盖新场景的空视口。</summary>
    [Fact]
    public async Task MiniMap_SceneChangesBeforePublication_RejectsStaleViewport()
    {
        using var runtime = TestGameRuntime.Create(new FakeSystemInfo(new RECT(0, 0, 1920, 1080), 1));
        var state = (MaskWindowMapState)runtime.MaskWindowMapState;
        var trigger = new MapMaskTrigger(runtime, new AllConfig());
        trigger.OnEnabled(null);
        SetField(trigger, "_currentMapType", (int)MapTypes.Enkanomiya);
        var gate = typeof(MapMaskTrigger).GetField("_bigMapCancellationLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(trigger)!;
        using var started = new ManualResetEventSlim();
        Task<bool> publication;
        lock (gate)
        {
            publication = Task.Run(() =>
            {
                started.Set();
                return trigger.PublishMiniMapComputeResult(MapTypes.Enkanomiya, 0, new Rect(10, 20, 30, 40));
            });
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            SetField(trigger, "_currentMapType", (int)MapTypes.MoonCanon);
            runtime.MaskWindowMapState.Update(mapType: MapTypes.MoonCanon, miniMapViewport: new Rect());
        }
        Assert.False(await publication.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(MapTypes.MoonCanon, state.Current.MapType);
        Assert.Equal(new Rect(), state.Current.MiniMapViewport);
    }

    /// <summary>首帧识别出独立地图后，当前绝对定位必须立即建立参考，无需再停下来定位一次。</summary>
    [Fact]
    public async Task BigMap_SceneChanges_FirstMatchedFrameCanTrackImmediately()
    {
        using var runtime = TestGameRuntime.Create(new FakeSystemInfo(new RECT(0, 0, 1920, 1080), 1));
        var trigger = new MapMaskTrigger(runtime, new AllConfig());
        trigger.OnEnabled(null);
        SetField(trigger, "_inBigMap", true);
        SetField(trigger, "_bigMapRevision", 1L);
        using var reader = new BigMapSceneReader { Recognize = _ => "霜月之岭" };
        SetField(trigger, "_sceneReader", reader);
        using var tracker = (BigMapMotionTracker)typeof(MapMaskTrigger)
            .GetField("_motionTracker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(trigger)!;
        using var color = MapMaskTestImages.Load("moon-highland");
        using var gray = color.CvtColor(ColorConversionCodes.BGR2GRAY);
        Assert.False(tracker.TryTrack(gray, 1, out _));

        // 调用真实 worker 入口，但任务图片直接来自离线样本，不连接游戏或调度截图。
        var workType = typeof(MapMaskTrigger).GetNestedType("ComputeWorkItem", BindingFlags.NonPublic)!;
        using var work = (IDisposable)Activator.CreateInstance(workType)!;
        workType.GetProperty("MapMatchingMethod")!.SetValue(work, "SIFT");
        workType.GetProperty("Mat")!.SetValue(work, gray.Clone());
        workType.GetProperty("ColorMat")!.SetValue(work, color.Clone());
        workType.GetProperty("BigMapRevision")!.SetValue(work, 1L);
        try
        {
            var compute = (Task)typeof(MapMaskTrigger)
                .GetMethod("ProcessBigMapComputeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(trigger, [work])!;
            await compute.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(MapTypes.MoonCanon, ((MaskWindowMapState)runtime.MaskWindowMapState).Current.MapType);
            Assert.True(tracker.TryTrack(gray, 2, out _));
        }
        finally
        {
            (typeof(MapMaskTrigger).GetField("_cascadeMatcher", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(trigger) as IDisposable)?.Dispose();
        }
    }

    /// <summary>地图停留超过 30 秒直到一小时，仍须收到截图才能重试定位及检测后续拖动。</summary>
    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(300)]
    [InlineData(3600)]
    public void StableBigMap_ContinuesReceivingFrames(int seconds)
    {
        var trigger = Trigger<MapMaskTrigger>();
        Assert.True(TaskTriggerDispatcher.ShouldRunForGameUi(trigger, GameUiCategory.BigMap,
            GameUiCategory.BigMap, TimeSpan.FromSeconds(seconds)));
    }

    /// <summary>主界面小地图及其它界面的遮罩清理同样必须持续执行。</summary>
    [Theory]
    [InlineData(GameUiCategory.Unknown)]
    [InlineData(GameUiCategory.Main)]
    [InlineData(GameUiCategory.Talk)]
    public void StableOtherUi_ContinuesReceivingFrames(GameUiCategory category)
    {
        Assert.True(TaskTriggerDispatcher.ShouldRunForGameUi(Trigger<MapMaskTrigger>(), category,
            category, TimeSpan.FromHours(1)));
    }

    /// <summary>开关地图以及弹出详情时，跨界面状态清理不能漏帧。</summary>
    [Theory]
    [InlineData(GameUiCategory.Unknown, GameUiCategory.BigMap)]
    [InlineData(GameUiCategory.BigMap, GameUiCategory.Unknown)]
    [InlineData(GameUiCategory.BigMap, GameUiCategory.Talk)]
    public void UiTransition_ReceivesFrame(GameUiCategory previous, GameUiCategory current)
    {
        Assert.True(TaskTriggerDispatcher.ShouldRunForGameUi(Trigger<MapMaskTrigger>(), current,
            previous, TimeSpan.FromHours(1)));
    }

    /// <summary>地图跨界面支持不扩大快速传送或自动剧情的界面范围，仍保留原有 30 秒过渡期。</summary>
    [Fact]
    public void SingleUiTriggers_KeepExistingFilteringAndTransitionGrace()
    {
        var teleport = Trigger<QuickTeleportTrigger>();
        var autoSkip = Trigger<AutoSkipTrigger>();
        Assert.True(TaskTriggerDispatcher.ShouldRunForGameUi(teleport, GameUiCategory.BigMap,
            GameUiCategory.BigMap, TimeSpan.FromHours(1)));
        Assert.False(TaskTriggerDispatcher.ShouldRunForGameUi(teleport, GameUiCategory.Unknown,
            GameUiCategory.Unknown, TimeSpan.FromSeconds(31)));
        Assert.True(TaskTriggerDispatcher.ShouldRunForGameUi(autoSkip, GameUiCategory.Talk,
            GameUiCategory.Talk, TimeSpan.FromHours(1)));
        Assert.False(TaskTriggerDispatcher.ShouldRunForGameUi(autoSkip, GameUiCategory.BigMap,
            GameUiCategory.BigMap, TimeSpan.FromSeconds(31)));
        Assert.True(TaskTriggerDispatcher.ShouldRunForGameUi(autoSkip, GameUiCategory.BigMap,
            GameUiCategory.BigMap, TimeSpan.FromSeconds(30)));
        Assert.True(TaskTriggerDispatcher.ShouldRunForGameUi(autoSkip, GameUiCategory.BigMap,
            GameUiCategory.Unknown, TimeSpan.FromHours(1)));
    }
}
