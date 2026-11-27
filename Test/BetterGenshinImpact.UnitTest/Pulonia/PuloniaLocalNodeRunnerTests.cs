using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>本地节点的 UID 核验边界，不接触真实游戏。</summary>
public sealed class PuloniaLocalNodeRunnerTests
{
    /// <summary>缺少核验能力时拒绝指定 UID 的游戏任务；其他任务保持原有行为。</summary>
    [Theory]
    [InlineData(true, "100000001", true)]
    [InlineData(true, null, false)]
    [InlineData(false, "100000001", false)]
    [InlineData(false, null, false)]
    public async Task MissingSwitcherNeverPassesUnverifiedUid(bool requiresGameSession, string? expectedUid, bool rejected)
    {
        var acquired = 0;
        var released = 0;
        var runner = new PuloniaLocalNodeRunner(new PuloniaGameTaskCoordinator(null!), null)
        {
            AcquireLeaseAsync = _ =>
            {
                acquired++;
                return Task.FromResult(new PuloniaGameTaskLease(null!, () => released++));
            }
        };
        var executor = new RecordingExecutor();
        var task = new PuloniaTaskPreparedTask("plan", "task", "task", "plan/task", "测试", "test", true,
            null, null, new JObject(), new Dictionary<string, string>(), new PuloniaTaskPolicy(), []);

        // 通过真实节点入口验证业务是否执行，以及异常路径是否释放游戏租约。
        Task Execute() => runner.ExecuteAsync(executor, task, "run", null!, 1, requiresGameSession,
            expectedUid, null, CancellationToken.None);
        if (rejected)
        {
            var error = await Assert.ThrowsAsync<PuloniaGenshinAccountException>(Execute);
            Assert.Contains("UID 核验能力", error.Message);
            Assert.Equal(0, executor.Calls);
        }
        else
        {
            await Execute();
            Assert.Equal(1, executor.Calls);
            Assert.Null(executor.Context!.VerifiedUid);
        }
        Assert.Equal(requiresGameSession ? 1 : 0, acquired);
        Assert.Equal(acquired, released);
    }

    /// <summary>记录收到的上下文，检测未经核验的身份是否进入业务。</summary>
    private sealed class RecordingExecutor : IPuloniaTaskExecutor
    {
        /// <summary>业务调用次数。</summary>
        public int Calls { get; private set; }
        /// <summary>最后一次业务上下文。</summary>
        public PuloniaTaskExecutionContext? Context { get; private set; }
        /// <inheritdoc />
        public IReadOnlyList<PuloniaTaskDefinition> Definitions => [];
        /// <inheritdoc />
        public Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task, PuloniaTaskExecutionContext context, CancellationToken ct)
        {
            Calls++;
            Context = context;
            return Task.FromResult(PuloniaTaskOutcome.Success("完成"));
        }
    }
}
