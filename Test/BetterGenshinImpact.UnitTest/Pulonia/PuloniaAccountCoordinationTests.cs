using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 跨实例的账户协调（统筹设计 4.5、4.6）：账户占用与在线标记、占用阻止删除账户、同一账户不能同时在两个实例运行、
/// 切换前的挤下线检查，以及“运行于”提交前检查的规则。每个实例各用一个指向同一目录的存储对象，模拟不同进程；
/// 游戏节点用假的切号器和不触碰游戏的租约。
/// </summary>
public sealed class PuloniaAccountCoordinationTests : IDisposable
{
    /// <summary>云原神实例 a 的实例键。</summary>
    private const string KeyA = "webview:a";

    /// <summary>云原神实例 b 的实例键。</summary>
    private const string KeyB = "webview:b";

    /// <summary>等待运行结束或条件成立的上限。</summary>
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(20);

    /// <summary>本测试独占的目录，不接触用户真实数据。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>账户占用同一时刻只有一个持有者：其他实例取不到并读到持有者，释放后可以重新取得。</summary>
    [Fact] public async Task AccountOccupancyIsExclusiveUntilReleased()
    {
        using var primaryStore = new PuloniaTaskStore(_directory);
        using var otherStore = new PuloniaTaskStore(_directory);
        var account = (await primaryStore.LoadAccountSettingsAsync()).Accounts[0];
        Assert.Null(primaryStore.GetAccountOccupant(account.Id));

        // 1. 云原神 a 的运行取得占用
        var runId = PuloniaId.NewRunId(DateTimeOffset.Now);
        var held = otherStore.TryAcquireAccountOccupancy(account.Id, Owner(KeyA, runId), out var none);
        Assert.NotNull(held);
        Assert.Null(none);

        // 2. 主实例取不到，并读到持有者
        Assert.Null(primaryStore.TryAcquireAccountOccupancy(account.Id, Owner(PuloniaInstanceKey.Primary, null), out var occupant));
        Assert.Equal((KeyA, runId), (occupant?.InstanceKey, occupant?.RunId));
        Assert.Equal(KeyA, primaryStore.GetAccountOccupant(account.Id)?.InstanceKey);
        Assert.Equal($"云原神 · a（运行 {runId}）", PuloniaAccountService.DescribeOccupant(occupant!));

        // 3. 释放后文件仍在，但不再算占用，主实例可以取得
        held!.Dispose();
        Assert.Null(primaryStore.GetAccountOccupant(account.Id));
        using var next = primaryStore.TryAcquireAccountOccupancy(account.Id, Owner(PuloniaInstanceKey.Primary, null), out _);
        Assert.NotNull(next);
    }

    /// <summary>在线标记在句柄持有期间有效；同一实例键已有进程在线时，第二个进程取不到。</summary>
    [Fact] public void InstancePresenceIsHeldUntilDisposed()
    {
        using var primaryStore = new PuloniaTaskStore(_directory);
        using var otherStore = new PuloniaTaskStore(_directory);
        Assert.False(primaryStore.IsInstanceOnline(KeyA));

        var presence = otherStore.TryAcquireInstancePresence(Owner(KeyA, null));
        Assert.NotNull(presence);
        Assert.True(primaryStore.IsInstanceOnline(KeyA));
        Assert.Null(primaryStore.TryAcquireInstancePresence(Owner(KeyA, null)));

        presence!.Dispose();
        Assert.False(primaryStore.IsInstanceOnline(KeyA));
    }

