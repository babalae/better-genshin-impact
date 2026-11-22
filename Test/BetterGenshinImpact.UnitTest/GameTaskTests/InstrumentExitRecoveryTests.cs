using BetterGenshinImpact.GameTask.Common.Job;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

public class InstrumentExitRecoveryTests
{
    [Theory]
    [InlineData("是否要退出演奏？")]
    [InlineData("是否要\n退出演奏?")]
    [InlineData("是否要退出演奏")]
    public void AcceptsCompleteExitQuestion(string text)
    {
        Assert.True(InstrumentExitRecovery.IsExitPrompt(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("是否要退出")]
    [InlineData("是否要退出演奏？网络连接已断开")]
    [InlineData("网络连接已断开\n是否要退出演奏？")]
    [InlineData("是否要退出演奏并返回登录界面？")]
    [InlineData("是否要退出游戏？")]
    public void RejectsPartialOrDifferentPrompt(string text)
    {
        Assert.False(InstrumentExitRecovery.IsExitPrompt(text));
    }

    [Fact]
    public void DelayedPopupAndImmediateReentryDoNotSendExtraClicks()
    {
        var exit = new InstrumentExitFlow();

        Assert.Equal(InstrumentExitAction.Close, exit.Observe(InstrumentExitScreen.InstrumentPage));
        Assert.Equal(InstrumentExitAction.Wait, exit.Observe(InstrumentExitScreen.InstrumentPage));
        Assert.Equal(InstrumentExitAction.Confirm, exit.Observe(InstrumentExitScreen.ExitPrompt));
        Assert.Equal(InstrumentExitAction.Wait, exit.Observe(InstrumentExitScreen.ExitPrompt));
        Assert.Equal(InstrumentExitAction.Wait, exit.Observe(InstrumentExitScreen.World));
        Assert.Equal(InstrumentExitAction.Stop, exit.Observe(InstrumentExitScreen.InstrumentPage));
        Assert.Equal(InstrumentExitAction.Stop, exit.Observe(InstrumentExitScreen.ExitPrompt));
    }

    [Fact]
    public void FreshFlow_UnknownFrame_IsNotApplicableAndKeepsTheFlowFresh()
    {
        var flow = new InstrumentExitFlow();

        Assert.Equal(
            [InstrumentExitAction.NotApplicable, InstrumentExitAction.NotApplicable],
            ObserveAll(flow, InstrumentExitScreen.Other, InstrumentExitScreen.Other));
        Assert.Equal(InstrumentExitAction.Close, flow.Observe(InstrumentExitScreen.InstrumentPage));
    }

    [Fact]
    public void DirectPrompt_ConfirmsThenCompletesOnlyAfterTwoWorldFrames()
    {
        var flow = new InstrumentExitFlow();

        Assert.Equal(
            [InstrumentExitAction.Confirm, InstrumentExitAction.Wait, InstrumentExitAction.Complete],
            ObserveAll(flow, InstrumentExitScreen.ExitPrompt, InstrumentExitScreen.World, InstrumentExitScreen.World));
    }

    [Fact]
    public void CloseWithoutPrompt_CompletesOnlyAfterTwoConsecutiveWorldFrames()
    {
        var flow = new InstrumentExitFlow();

        Assert.Equal(
            [InstrumentExitAction.Close, InstrumentExitAction.Wait, InstrumentExitAction.Complete],
            ObserveAll(flow, InstrumentExitScreen.InstrumentPage, InstrumentExitScreen.World, InstrumentExitScreen.World));
    }

    [Fact]
    public void UnknownFrameAfterClose_WaitsAndDoesNotConsumeTheExactPrompt()
    {
        var flow = new InstrumentExitFlow();

        Assert.Equal(
            [
                InstrumentExitAction.Close,
                InstrumentExitAction.Wait,
                InstrumentExitAction.Wait,
                InstrumentExitAction.Confirm,
                InstrumentExitAction.Wait,
                InstrumentExitAction.Complete
            ],
            ObserveAll(flow,
                InstrumentExitScreen.InstrumentPage,
                InstrumentExitScreen.Other,
                InstrumentExitScreen.Other,
                InstrumentExitScreen.ExitPrompt,
                InstrumentExitScreen.World,
                InstrumentExitScreen.World));
    }

    [Fact]
    public void InstrumentPageReturningAfterFirstWorldAndUnknownFrame_StillStops()
    {
        var flow = new InstrumentExitFlow();

        Assert.Equal(
            [
                InstrumentExitAction.Close,
                InstrumentExitAction.Confirm,
                InstrumentExitAction.Wait,
                InstrumentExitAction.Wait,
                InstrumentExitAction.Stop
            ],
            ObserveAll(flow,
                InstrumentExitScreen.InstrumentPage,
                InstrumentExitScreen.ExitPrompt,
                InstrumentExitScreen.World,
                InstrumentExitScreen.Other,
                InstrumentExitScreen.InstrumentPage));
    }

    [Fact]
    public void IndependentFlows_DoNotShareProgress()
    {
        var first = new InstrumentExitFlow();
        var second = new InstrumentExitFlow();

        Assert.Equal(InstrumentExitAction.Close, first.Observe(InstrumentExitScreen.InstrumentPage));
        Assert.Equal(InstrumentExitAction.Close, second.Observe(InstrumentExitScreen.InstrumentPage));

        Assert.Equal(InstrumentExitAction.Confirm, first.Observe(InstrumentExitScreen.ExitPrompt));
        Assert.Equal(InstrumentExitAction.Confirm, second.Observe(InstrumentExitScreen.ExitPrompt));

        Assert.Equal(InstrumentExitAction.Wait, first.Observe(InstrumentExitScreen.World));
        Assert.Equal(InstrumentExitAction.Complete, first.Observe(InstrumentExitScreen.World));

        Assert.Equal(InstrumentExitAction.Wait, second.Observe(InstrumentExitScreen.World));
        Assert.Equal(InstrumentExitAction.Complete, second.Observe(InstrumentExitScreen.World));

        var third = new InstrumentExitFlow();
        Assert.Equal(InstrumentExitAction.NotApplicable, third.Observe(InstrumentExitScreen.Other));
    }

    [Fact]
    public void UnfamiliarDialogRemainsBlockedWhenTheExactDialogAppearsLater()
    {
        var flow = new InstrumentExitFlow();

        Assert.Equal(
            [InstrumentExitAction.Close, InstrumentExitAction.Stop, InstrumentExitAction.Stop],
            ObserveAll(flow, InstrumentExitScreen.InstrumentPage, InstrumentExitScreen.BlockedPrompt, InstrumentExitScreen.ExitPrompt));
    }

    [Fact]
    public void CachedWorldFrameDoesNotCountAsSecondObservation()
    {
        var flow = new InstrumentExitFlow();

        Assert.Equal(InstrumentExitAction.Close, flow.Observe(InstrumentExitScreen.InstrumentPage));
        Assert.Equal(InstrumentExitAction.Wait, flow.Observe(InstrumentExitScreen.World));
        Assert.Equal(InstrumentExitAction.Wait, flow.Observe(InstrumentExitScreen.World, sameWorldFrame: true));
        Assert.Equal(InstrumentExitAction.Wait, flow.Observe(InstrumentExitScreen.World, sameWorldFrame: true));
        Assert.Equal(InstrumentExitAction.Complete, flow.Observe(InstrumentExitScreen.World));
    }

    private static InstrumentExitAction[] ObserveAll(InstrumentExitFlow flow, params InstrumentExitScreen[] screens)
    {
        return screens.Select(screen => flow.Observe(screen)).ToArray();
    }
}
