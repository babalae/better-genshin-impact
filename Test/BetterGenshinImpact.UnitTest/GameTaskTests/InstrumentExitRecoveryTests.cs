using BetterGenshinImpact.GameTask.Common.Job;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

public class InstrumentExitRecoveryTests
{
    [Theory]
    [InlineData("是否要退出演奏？")]
    [InlineData("是否要\n退出演奏?")]
    [InlineData("是否要退出演奏")]
    public void AcceptsTheCompleteExitQuestion(string text)
    {
        Assert.True(InstrumentExitRecovery.IsExitPrompt(text));
    }

    [Theory]
    [InlineData("是否要退出")]
    [InlineData("是否要退出演奏？网络连接已断开")]
    [InlineData("网络连接已断开\n是否要退出演奏？")]
    [InlineData("是否要退出游戏？")]
    public void RejectsOtherOrPartialQuestions(string text)
    {
        Assert.False(InstrumentExitRecovery.IsExitPrompt(text));
    }

    [Fact]
    public void SendsAtMostOneCloseAndOneConfirmEvenIfThePageReturns()
    {
        var flow = new InstrumentExitFlow();
        Assert.Equal(InstrumentExitAction.Close, flow.Observe(InstrumentExitScreen.InstrumentPage));
        Assert.Equal(InstrumentExitAction.Wait, flow.Observe(InstrumentExitScreen.InstrumentPage));
        Assert.Equal(InstrumentExitAction.Confirm, flow.Observe(InstrumentExitScreen.ExitPrompt));
        Assert.Equal(InstrumentExitAction.Wait, flow.Observe(InstrumentExitScreen.World));
        Assert.Equal(InstrumentExitAction.Wait, flow.Observe(InstrumentExitScreen.InstrumentPage));
        Assert.Equal(InstrumentExitAction.Wait, flow.Observe(InstrumentExitScreen.ExitPrompt));
        Assert.Equal(InstrumentExitAction.Wait, flow.Observe(InstrumentExitScreen.World));
        Assert.Equal(InstrumentExitAction.Complete, flow.Observe(InstrumentExitScreen.World));
    }

    [Fact]
    public void UnknownDialogWaitsAndEachInterruptionRestartsTheWorldCheck()
    {
        var flow = new InstrumentExitFlow();
        Assert.Equal(InstrumentExitAction.Close, flow.Observe(InstrumentExitScreen.InstrumentPage));
        Assert.Equal(InstrumentExitAction.Wait, flow.Observe(InstrumentExitScreen.World));
        Assert.Equal(InstrumentExitAction.Wait, flow.Observe(InstrumentExitScreen.BlockedPrompt));
        Assert.Equal(InstrumentExitAction.Wait, flow.Observe(InstrumentExitScreen.World));
        Assert.Equal(InstrumentExitAction.Wait, flow.Observe(InstrumentExitScreen.World, sameWorldFrame: true));
        Assert.Equal(InstrumentExitAction.Complete, flow.Observe(InstrumentExitScreen.World));
    }

    [Fact]
    public void AWorldFrameBeforeTheFirstActionDoesNotBlockTheExit()
    {
        var flow = new InstrumentExitFlow();
        Assert.Equal(InstrumentExitAction.Wait, flow.Observe(InstrumentExitScreen.World, worldReady: false));
        Assert.Equal(InstrumentExitAction.Close, flow.Observe(InstrumentExitScreen.InstrumentPage));
        Assert.Equal(InstrumentExitAction.Confirm, flow.Observe(InstrumentExitScreen.ExitPrompt));
    }

    [Fact]
    public void ADirectExitPromptNeverSendsCloseAndWaitsForTheOriginalDestination()
    {
        var flow = new InstrumentExitFlow();
        Assert.Equal(InstrumentExitAction.Confirm, flow.Observe(InstrumentExitScreen.ExitPrompt));
        Assert.Equal(InstrumentExitAction.Wait, flow.Observe(InstrumentExitScreen.InstrumentPage));
        Assert.Equal(InstrumentExitAction.Wait, flow.Observe(InstrumentExitScreen.World, worldReady: false));
        Assert.Equal(InstrumentExitAction.Wait, flow.Observe(InstrumentExitScreen.World));
        Assert.Equal(InstrumentExitAction.Complete, flow.Observe(InstrumentExitScreen.World));
    }
}