    /// <summary>切换前检查列出的实例：当前账户是该账户、正在线的其他实例；本实例和不在线的实例不列出。本实例总视为在线。</summary>
    [Fact] public async Task OnlineInstancesUsingAccountExcludeSelfAndOffline()
    {
        using var primaryStore = new PuloniaTaskStore(_directory);
        using var otherStore = new PuloniaTaskStore(_directory);
        var accounts = Accounts(primaryStore, PuloniaInstanceKey.Primary);
        var my = (await accounts.LoadAccountsAsync()).Accounts[0];
        var alt = await AddAccountAsync(accounts, "乙", "100000002");
        foreach (var key in new[] { PuloniaInstanceKey.Primary, PuloniaInstanceKey.ChildSession, KeyA })
            await accounts.SetCurrentAccountAsync(key, alt.Id);
        await accounts.SetCurrentAccountAsync(KeyB, my.Id);
        // 云原神 a、b 在线，桌面分身不在线
        using var presenceA = otherStore.TryAcquireInstancePresence(Owner(KeyA, null));
        using var presenceB = otherStore.TryAcquireInstancePresence(Owner(KeyB, null));

        Assert.Equal([KeyA], await accounts.ListOtherOnlineInstancesUsingAccountAsync(alt.Id));
        Assert.Equal([KeyB], await accounts.ListOtherOnlineInstancesUsingAccountAsync(my.Id));
        Assert.True(accounts.IsInstanceOnline(PuloniaInstanceKey.Primary));
        Assert.True(accounts.IsInstanceOnline(KeyA));
        Assert.False(accounts.IsInstanceOnline(PuloniaInstanceKey.ChildSession));
    }

