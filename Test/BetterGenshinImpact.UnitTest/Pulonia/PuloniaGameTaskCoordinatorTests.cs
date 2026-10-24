using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.Pulonia.Services;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 游戏任务租约的嵌套检测，不启动游戏、不占用全局任务锁。
/// </summary>
public sealed class PuloniaGameTaskCoordinatorTests
{
    /// <summary>
    /// 已在任务会话内（例如节点执行器或 TaskRunner 任务内部）再次获取租约时立即报错，而不是永久等待不可重入的任务锁。
    /// </summary>
    [Fact]
    public async Task AcquireAsync_InsideGameTaskSession_ThrowsImmediately()
    {
        var coordinator = new PuloniaGameTaskCoordinator(null!);
        using (GameTaskSessionScope.Enter())
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => coordinator.AcquireAsync(CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("当前已在任务会话中", error.Message);
        }
        Assert.False(GameTaskSessionScope.IsActive);
    }
}
