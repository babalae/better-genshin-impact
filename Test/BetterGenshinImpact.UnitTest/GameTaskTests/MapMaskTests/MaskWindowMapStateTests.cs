using BetterGenshinImpact.Core.Mask;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using BetterGenshinImpact.ViewModel;
using Rect = System.Windows.Rect;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>地图诊断文本与视口合并时的状态回归。</summary>
public class MaskWindowMapStateTests
{
    /// <summary>小地图增量发布不覆盖已确认的独立坐标系，解绑则恢复默认场景。</summary>
    [Fact]
    public void Update_IndependentScene_PreservesSceneAcrossViewportUpdates()
    {
        var state = new MaskWindowMapState();
        state.Update(mapType: MapTypes.MoonCanon, bigMapViewport: new Rect(1, 2, 3, 4));
        state.Update(miniMapViewport: new Rect(10, 20, 30, 40));
        Assert.Equal(MapTypes.MoonCanon, state.Current.MapType);
        state.Reset();
        Assert.Equal(MapTypes.Teyvat, state.Current.MapType);
    }
    /// <summary>UI 延迟包含排队、初始化及调度等待，定位总耗时还须覆盖截图之前的稳定等待与重试。</summary>
    [Fact]
    public void DebugTiming_IncludesQueueAndUiDelayAndPreservesCaptureTime()
    {
        var state = new MaskWindowMapState();
        state.Update(bigMapDebugText: "计算: 12ms", bigMapFrameTick: 1300, bigMapRequestTick: 1000);
        state.Update(miniMapViewport: new Rect(1, 2, 3, 4));
        var text = MaskWindowViewModel.FormatBigMapDebugText(state.Current, 1600);
        Assert.Contains("计算: 12ms", text);
        Assert.Contains("截图到UI: 300ms", text);
        Assert.Contains("本次重定位: 600ms", text);
        state.Reset();
        Assert.Equal(0, state.Current.BigMapFrameTick);
        Assert.Equal(0, state.Current.BigMapRequestTick);
        Assert.Null(MaskWindowViewModel.FormatBigMapDebugText(state.Current, 2000));
    }

    /// <summary>小地图更新不覆盖大地图诊断；空字符串用于显式清除诊断。</summary>
    [Fact]
    public void Update_MergesFieldsAndCanClearDebugText()
    {
        var state = new MaskWindowMapState();
        state.Update(isInBigMap: true, bigMapViewport: new Rect(100, 200, 960, 540), bigMapDebugText: "已定位");
        state.Update(miniMapViewport: new Rect(300, 400, 100, 100));
        Assert.Equal("已定位", state.Current.BigMapDebugText);
        Assert.Equal(new Rect(100, 200, 960, 540), state.Current.BigMapViewport);
        state.Update(bigMapDebugText: string.Empty);
        Assert.Equal(string.Empty, state.Current.BigMapDebugText);
    }

    /// <summary>停用或解绑后不能残留旧坐标或诊断。</summary>
    [Fact]
    public void Reset_ClearsViewportsAndDebugText()
    {
        var state = new MaskWindowMapState();
        state.Update(isInBigMap: true, bigMapViewport: new Rect(1, 2, 3, 4), bigMapDebugText: "旧会话");
        state.Reset();
        Assert.Equal(MaskWindowMapSnapshot.Empty, state.Current);
        Assert.Null(state.Current.BigMapDebugText);
    }

    /// <summary>相同快照不重复通知，防止后台刷新放大 UI 工作量。</summary>
    [Fact]
    public void Update_IdenticalSnapshot_DoesNotNotifyAgain()
    {
        var state = new MaskWindowMapState();
        var notifications = 0;
        state.Changed += () => notifications++;
        state.Update(bigMapDebugText: "已定位");
        state.Update(bigMapDebugText: "已定位");
        Assert.Equal(1, notifications);
    }
}
