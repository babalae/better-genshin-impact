using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 运行开始时确定运行账户：跟随当前账户的 UID 比对与“未确定”，指定账户切换后改写实例当前账户。
/// 用假的切号器和不触碰游戏的租约，只验证任务服务的账户流程。
/// </summary>
public sealed class PuloniaRunAccountTests : IDisposable
{
    /// <summary>本测试独占的目录。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>当前账户没有 UID 时不读取 UID，记为未比对，运行账户为实例当前账户。</summary>
    [Fact] public async Task FollowCurrentWithoutUidSkipsComparison()
    {
        using var context = await TestContext.CreateAsync(_directory);
        var run = await context.RunAsync(await context.SavePlanAsync());
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, run.Status);
        Assert.Equal(context.MyAccount.Id, run.AccountId);
        Assert.Equal(PuloniaTaskAccountSource.InstanceCurrent, run.AccountSource);
        Assert.Equal(PuloniaTaskUidCheck.Skipped, run.UidCheck);
        Assert.Null(run.VerifiedUid); Assert.Null(run.ObservedUid);
        Assert.Equal(0, context.Switcher.ReadCalls);
        // 执行器拿到的运行账户与运行记录一致（首领领奖次数按它计）
        Assert.Equal(context.MyAccount.Id, Assert.Single(context.Executor.AccountIds));
    }

    /// <summary>当前账户有 UID 且游戏中读到相同 UID 时记为一致，节点执行时不再核验 UID。</summary>
    [Fact] public async Task FollowCurrentWithMatchingUid()
    {
        using var context = await TestContext.CreateAsync(_directory, myUid: "100000001");
        context.Switcher.GameUid = "100000001";
        var run = await context.RunAsync(await context.SavePlanAsync());
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, run.Status);
        Assert.Equal(PuloniaTaskUidCheck.Matched, run.UidCheck);
        Assert.Null(run.ObservedUid);
        Assert.Equal(1, context.Switcher.ReadCalls);
        Assert.Equal(0, context.Switcher.VerifyCalls);
    }

    /// <summary>UID 不一致只记录警告和读到的 UID，运行照常进行，进度仍记在实例当前账户名下。</summary>
    [Fact] public async Task FollowCurrentWithMismatchedUidStillRuns()
    {
        using var context = await TestContext.CreateAsync(_directory, myUid: "100000001");
        context.Switcher.GameUid = "100000002";
        var run = await context.RunAsync(await context.SavePlanAsync());
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, run.Status);
        Assert.Equal(PuloniaTaskUidCheck.Mismatched, run.UidCheck);
        Assert.Equal("100000002", run.ObservedUid);
        Assert.Equal(context.MyAccount.Id, run.AccountId);
        Assert.Equal(1, context.Executor.Executions);
        // 读不到 UID（不在主界面）时记为未比对
        context.Switcher.GameUid = null;
        var unread = await context.RunAsync(await context.SavePlanAsync());
        Assert.Equal(PuloniaTaskUidCheck.Skipped, unread.UidCheck);
    }

    /// <summary>
    /// 本地运行的 UID 比对结果报告给账户服务（只在内存中）：不一致会保留到比对一致或修改本实例当前账户，
    /// 未比对不解除不一致。
    /// </summary>
    [Fact] public async Task UidCheckResultsAreReportedToAccountService()
    {
        using var context = await TestContext.CreateAsync(_directory, myUid: "100000001");
        var changes = 0;
        context.Accounts.UidCheckChanged += (_, _) => changes++;
        var plan = await context.SavePlanAsync();
        // 1. 不一致：记下读到的 UID
        context.Switcher.GameUid = "100000002";
        await context.RunAsync(plan);
        Assert.Equal(PuloniaTaskUidCheck.Mismatched, context.Accounts.LastUidCheck?.Result);
        Assert.Equal("100000002", context.Accounts.UidMismatch?.ObservedUid);
        Assert.Equal(context.MyAccount.Id, context.Accounts.UidMismatch?.AccountId);
        // 2. 读不到 UID：记为未比对，不一致仍保留
        context.Switcher.GameUid = null;
        await context.RunAsync(plan);
        Assert.Equal(PuloniaTaskUidCheck.Skipped, context.Accounts.LastUidCheck?.Result);
        Assert.NotNull(context.Accounts.UidMismatch);
        // 3. 比对一致：解除不一致
        context.Switcher.GameUid = "100000001";
        await context.RunAsync(plan);
        Assert.Equal(PuloniaTaskUidCheck.Matched, context.Accounts.LastUidCheck?.Result);
        Assert.Null(context.Accounts.UidMismatch);
        // 4. 再次不一致后修改本实例当前账户：结果全部清除；修改其他实例的当前账户不影响本实例
        context.Switcher.GameUid = "100000002";
        await context.RunAsync(plan);
        Assert.NotNull(context.Accounts.UidMismatch);
        await context.Accounts.SetCurrentAccountAsync(PuloniaInstanceKey.ChildSession, context.MyAccount.Id);
        Assert.NotNull(context.Accounts.UidMismatch);
        await context.Accounts.SetCurrentAccountAsync(PuloniaInstanceKey.Primary, context.MyAccount.Id);
        Assert.Null(context.Accounts.UidMismatch);
        Assert.Null(context.Accounts.LastUidCheck);
        Assert.Equal(5, changes);
    }

    /// <summary>本实例当前账户“未确定”时，跟随当前账户的运行进入待处理，不执行任何节点。</summary>
    [Fact] public async Task FollowCurrentWithUndeterminedAccountNeedsAttention()
    {
        using var context = await TestContext.CreateAsync(_directory);
        await context.Accounts.SetCurrentAccountAsync(PuloniaInstanceKey.Primary, null);
        var run = await context.RunAsync(await context.SavePlanAsync());
        Assert.Equal(PuloniaTaskRunStatus.NeedsAttention, run.Status);
        Assert.Equal("本实例当前账户未确定，请先在标题栏或账户页设置。", run.Message);
        Assert.Equal(0, context.Executor.Executions);
        Assert.Null(run.AccountId);
    }

    /// <summary>指定账户：切换并核验成功后把本实例当前账户改为目标账户，运行记录写入核验 UID。</summary>
    [Fact] public async Task SpecifiedAccountUpdatesInstanceCurrentAccount()
    {
        using var context = await TestContext.CreateAsync(_directory);
        var target = await context.AddAccountAsync("小号", "100000002");
        context.Switcher.GameUid = "100000009";
        var run = await context.RunAsync(await context.SavePlanAsync(target.Id));
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, run.Status);
        Assert.Equal(target.Id, run.AccountId);
        Assert.Equal(PuloniaTaskAccountSource.Specified, run.AccountSource);
        Assert.Equal("100000002", run.VerifiedUid);
        Assert.Null(run.UidCheck);
        Assert.Contains(run.NodeResults, result => result.TaskType == "genshin.account.prepare");
        Assert.Equal(target.Id, await context.Accounts.GetCurrentAccountIdAsync(PuloniaInstanceKey.Primary));
        // 节点级 UID 核验保持现有行为
        Assert.Equal(1, context.Switcher.VerifyCalls);
    }

    /// <summary>指定账户：切换已经开始后失败，运行进入待处理，本实例当前账户改为“未确定”。</summary>
    [Fact] public async Task SpecifiedAccountFailureAfterSwitchStartedClearsCurrentAccount()
    {
        using var context = await TestContext.CreateAsync(_directory);
        var target = await context.AddAccountAsync("小号", "100000002");
        context.Switcher.GameUid = "100000009";
        context.Switcher.FailAfterSwitchStarted = true;
        var run = await context.RunAsync(await context.SavePlanAsync(target.Id));
        Assert.Equal(PuloniaTaskRunStatus.NeedsAttention, run.Status);
        Assert.Contains("验证码", run.Message);
        Assert.Equal(0, context.Executor.Executions);
        Assert.Null(await context.Accounts.GetCurrentAccountIdAsync(PuloniaInstanceKey.Primary));
    }

    /// <summary>指定账户：还没开始切换就失败（例如没有登录资料）时不改动本实例当前账户。</summary>
    [Fact] public async Task SpecifiedAccountFailureBeforeSwitchKeepsCurrentAccount()
    {
        using var context = await TestContext.CreateAsync(_directory);
        var target = await context.AddAccountAsync("小号", "100000002");
        context.Switcher.GameUid = "100000009";
        context.Switcher.FailBeforeSwitch = true;
        var run = await context.RunAsync(await context.SavePlanAsync(target.Id));
        Assert.Equal(PuloniaTaskRunStatus.NeedsAttention, run.Status);
        Assert.Equal(context.MyAccount.Id, await context.Accounts.GetCurrentAccountIdAsync(PuloniaInstanceKey.Primary));
    }

    /// <summary>续跑跟随当前账户的运行时，本实例当前账户必须与原运行账户相同，否则进入待处理。</summary>
    [Fact] public async Task ResumeRequiresSameInstanceCurrentAccount()
    {
        using var context = await TestContext.CreateAsync(_directory);
        var other = await context.AddAccountAsync("小号", "100000002");
        context.Executor.FailNext = true;
        var plan = await context.SavePlanAsync();
        var failed = await context.RunAsync(plan);
        Assert.Equal(PuloniaTaskRunStatus.Failed, failed.Status);
        Assert.Equal(context.MyAccount.Id, failed.AccountId);

        await context.Accounts.SetCurrentAccountAsync(PuloniaInstanceKey.Primary, other.Id);
        var mismatched = await context.ResumeAsync(failed.RunId);
        Assert.Equal(PuloniaTaskRunStatus.NeedsAttention, mismatched.Status);
        Assert.Contains("续跑", mismatched.Message);

        await context.Accounts.SetCurrentAccountAsync(PuloniaInstanceKey.Primary, context.MyAccount.Id);
        var resumed = await context.ResumeAsync(failed.RunId);
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, resumed.Status);
        Assert.Equal(context.MyAccount.Id, resumed.AccountId);
    }

    /// <summary>仅清理本测试创建的独占目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    /// <summary>一组测试共用的存储、账户服务、假切号器、游戏执行器与任务服务。</summary>
    private sealed class TestContext : IDisposable
    {
        /// <summary>临时目录中的 Pulonia 存储。</summary>
        public PuloniaTaskStore Store { get; }
        /// <summary>主实例的账户服务。</summary>
        public PuloniaAccountService Accounts { get; }
        /// <summary>假的切号器。</summary>
        public FakeSwitcher Switcher { get; } = new();
        /// <summary>需要游戏会话的假执行器。</summary>
        public GameExecutor Executor { get; } = new();
        /// <summary>任务服务。</summary>
        public PuloniaTaskService Service { get; }
        /// <summary>账户目录初始自带的“我的账户”。</summary>
        public PuloniaGenshinAccountProfile MyAccount { get; private set; } = null!;

        /// <summary>建立测试环境；租约替换为不触碰游戏的空租约。</summary>
        private TestContext(string directory)
        {
            Store = new PuloniaTaskStore(directory);
            Accounts = new PuloniaAccountService(Store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
            Service = new PuloniaTaskService(Store, new PuloniaTaskBuilder(Store), [Executor], new PuloniaGameTaskCoordinator(null!),
                new TaskStopService(NullLogger<TaskStopService>.Instance), accountSwitcher: Switcher, accountService: Accounts)
            {
                GameLeaseFactory = _ => Task.FromResult(new PuloniaGameTaskLease(null!, () => { }))
            };
        }

        /// <summary>建立环境并可选地给“我的账户”填写 UID。</summary>
        public static async Task<TestContext> CreateAsync(string directory, string? myUid = null)
        {
            var context = new TestContext(directory);
            var settings = await context.Accounts.LoadAccountsAsync();
            if (myUid is not null)
            {
                settings.Accounts[0].Uid = myUid;
                settings = (await context.Accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>())).Settings;
            }
            context.MyAccount = settings.Accounts[0];
            return context;
        }

        /// <summary>新增一个带 UID 的账户。</summary>
        public async Task<PuloniaGenshinAccountProfile> AddAccountAsync(string name, string uid)
        {
            var settings = await Accounts.LoadAccountsAsync();
            var account = new PuloniaGenshinAccountProfile { Name = name, Uid = uid, ListText = "123****45" };
            settings.Accounts.Add(account);
            await Accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>());
            return account;
        }

        /// <summary>保存一份只含一个游戏节点的计划。</summary>
        public Task<PuloniaTaskPlan> SavePlanAsync(string? accountId = null) => Store.SavePlanAsync(new PuloniaTaskPlan
        {
            Name = "账户测试", AccountId = accountId,
            RootTask = new PuloniaTask { Children = [new PuloniaTask { Name = "游戏任务", TaskType = GameExecutor.TaskType }] }
        });

        /// <summary>提交并等待一次运行结束。</summary>
        public async Task<PuloniaTaskRunView> RunAsync(PuloniaTaskPlan plan)
        {
            var id = await Service.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
            return await Service.WaitForCompletionAsync(id).WaitAsync(TimeSpan.FromSeconds(20));
        }

        /// <summary>续跑并等待结束。</summary>
        public async Task<PuloniaTaskRunView> ResumeAsync(string runId)
        {
            var id = await Service.ResumeAsync(runId, allowUncertainReplay: true);
            return await Service.WaitForCompletionAsync(id).WaitAsync(TimeSpan.FromSeconds(20));
        }

        /// <summary>同步释放任务服务与存储。</summary>
        public void Dispose()
        {
            Service.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Store.Dispose();
        }
    }

    /// <summary>假的切号器：游戏中的 UID 由测试设置，可模拟切换前或切换开始后失败。</summary>
    private sealed class FakeSwitcher : IPuloniaGenshinAccountSwitcher
    {
        /// <summary>游戏当前显示的 UID；为空表示不在主界面、读不到。</summary>
        public string? GameUid { get; set; }
        /// <summary>开始切换后抛出验证码错误。</summary>
        public bool FailAfterSwitchStarted { get; set; }
        /// <summary>开始切换前抛出错误。</summary>
        public bool FailBeforeSwitch { get; set; }
        /// <summary>只读 UID 的次数。</summary>
        public int ReadCalls { get; private set; }
        /// <summary>节点核验的次数。</summary>
        public int VerifyCalls { get; private set; }

        /// <inheritdoc />
        public Task<string> PrepareAsync(GameRuntime runtime, PuloniaGenshinAccountProfile account, Action switchStarted, CancellationToken ct)
        {
            if (GameUid == account.Uid) return Task.FromResult(account.Uid!);
            if (FailBeforeSwitch) throw new InvalidOperationException("目标账户尚未配置登录资料，未登出当前账户。");
            switchStarted();
            if (FailAfterSwitchStarted) throw new InvalidOperationException("登录需要验证码，请人工处理后重新运行。");
            GameUid = account.Uid;
            return Task.FromResult(account.Uid!);
        }

        /// <inheritdoc />
        public Task VerifyAsync(GameRuntime runtime, string expectedUid, CancellationToken ct)
        {
            VerifyCalls++;
            return GameUid == expectedUid ? Task.CompletedTask : throw new InvalidOperationException("UID 不一致。");
        }

        /// <inheritdoc />
        public Task<string?> TryReadUidAsync(GameRuntime runtime, CancellationToken ct)
        {
            ReadCalls++;
            return Task.FromResult(GameUid);
        }

        /// <inheritdoc />
        public Task<(string Uid, string ImagePath)> CaptureCurrentAccountAsync(GameRuntime runtime, string accountId, CancellationToken ct)
            => throw new InvalidOperationException("测试不采集图片。");
    }

    /// <summary>需要游戏会话的假执行器，记录收到的运行账户。</summary>
    private sealed class GameExecutor : IPuloniaTaskExecutor
    {
        /// <summary>任务类型。</summary>
        public const string TaskType = "test.game";
        /// <summary>执行次数。</summary>
        public int Executions { get; private set; }
        /// <summary>下一次执行返回失败。</summary>
        public bool FailNext { get; set; }
        /// <summary>每次执行收到的运行账户 ID。</summary>
        public List<string?> AccountIds { get; } = [];
        /// <inheritdoc />
        public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; } = [new()
        {
            TaskType = TaskType, RequiresGameSession = true,
            ParameterSchema = new JObject { ["type"] = "object", ["additionalProperties"] = true }
        }];
        /// <inheritdoc />
        public Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task, PuloniaTaskExecutionContext context, CancellationToken ct)
        {
            Executions++;
            AccountIds.Add(context.AccountId);
            if (!FailNext) return Task.FromResult(PuloniaTaskOutcome.Success("完成"));
            FailNext = false;
            return Task.FromResult(PuloniaTaskOutcome.Failure("模拟失败"));
        }
    }
}
