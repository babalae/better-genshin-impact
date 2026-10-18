using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>账号目录、绑定、快照和实际 UID 账本隔离回归，不启动程序或游戏。</summary>
public sealed class PuloniaGenshinAccountTests : IDisposable
{
    /// <summary>本测试独占的目录，不接触用户真实账号与凭据。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-pulonia-account-" + Guid.NewGuid().ToString("N"));

    /// <summary>首次使用无需目录文件，默认账号不会包含目标 UID 或登录凭据。</summary>
    [Fact] public async Task DefaultAccountNeedsNoConfiguration()
    {
        using var store = new PuloniaTaskStore(_directory);
        var settings = await store.LoadAccountSettingsAsync();
        var account = Assert.Single(settings.Accounts);
        Assert.True(account.IsDefault); Assert.Null(account.Uid); Assert.Null(account.CredentialReference);
        Assert.False(settings.MultiAccountEnabled); Assert.False(Directory.Exists(_directory));
    }

    /// <summary>目录保存检查修订冲突，失败时保留已保存资料并使用无 BOM 编码。</summary>
    [Fact] public async Task AccountDirectoryRejectsStaleRevisionAndKeepsSavedData()
    {
        using var store = new PuloniaTaskStore(_directory);
        var old = await store.LoadAccountSettingsAsync();
        var current = await store.LoadAccountSettingsAsync();
        current.Accounts.Add(new() { Name = "账号甲", Uid = "100000001" });
        current.MultiAccountEnabled = true;
        var saved = await store.SaveAccountSettingsAsync(current);
        Assert.Equal(1, saved.Revision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAccountSettingsAsync(old));
        Assert.Equal(2, (await store.LoadAccountSettingsAsync()).Accounts.Count);
        Assert.False((await File.ReadAllBytesAsync(Path.Combine(_directory, "accounts.json"))).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }));
        await File.WriteAllTextAsync(Path.Combine(_directory, "accounts.json"), "broken");
        await Assert.ThrowsAnyAsync<Exception>(() => store.LoadAccountSettingsAsync());
    }

    /// <summary>拒绝重复 UID 及污染默认账号，防止选择不同资料却共用错误登录目标。</summary>
    [Fact] public void InvalidAccountIdentityIsRejected()
    {
        var settings = new PuloniaGenshinAccountSettings();
        settings.Accounts.Add(new() { Uid = "100000001" }); settings.Accounts.Add(new() { Uid = "100000001" });
        Assert.Throws<InvalidOperationException>(() => PuloniaTaskStore.ValidateAccountSettings(settings));
        settings = new(); settings.Accounts[0].Uid = "100000001";
        Assert.Throws<InvalidOperationException>(() => PuloniaTaskStore.ValidateAccountSettings(settings));
    }

    /// <summary>同 UID 的不同资料共享额度，不同 UID 完全隔离；显式世界归属保持优先。</summary>
    [Fact] public void VerifiedUidControlsCooldownRatherThanProfileId()
    {
        var rule = new PuloniaTaskAvailabilityRule { RuleId = "daily", EffectKey = "reward",
            Kind = PuloniaTaskAvailabilityKind.ResetQuota, ResetPeriod = PuloniaTaskResetPeriod.Daily, Quota = 1 };
        var first = new PuloniaTaskRequest { AccountId = "profile_a" };
        var second = new PuloniaTaskRequest { AccountId = "profile_b" };
        var now = DateTimeOffset.UtcNow;
        var entry = PuloniaTaskAvailability.CreateLedgerEntry(first, rule,
            new PuloniaTaskCompletionEvent { RuleId = rule.RuleId, OccurredAt = now, Units = 1 }, "event", Guid.NewGuid(), "task", "100000001");
        Assert.False(PuloniaTaskAvailability.Evaluate(second, [rule], [entry], [], now, "100000001").IsAllowed);
        Assert.True(PuloniaTaskAvailability.Evaluate(second, [rule], [entry], [], now, "100000002").IsAllowed);
        Assert.Equal("cn/world/owner", PuloniaTaskAvailability.BuildScopeKey(new() { WorldOwnerAccountId = "owner" }, PuloniaTaskScopeKind.World, "100000001"));
    }

    /// <summary>纯一条龙固定账号资料但不初始化游戏，后续编辑不污染快照。</summary>
    [Fact] public async Task PurePlanFreezesAccountWithoutTouchingGame()
    {
        using var store = new PuloniaTaskStore(_directory);
        var settings = await store.LoadAccountSettingsAsync();
        var account = new PuloniaGenshinAccountProfile { Name = "账号甲", Uid = "100000001" };
        settings.Accounts.Add(account); settings = await store.SaveAccountSettingsAsync(settings);
        var registry = new PuloniaCSharpTaskRegistry();
        registry.Register("test.identity", (_, _, _) => Task.FromResult(PuloniaTaskOutcome.Success("纯计算完成")));
        var switcher = new UnexpectedGameSwitcher();
        await using var service = new PuloniaTaskService(store, new(store), [new PuloniaCSharpTaskExecutor(registry)],
            new PuloniaGameTaskCoordinator(null!), new TaskStopService(NullLogger<TaskStopService>.Instance), accountSwitcher: switcher);
        var plan = await store.SavePlanAsync(new PuloniaTaskPlan { Purpose = PuloniaTaskPlanPurpose.OneDragon, Name = "纯计算",
            Accounts = [new() { AccountId = account.Id }], RootTask = new() { Children = [new() { Name = "计算", TaskType = "csharp",
                Parameters = new JObject { ["operation"] = "test.identity" } }] } });
        var requestId = await service.EnqueueAsync(new() { PlanId = plan.Id });
        settings.Accounts.Single(a => a.Id == account.Id).Uid = "100000002";
        await store.SaveAccountSettingsAsync(settings);
        var run = await service.WaitForCompletionAsync(requestId).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, run.Status); Assert.Null(run.VerifiedUid);
        var snapshot = PuloniaTaskJson.ReadSnapshot(run.SnapshotJson);
        Assert.Equal("100000001", PuloniaTaskJson.Read<PuloniaGenshinAccountProfile>(snapshot.GenshinAccountJson!).Uid);
        Assert.Equal(account.Id, run.AccountId); Assert.Equal(0, switcher.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnqueueAsync(new() { PlanId = plan.Id, AccountId = "other" }));
    }

    /// <summary>账号图片缓存是内容固定版本，更新原图不影响旧快照。</summary>
    [Fact] public async Task FrozenImageDoesNotFollowOriginalChanges()
    {
        using var store = new PuloniaTaskStore(_directory);
        Directory.CreateDirectory(Path.Combine(_directory, "account-images"));
        var account = new PuloniaGenshinAccountProfile { Uid = "100000001", ImagePath = "account-images/input.png" };
        var source = store.GetAccountImagePath(account.ImagePath);
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        var frozen = store.FreezeAccount(account);
        await File.WriteAllBytesAsync(source, [4, 5, 6]);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(store.GetAccountImagePath(frozen.ImagePath!)));
        Assert.NotEqual(frozen.ImageVersion, store.FreezeAccount(account).ImageVersion);
        Assert.Throws<InvalidOperationException>(() => store.GetAccountImagePath("../outside.png"));
    }

    /// <summary>一条龙触发器不能单独覆盖配置绑定，普通计划仍保留原账号资料选择。</summary>
    [Fact] public void TriggerUsesOneDragonAccountBinding()
    {
        var plan = new PuloniaTaskPlan { Purpose = PuloniaTaskPlanPurpose.OneDragon, Accounts = [new() { AccountId = "account_a" }] };
        var editor = new PuloniaTaskTriggerEditorViewModel(new() { AccountId = "account_a" }, plan);
        Assert.False(editor.CanSelectAccount); Assert.Null(editor.CreateTrigger().AccountId);
        Assert.Equal("account_a", PuloniaTaskService.ResolveOneDragonAccountId(plan));
        plan.Accounts.Add(new() { AccountId = "account_b" });
        Assert.Throws<InvalidOperationException>(() => PuloniaTaskService.ResolveOneDragonAccountId(plan));
    }

    /// <summary>OCR 选择只规范化空白和掩码，不以任意相似文字放行。</summary>
    [Theory]
    [InlineData("123******45", "123******45", true)]
    [InlineData(" 123＊＊＊45 ", "123***45", true)]
    [InlineData("123******46", "123******45", false)]
    [InlineData("abc@mail.com", "abcd@mail.com", false)]
    public void AccountListRequiresFullIdentifier(string actual, string target, bool expected)
        => Assert.Equal(expected, PuloniaGenshinAccountSwitcher.MatchesListText(actual, target));

    /// <summary>纯任务如果错误进入任何游戏准备就使测试失败。</summary>
    private sealed class UnexpectedGameSwitcher : IPuloniaGenshinAccountSwitcher
    {
        /// <summary>检测游戏调用次数。</summary>
        public int Calls { get; private set; }
        /// <inheritdoc />
        public Task<string> PrepareAsync(GameRuntime runtime, PuloniaGenshinAccountProfile account, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("纯任务不能准备游戏"); }
        /// <inheritdoc />
        public Task VerifyAsync(GameRuntime runtime, string expectedUid, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("纯任务不能核验游戏"); }
        /// <inheritdoc />
        public Task<(string Uid, string ImagePath)> CaptureCurrentAccountAsync(GameRuntime runtime, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("纯任务不能采集账号"); }
    }

    /// <summary>仅清理本测试创建的独占目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
