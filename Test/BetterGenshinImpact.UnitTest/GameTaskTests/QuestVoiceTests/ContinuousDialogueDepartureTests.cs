using System;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Continuous;
using Xunit;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.QuestVoiceTests;

/// <summary>验证场景不确定只能失效未播候选，不能提前停止当前声音。</summary>
public sealed class ContinuousDialogueDepartureTests
{
    /// <summary>贯通未知布局事件到真实声音状态机，旧播放不能被取消。</summary>
    [Theory]
    [InlineData("UnsupportedTextGeometry")]
    [InlineData("ConflictingLayouts")]
    public async Task UnknownLayoutPreservesActivePlayback(string reason)
    {
        await using var f = new ContinuousFrameFixture(); await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete("A");
        (await f.Voice.Engine.NextAsync()).Complete(); var a = await f.Voice.Player.NextAsync();
        f.Inspect.NonSelectedKind = SceneDecisionKind.Unknown; f.Inspect.UnknownReason = reason;
        await f.Frame(3, 500); await f.Frame(4, 1100);
        Assert.False(a.Token.IsCancellationRequested); Assert.False(a.Stop.Task.IsCompleted);
        Assert.Equal(0, f.Session.Snapshot.Epoch);
    }

    /// <summary>不支持几何与未知场景均保留epoch，可信退出不能凭单帧或跨gap累计。</summary>
    [Theory]
    [InlineData(SceneDecisionKind.Unknown, "UnsupportedTextGeometry")]
    [InlineData(SceneDecisionKind.Unknown, "ConflictingLayouts")]
    public void UnknownAndTrustedExitNeedIndependentEvidence(SceneDecisionKind kind, string reason)
    {
        var gate = new DialogueFrameGate(ContinuousFrameGateTests.Settings);
        var selected = ContinuousFrameGateTests.Inspection();
        var missing = selected with { Signature = null, Selection = selected.Selection with
            { Profile = null, Decision = selected.Selection.Decision with { Kind = kind, Reason = reason } } };
        gate.Observe(ContinuousFrameGateTests.Frame(0, 0), selected);
        gate.Observe(ContinuousFrameGateTests.Frame(1, 100), missing);
        Assert.False(gate.Observe(ContinuousFrameGateTests.Frame(2, 800), missing).Leave);
        Assert.False(gate.Observe(new(3, TimeSpan.FromMilliseconds(900), new byte[] { 1 }, true), missing).Leave);
        Assert.False(gate.Observe(new(4, TimeSpan.FromMilliseconds(2501), new byte[] { 1 }, true), missing).Leave);
        Assert.False(gate.Observe(new(5, TimeSpan.FromMilliseconds(2801), new byte[] { 1 }, true), missing).Leave);
        Assert.Equal("ConfirmedMainUiExit", gate.Observe(new(6, TimeSpan.FromMilliseconds(3101), new byte[] { 1 }, true), missing).Reason);
        Assert.False(gate.Observe(new(7, TimeSpan.FromMilliseconds(3201), new byte[] { 1 }, true), missing).Leave);
    }

    /// <summary>持续不确定满30秒才执行有明确原因的超时，且只执行一次。</summary>
    [Fact]
    public void UncertainSceneGraceIsBoundedAndDoesNotClaimTrustedExit()
    {
        var gate = new DialogueFrameGate(ContinuousFrameGateTests.Settings);
        gate.Observe(ContinuousFrameGateTests.Frame(0, 0), ContinuousFrameGateTests.Inspection());
        for (var n = 1; n <= 30; n++)
            Assert.False(gate.Observe(ContinuousFrameGateTests.Frame(n, n * 1000), ContinuousFrameGateTests.Inspection(selected: false)).Leave);
        var expired = gate.Observe(ContinuousFrameGateTests.Frame(31, 31000), ContinuousFrameGateTests.Inspection(selected: false));
        Assert.True(expired.Leave); Assert.Equal("UncertainSceneTimeout", expired.Reason);
        Assert.False(gate.Observe(ContinuousFrameGateTests.Frame(32, 32000), ContinuousFrameGateTests.Inspection(selected: false)).Leave);
    }

    /// <summary>覆盖持续拒绝、断档且当前选中，以及断档且未选中的旧误停路径。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task UncertainObservationPreservesPlayingAudioAndEpoch(bool gap, bool selected)
    {
        await using var f = new ContinuousFrameFixture();
        await f.StartAsync();
        await f.Stable(0, 0);
        (await f.Ocr.NextAsync()).Complete("A");
        (await f.Voice.Engine.NextAsync()).Complete();
        var playing = await f.Voice.Player.NextAsync();
        if (gap) await f.Frame(3, 2100, selected ? 1 : 0);
        else { await f.Frame(3, 500, 0); await f.Frame(4, 1100, 0); }

        Assert.False(playing.Token.IsCancellationRequested);
        Assert.False(playing.Stop.Task.IsCompleted);
        Assert.Equal(0, f.Session.Snapshot.Epoch);
        Assert.DoesNotContain(f.Events, e => e.Kind == ContinuousFrameEventKind.LeftDialogue);
    }

    /// <summary>软失效后重读同文不能重播；有效新句只在完整就绪时接替。</summary>
    [Fact]
    public async Task SoftInvalidationKeepsIdentityAndWaitsForReplacementCompletion()
    {
        await using var f = new ContinuousFrameFixture();
        await f.StartAsync();
        await f.Stable(0, 0); (await f.Ocr.NextAsync()).Complete("A");
        (await f.Voice.Engine.NextAsync()).Complete();
        var a = await f.Voice.Player.NextAsync();
        await f.Frame(3, 2100);
        await f.Frame(4, 2300); await f.Frame(5, 2500);
        (await f.Ocr.NextAsync()).Complete("A");
        await f.EventAsync(ContinuousFrameEventKind.Discarded, 5);
        Assert.Single(f.Voice.Engine.All);
        Assert.False(a.Stop.Task.IsCompleted);

        await f.Stable(6, 2600, 2);
        (await f.Ocr.NextAsync()).Complete("B");
        var b = await f.Voice.Engine.NextAsync();
        Assert.False(a.Stop.Task.IsCompleted);
        b.Complete();
        await a.Stop.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(f.Voice.Player.All);
        a.Exit.TrySetResult(true);
        await f.Voice.Player.NextAsync();
        Assert.Contains(f.Voice.Events, e => e.Kind == ContinuousVoiceEventKind.StopRequested
            && e.Detail == "Replacement audio ready");
    }
}
