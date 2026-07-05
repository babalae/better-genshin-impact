using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoDomainTests;

public class AutoDomainReviveRetryTests
{
    [Fact]
    public void IsDomainReviveRetry_ShouldOnlyMatchDomainRevivePrompt()
    {
        Assert.True(AutoDomainTask.IsDomainReviveRetry(new RetryException("检测到秘境内复苏界面，存在角色被击败，退出秘境后重试")));
        Assert.False(AutoDomainTask.IsDomainReviveRetry(new RetryException("检测到复苏界面，存在角色被击败，前往七天神像复活")));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void HasExitedDomainReviveState_ShouldDependOnCurrentDomainState(bool isInDomainIncludingRevivePrompt, bool expected)
    {
        Assert.Equal(expected, AutoDomainTask.HasExitedDomainReviveState(isInDomainIncludingRevivePrompt));
    }

    [Fact]
    public async Task TryRecoverAfterDomainReviveRetry_ShouldRecoverWhenExitSucceeds()
    {
        var exitCalls = 0;
        var recoverCalls = 0;

        var recovered = await AutoDomainTask.TryRecoverAfterDomainReviveRetry(
            CancellationToken.None,
            () =>
            {
                exitCalls++;
                return Task.FromResult(true);
            },
            _ =>
            {
                recoverCalls++;
                return Task.CompletedTask;
            });

        Assert.True(recovered);
        Assert.Equal(1, exitCalls);
        Assert.Equal(1, recoverCalls);
    }

    [Fact]
    public async Task TryRecoverAfterDomainReviveRetry_ShouldNotRecoverWhenExitFails()
    {
        var recoverCalls = 0;

        var recovered = await AutoDomainTask.TryRecoverAfterDomainReviveRetry(
            CancellationToken.None,
            () => Task.FromResult(false),
            _ =>
            {
                recoverCalls++;
                return Task.CompletedTask;
            });

        Assert.False(recovered);
        Assert.Equal(0, recoverCalls);
    }

    [Fact]
    public async Task TryRecoverAfterDomainReviveRetry_ShouldPropagateRecoverFailure()
    {
        var expected = new InvalidOperationException("recover failed");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AutoDomainTask.TryRecoverAfterDomainReviveRetry(
                CancellationToken.None,
                () => Task.FromResult(true),
                _ => throw expected));

        Assert.Same(expected, actual);
    }
}