    /// <summary>账户正被运行占用时拒绝删除，目录不保存；占用释放后可以删除。</summary>
    [Fact] public async Task OccupiedAccountCannotBeRemoved()
    {
        using var primaryStore = new PuloniaTaskStore(_directory);
        using var otherStore = new PuloniaTaskStore(_directory);
        var accounts = Accounts(primaryStore, PuloniaInstanceKey.Primary);
        var alt = await AddAccountAsync(accounts, "乙", "100000002");
        var held = otherStore.TryAcquireAccountOccupancy(alt.Id, Owner(KeyA, PuloniaId.NewRunId(DateTimeOffset.Now)), out _);
        Assert.NotNull(held);

        // 1. 占用期间：删除前检查与保存都拒绝
        Assert.Contains("正在云原神 · a", await accounts.CheckAccountRemovalAsync(alt.Id));
        var settings = await accounts.LoadAccountsAsync();
        settings.Accounts.RemoveAll(account => account.Id == alt.Id);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>()));
        Assert.Contains("请等它结束后再删除", error.Message);
        Assert.Equal(2, (await accounts.LoadAccountsAsync()).Accounts.Count);

        // 2. 释放后可以删除
        held!.Dispose();
        Assert.Null(await accounts.CheckAccountRemovalAsync(alt.Id));
        await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>());
        Assert.Single((await accounts.LoadAccountsAsync()).Accounts);
    }

    /// <summary>账户页概况列出指定了该账户的计划：普通计划在前、一条龙在后，同类按名称排序；跟随当前账户的计划不列出。</summary>
    [Fact] public async Task PlansSpecifyingAccountAreListedByPurposeAndName()
    {
        using var store = new PuloniaTaskStore(_directory);
        var accounts = Accounts(store, PuloniaInstanceKey.Primary);
        var my = (await accounts.LoadAccountsAsync()).Accounts[0];
        var alt = await AddAccountAsync(accounts, "乙", "100000002");
        var dragon = await store.SavePlanAsync(new PuloniaTaskPlan
            { Name = "乙的一条龙", Purpose = PuloniaTaskPlanPurpose.OneDragon, AccountId = alt.Id });
        var second = await store.SavePlanAsync(new PuloniaTaskPlan { Name = "乙计划B", AccountId = alt.Id });
        var first = await store.SavePlanAsync(new PuloniaTaskPlan { Name = "乙计划A", AccountId = alt.Id });
        await store.SavePlanAsync(new PuloniaTaskPlan { Name = "跟随当前账户" });

        var usages = await accounts.ListPlansSpecifyingAccountAsync(alt.Id);

        Assert.Equal([first.Id, second.Id, dragon.Id], usages.Select(usage => usage.PlanId));
        Assert.Equal(("乙的一条龙", PuloniaTaskPlanPurpose.OneDragon), (usages[2].PlanName, usages[2].Purpose));
        Assert.Empty(await accounts.ListPlansSpecifyingAccountAsync(my.Id));
    }

    /// <summary>
    /// 两个实例的当前账户相同：先开始的运行持有账户占用，另一个实例的运行不等待、不排队，直接进入待处理且不执行任何节点；
    /// 先开始的运行结束后，另一个实例可以正常运行（A7）。
    /// </summary>
    [Fact] public async Task SameAccountCannotRunOnTwoInstancesAtOnce()
    {
        using var primaryStore = new PuloniaTaskStore(_directory);
        using var otherStore = new PuloniaTaskStore(_directory);
        var primaryAccounts = Accounts(primaryStore, PuloniaInstanceKey.Primary);
        var my = (await primaryAccounts.LoadAccountsAsync()).Accounts[0];
        await primaryAccounts.SetCurrentAccountAsync(KeyA, my.Id);
        var blocking = new GameExecutor { Block = true };
        var otherExecutor = new GameExecutor();
        await using var primary = Service(primaryStore, primaryAccounts, blocking, new FakeSwitcher());
        await using var other = Service(otherStore, Accounts(otherStore, KeyA), otherExecutor, new FakeSwitcher());
        var plan = await primaryStore.SavePlanAsync(GamePlan());

        // 1. 主实例开始运行，持有“我的账户”的占用
        var first = await primary.EnqueueAsync(new PuloniaTaskRequest { PlanId = plan.Id });
        await blocking.Entered.Task.WaitAsync(WaitLimit);
        Assert.Equal(PuloniaInstanceKey.Primary, primaryStore.GetAccountOccupant(my.Id)?.InstanceKey);

        // 2. 云原神 a 为同一账户运行：进入待处理，说明账户正在哪里运行
        var refused = await RunAsync(other, new PuloniaTaskRequest { PlanId = plan.Id });
        Assert.Equal(PuloniaTaskRunStatus.NeedsAttention, refused.Status);
        Assert.Contains("正在主实例", refused.Message);
        Assert.Contains("请为每个实例设置不同的当前账户", refused.Message);
        Assert.Equal(0, otherExecutor.Executions);

        // 3. 主实例结束并释放占用后，云原神 a 可以运行
        blocking.Release.SetResult();
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, (await primary.WaitForCompletionAsync(first).WaitAsync(WaitLimit)).Status);
        await WaitUntilAsync(() => primaryStore.GetAccountOccupant(my.Id) is null);
        var accepted = await RunAsync(other, new PuloniaTaskRequest { PlanId = plan.Id });
        Assert.Equal((PuloniaTaskRunStatus.Succeeded, KeyA), (accepted.Status, accepted.Instance));
        Assert.Equal(1, otherExecutor.Executions);
    }

    /// <summary>
    /// 切换会把另一个在线实例挤下线时（4.6）：没有确认，或确认来自界面以外的来源，都不切换、进入待处理，本实例当前账户不变；
    /// 本实例界面手动运行并确认后才切换，成功后本实例当前账户改为目标账户。
    /// </summary>
    [Fact] public async Task SwitchThatDisplacesOnlineInstanceNeedsLocalConfirmation()
    {
        using var primaryStore = new PuloniaTaskStore(_directory);
        using var otherStore = new PuloniaTaskStore(_directory);
        var accounts = Accounts(primaryStore, PuloniaInstanceKey.Primary);
        var my = (await accounts.LoadAccountsAsync()).Accounts[0];
        var alt = await AddAccountAsync(accounts, "乙", "100000002");
        // 云原神 a 在线，当前账户是乙；本实例游戏中登录的是其他账户，需要切换
        await accounts.SetCurrentAccountAsync(KeyA, alt.Id);
        using var presence = otherStore.TryAcquireInstancePresence(Owner(KeyA, null));
        var switcher = new FakeSwitcher { GameUid = "100000009" };
        var executor = new GameExecutor();
        await using var service = Service(primaryStore, accounts, executor, switcher);
        var plan = await primaryStore.SavePlanAsync(GamePlan(alt.Id));

        // 1. 没有确认；或带确认但来源不是界面（自动请求的确认被忽略）
        foreach (var request in new[]
                 {
                     new PuloniaTaskRequest { PlanId = plan.Id, Source = "ui" },
                     new PuloniaTaskRequest { PlanId = plan.Id, Source = "cli", AllowDisplaceOnlineInstance = true }
                 })
        {
            var refused = await RunAsync(service, request);
            Assert.Equal(PuloniaTaskRunStatus.NeedsAttention, refused.Status);
            Assert.Contains("云原神 · a的当前账户是“乙”且正在线", refused.Message);
            Assert.Contains("挤下线", refused.Message);
        }
        Assert.Equal((0, 0), (switcher.Switches, executor.Executions));
        Assert.Equal(my.Id, await accounts.GetCurrentAccountIdAsync(PuloniaInstanceKey.Primary));

        // 2. 本实例界面手动运行并确认：切换后照常执行
        var confirmed = await RunAsync(service,
            new PuloniaTaskRequest { PlanId = plan.Id, Source = "ui", AllowDisplaceOnlineInstance = true });
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, confirmed.Status);
        Assert.Equal((1, 1), (switcher.Switches, executor.Executions));
        Assert.Equal(alt.Id, await accounts.GetCurrentAccountIdAsync(PuloniaInstanceKey.Primary));
    }

    /// <summary>
    /// “运行于”提交前检查（4.5、4.6、9.4）：当前账户未确定、几个目标为同一账户运行、账户被其他实例占用、云原神不能切换、
    /// 切换会挤下线。本实例的挤下线单独确认，下发到子实例的作为问题提示；被目标实例自己占用、云原神已登录目标账户时不提示。
    /// </summary>
    [Fact] public void RunTargetCheckReportsAccountConflicts()
    {
        var my = new PuloniaGenshinAccountProfile { Name = "甲", Uid = "100000001" };
        var alt = new PuloniaGenshinAccountProfile { Name = "乙", Uid = "100000002" };
        var third = new PuloniaGenshinAccountProfile { Name = "丙", Uid = "100000003" };
        PuloniaGenshinAccountProfile[] accounts = [my, alt, third];

        // 1. 跟随当前账户：未确定与重复
        var follow = PuloniaRunTargetCheck.Evaluate(null,
            [Target(PuloniaInstanceKey.Primary, true, my.Id), Target(KeyA, false, my.Id), Target(KeyB, false, null)],
            accounts, [], _ => null);
        Assert.Equal(["云原神 · b的当前账户未确定，请先为它选择当前账户。",
            "主实例、云原神 · a都会为“甲”运行，同一账户不能同时在两个实例运行，后开始的会进入待处理。"], follow.Problems);
        Assert.Null(follow.LocalDisplacement);

        // 2. 账户被其他实例占用时提示；被目标实例自己占用只是排在它的队列后面
        var occupied = PuloniaRunTargetCheck.Evaluate(null,
            [Target(PuloniaInstanceKey.Primary, true, my.Id), Target(KeyA, false, alt.Id)], accounts, [],
            accountId => accountId == my.Id ? new PuloniaLockOwner { InstanceKey = PuloniaInstanceKey.ChildSession }
                : accountId == alt.Id ? new PuloniaLockOwner { InstanceKey = KeyA } : null);
        Assert.Equal("账户“甲”正在桌面分身运行，主实例的运行开始时会进入待处理。", Assert.Single(occupied.Problems));

        // 3. 指定账户：云原神当前账户不是目标账户时不能切换；已是目标账户时不提示
        var cloud = PuloniaRunTargetCheck.Evaluate(third.Id,
            [Target(KeyA, false, alt.Id, third.Id)], accounts, [], _ => null);
        Assert.Equal("云原神 · a的当前账户是“乙”，云原神实例不能切换到“丙”。", Assert.Single(cloud.Problems));
        Assert.Empty(PuloniaRunTargetCheck.Evaluate(third.Id,
            [Target(KeyA, false, third.Id, third.Id)], accounts, [(KeyA, third.Id)], _ => null).Problems);

        // 4. 指定账户、需要切换，而另一个在线实例的当前账户就是目标账户：本实例的运行单独确认，下发的运行作为问题提示
        IReadOnlyList<(string InstanceKey, string? CurrentAccountId)> online = [(KeyB, third.Id), (PuloniaInstanceKey.Primary, my.Id)];
        var local = PuloniaRunTargetCheck.Evaluate(third.Id,
            [Target(PuloniaInstanceKey.Primary, true, my.Id, third.Id)], accounts, online, _ => null);
        Assert.Empty(local.Problems);
        Assert.Equal("云原神 · b的当前账户是“丙”且正在线，主实例切换到“丙”会把它挤下线。", local.LocalDisplacement);
        var dispatched = PuloniaRunTargetCheck.Evaluate(third.Id,
            [Target(PuloniaInstanceKey.ChildSession, false, my.Id, third.Id)], accounts, online, _ => null);
        Assert.Null(dispatched.LocalDisplacement);
        Assert.EndsWith("下发的运行不会挤下线，会进入待处理。", Assert.Single(dispatched.Problems));
    }

    /// <summary>仅清理本测试创建的独占目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    /// <summary>建立一个目标实例的提交前情况，显示名按实例键生成。</summary>
    /// <param name="instanceKey">实例键</param>
    /// <param name="isLocal">是否在本实例本地提交</param>
    /// <param name="currentAccountId">实例当前账户</param>
    /// <param name="planAccountId">计划指定的账户；为空表示跟随当前账户</param>
    private static PuloniaRunTargetState Target(string instanceKey, bool isLocal, string? currentAccountId, string? planAccountId = null)
        => new(instanceKey, PuloniaInstanceKey.Describe(instanceKey), isLocal, currentAccountId, planAccountId ?? currentAccountId);

    /// <summary>占用文件中写入的持有者。</summary>
    /// <param name="instanceKey">持有者实例键</param>
    /// <param name="runId">占用账户的运行 ID；在线标记为空</param>
    private static PuloniaLockOwner Owner(string instanceKey, string? runId) => new()
        { InstanceKey = instanceKey, RunId = runId, ProcessId = Environment.ProcessId, StartedAt = DateTimeOffset.UtcNow };

    /// <summary>指定实例的账户服务。</summary>
    /// <param name="store">该实例的存储对象</param>
    /// <param name="instanceKey">实例键</param>
    private static PuloniaAccountService Accounts(PuloniaTaskStore store, string instanceKey)
        => new(store, new PuloniaInstanceKeyProvider(instanceKey));

    /// <summary>新增一个带 UID 和登录资料的账户。</summary>
    /// <param name="accounts">账户服务</param>
    /// <param name="name">账户名称</param>
    /// <param name="uid">UID</param>
    private static async Task<PuloniaGenshinAccountProfile> AddAccountAsync(PuloniaAccountService accounts, string name, string uid)
    {
        var settings = await accounts.LoadAccountsAsync();
        var account = new PuloniaGenshinAccountProfile { Name = name, Uid = uid, ListText = "123****45" };
        settings.Accounts.Add(account);
        await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>());
        return account;
    }

    /// <summary>只含一个游戏节点的计划。</summary>
    /// <param name="accountId">计划指定的账户；为空表示跟随当前账户</param>
    private static PuloniaTaskPlan GamePlan(string? accountId = null) => new()
    {
        Name = "占用测试", AccountId = accountId,
        RootTask = new PuloniaTask { Children = [new PuloniaTask { Name = "游戏任务", TaskType = GameExecutor.TaskType }] }
    };

    /// <summary>建立某个实例的任务服务；租约替换为不触碰游戏的空租约。</summary>
    /// <param name="store">该实例的存储对象</param>
    /// <param name="accounts">该实例的账户服务，决定实例键</param>
    /// <param name="executor">游戏节点执行器</param>
    /// <param name="switcher">假的切号器</param>
    private static PuloniaTaskService Service(PuloniaTaskStore store, PuloniaAccountService accounts, GameExecutor executor,
        FakeSwitcher switcher)
        => new(store, new PuloniaTaskBuilder(store), [executor], new PuloniaGameTaskCoordinator(null!),
            new TaskStopService(NullLogger<TaskStopService>.Instance), accountSwitcher: switcher, accountService: accounts)
        {
            GameLeaseFactory = _ => Task.FromResult(new PuloniaGameTaskLease(null!, () => { }))
        };

    /// <summary>提交并等待一次运行结束。</summary>
    /// <param name="service">任务服务</param>
    /// <param name="request">运行请求</param>
    private static async Task<PuloniaTaskRunView> RunAsync(PuloniaTaskService service, PuloniaTaskRequest request)
    {
        var id = await service.EnqueueAsync(request);
        return await service.WaitForCompletionAsync(id).WaitAsync(WaitLimit);
    }

    /// <summary>轮询直到条件成立；超过上限时失败。</summary>
    /// <param name="condition">等待的条件</param>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + WaitLimit;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("等待条件成立超时。");
            await Task.Delay(50);
        }
    }

    /// <summary>假的切号器：游戏中的 UID 由测试设置，需要切换时记录次数并改为目标 UID。</summary>
    private sealed class FakeSwitcher : IPuloniaGenshinAccountSwitcher
    {
        /// <summary>游戏当前显示的 UID；为空表示读不到。</summary>
        public string? GameUid { get; set; }

        /// <summary>实际切换的次数。</summary>
        public int Switches { get; private set; }

        /// <inheritdoc />
        public Task<string> PrepareAsync(GameRuntime runtime, PuloniaGenshinAccountProfile account, Action switchStarted, CancellationToken ct)
        {
            if (GameUid == account.Uid)
                return Task.FromResult(account.Uid!);
            // 即将离开主界面：任务服务可在这里拒绝切换
            switchStarted();
            Switches++;
            GameUid = account.Uid;
            return Task.FromResult(account.Uid!);
        }

        /// <inheritdoc />
        public Task VerifyAsync(GameRuntime runtime, string expectedUid, CancellationToken ct)
            => GameUid == expectedUid ? Task.CompletedTask : throw new InvalidOperationException("UID 不一致。");

        /// <inheritdoc />
        public Task<string?> TryReadUidAsync(GameRuntime runtime, CancellationToken ct) => Task.FromResult(GameUid);

        /// <inheritdoc />
        public Task<(string Uid, string ImagePath)> CaptureCurrentAccountAsync(GameRuntime runtime, string accountId, CancellationToken ct)
            => throw new InvalidOperationException("测试不采集图片。");
    }

    /// <summary>需要游戏会话的假执行器；可在执行中等待测试放行，用来让两个运行重叠。</summary>
    private sealed class GameExecutor : IPuloniaTaskExecutor
    {
        /// <summary>任务类型。</summary>
        public const string TaskType = "test.game";

        /// <summary>执行次数，跨线程累加。</summary>
        private int _executions;

        /// <summary>执行时是否等待 <see cref="Release"/>。</summary>
        public bool Block { get; init; }

        /// <summary>第一次进入执行时完成。</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>测试放行被阻塞的执行。</summary>
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>执行次数。</summary>
        public int Executions => Volatile.Read(ref _executions);

        /// <inheritdoc />
        public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; } = [new()
        {
            TaskType = TaskType, RequiresGameSession = true,
            ParameterSchema = new JObject { ["type"] = "object", ["additionalProperties"] = true }
        }];

        /// <inheritdoc />
        public async Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task, PuloniaTaskExecutionContext context,
            CancellationToken ct)
        {
            Interlocked.Increment(ref _executions);
            Entered.TrySetResult();
            if (Block)
                await Release.Task.WaitAsync(ct);
            return PuloniaTaskOutcome.Success("完成");
        }
    }
}
