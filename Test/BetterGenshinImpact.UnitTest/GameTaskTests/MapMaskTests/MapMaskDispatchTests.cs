using System.Runtime.CompilerServices;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.GameTask.QuickTeleport;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.MapMaskTests;

/// <summary>覆盖真实调度入口，防止匹配器测试通过但稳定界面不再收到截图。</summary>
public class MapMaskDispatchTests
{
    /// <summary>只读取触发器的界面声明，不启动运行环境、截图器或资源加载。</summary>
    private static ITaskTrigger Trigger<T>() where T : ITaskTrigger
        => (ITaskTrigger)RuntimeHelpers.GetUninitializedObject(typeof(T));

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
