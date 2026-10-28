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
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

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

    /// <summary>
    /// 切换到指定账号时按核验 UID 隔离：同 UID 的不同资料共享采集 CD，不同 UID 完全隔离；
    /// 未切换账号时不论资料 ID 都使用默认身份，已有账本键格式不变。
    /// </summary>
    [Fact] public void VerifiedUidControlsCooldownRatherThanProfileId()
    {
        var policy = PuloniaCollectionCooldown.Resolve(Path.Combine(_directory, "落落莓", "A.json"), null, new JObject());
        var offset = new PuloniaTaskRequest().ServerUtcOffsetMinutes;
        var now = DateTimeOffset.UtcNow;
        var entry = new PuloniaTaskLedgerEntry
        {
            EffectKey = PuloniaCollectionCooldown.GetEffectKey(policy),
            ScopeKey = PuloniaCollectionCooldown.BuildScopeKey("100000001"),
            OccurredAt = now, NextEligibleAt = now.AddHours(46)
        };
        Assert.True(Status("100000001").IsSkipped);
        Assert.False(Status("100000002").IsSkipped);
        // 未切换账号（默认账号或没有账号准备）的运行不受指定账号记录影响
        Assert.False(Status(null).IsSkipped);
        Assert.Equal("cn/world/100000001", entry.ScopeKey);
        Assert.Equal("cn/world/default", PuloniaCollectionCooldown.BuildScopeKey());

        // 按运行的作用域查找最新记录后判定，与任务服务的执行前判定一致。
        PuloniaCollectionCooldownStatus Status(string? uid) => PuloniaCollectionCooldown.GetStatus(policy,
            PuloniaCollectionCooldown.FindLatest([entry], PuloniaCollectionCooldown.BuildScopeKey(uid),
                PuloniaCollectionCooldown.GetEffectKey(policy)), now.AddHours(1), offset);
    }

    /// <summary>
    /// 只有快照固定了非默认账号时才算“切换账号”：一条龙默认账号与普通计划一样使用默认作用域，
    /// 同一真实账号在两者之间共享采集 CD。
    /// </summary>
    [Fact] public async Task OnlySpecificAccountSnapshotsUseVerifiedUidScope()
    {
        using var store = new PuloniaTaskStore(_directory);
        var registry = new PuloniaCSharpTaskRegistry();
        var plan = new PuloniaTaskPlan { Name = "作用域", RootTask = new() { Children = [new() { Name = "计算", TaskType = "csharp",
            Parameters = new JObject { ["operation"] = "sample.sum" } }] } };
        var snapshot = await new PuloniaTaskBuilder(store).BuildAsync(plan, new PuloniaTaskBuildOptions
            { Definitions = new PuloniaCSharpTaskExecutor(registry).Definitions.ToList() });

        Assert.False(PuloniaTaskService.IsSwitchedAccount(snapshot));
        Assert.False(PuloniaTaskService.IsSwitchedAccount(snapshot.WithGenshinAccount(PuloniaGenshinAccountProfile.CreateDefault())));
        Assert.True(PuloniaTaskService.IsSwitchedAccount(snapshot.WithGenshinAccount(
            new PuloniaGenshinAccountProfile { Name = "账号甲", Uid = "100000001" })));
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

    /// <summary>账号图片每次都新建可读文件名，不覆盖旧图片，旧快照引用的内容保持不变。</summary>
    [Fact] public async Task AccountImagesAreNeverOverwritten()
    {
        using var store = new PuloniaTaskStore(_directory);
        var account = new PuloniaGenshinAccountProfile { Uid = "100000001" };
        var first = await store.CreateAccountImageAsync(account.Id, [1, 2, 3]);
        var second = await store.CreateAccountImageAsync(account.Id, [4, 5, 6]);
        Assert.NotEqual(first, second);
        Assert.Matches("^account-images\\\\" + account.Id + "-\\d{8}-\\d{6}(-\\d+)?\\.png$", first);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(store.GetAccountImagePath(first)));
        Assert.Equal(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(store.GetAccountImagePath(second)));
        Assert.Throws<InvalidOperationException>(() => store.GetAccountImagePath("../outside.png"));
    }

    /// <summary>凭据在账号目录保存成功后才写入，同一账号始终使用固定目标名，改密码时覆盖而不另建条目。</summary>
    [Fact] public async Task CredentialsAreCommittedAfterDirectoryWithStableTarget()
    {
        var vault = new MemoryCredentialVault();
        using var store = new PuloniaTaskStore(_directory) { CredentialVault = vault };
        var settings = await store.LoadAccountSettingsAsync();
        var account = new PuloniaGenshinAccountProfile { Name = "账号甲", Uid = "100000001" };
        settings.Accounts.Add(account);
        var target = PuloniaTaskStore.GetCredentialTarget(account.Id);
        // 写入凭据时账号目录必须已经引用该目标名，证明先保存目录再提交凭据。
        vault.BeforeWrite = written => Assert.Contains(written, File.ReadAllText(Path.Combine(_directory, "accounts.json")));
        var first = await store.SaveAccountSettingsAsync(settings, Credentials(account.Id, "user", "secret-1"));
        Assert.Null(first.CredentialError);
        Assert.Equal(target, first.Settings.Accounts.Single(a => a.Id == account.Id).CredentialReference);
        Assert.Null(account.CredentialReference);
        var second = await store.SaveAccountSettingsAsync(first.Settings, Credentials(account.Id, "user", "secret-2"));
        Assert.Null(second.CredentialError);
        Assert.Equal(("user", "secret-2"), Assert.Single(vault.Items).Value);
        Assert.Equal(target, Assert.Single(vault.Items).Key);
        Assert.Equal(2, vault.Writes); Assert.Empty(vault.Deletes);
        // 不带凭据的普通保存保留已保存凭据。
        var third = await store.SaveAccountSettingsAsync(second.Settings);
        Assert.Equal(target, third.Accounts.Single(a => a.Id == account.Id).CredentialReference);
        Assert.Single(vault.Items); Assert.Empty(vault.Deletes);
    }

    /// <summary>账号目录保存失败（修订冲突或资料无效）时不写入任何凭据，不会留下孤立条目。</summary>
    [Fact] public async Task FailedDirectorySaveWritesNoCredential()
    {
        var vault = new MemoryCredentialVault();
        using var store = new PuloniaTaskStore(_directory) { CredentialVault = vault };
        var stale = await store.LoadAccountSettingsAsync();
        await store.SaveAccountSettingsAsync(await store.LoadAccountSettingsAsync());
        var account = new PuloniaGenshinAccountProfile { Name = "账号甲", Uid = "100000001" };
        stale.Accounts.Add(account);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAccountSettingsAsync(stale, Credentials(account.Id, "user", "secret")));
        var current = await store.LoadAccountSettingsAsync();
        current.Accounts.Add(new PuloniaGenshinAccountProfile { Name = "账号乙", Uid = "123" });
        var invalid = current.Accounts[^1];
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAccountSettingsAsync(current, Credentials(invalid.Id, "user", "secret")));
        current.Accounts.RemoveAt(current.Accounts.Count - 1);
        var imageOnly = new PuloniaGenshinAccountProfile { Name = "图片账号", Uid = "100000002", LoginMode = PuloniaGenshinAccountLoginMode.Image };
        current.Accounts.Add(imageOnly);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAccountSettingsAsync(current, Credentials(imageOnly.Id, "user", "secret")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAccountSettingsAsync(current, Credentials(PuloniaGenshinAccountProfile.DefaultId, "user", "secret")));
        Assert.Equal(0, vault.Writes); Assert.Empty(vault.Items);
        Assert.Single((await store.LoadAccountSettingsAsync()).Accounts);
    }

    /// <summary>账号被移除或改用不使用密码的登录方式后，保存成功时删除旧凭据并清除引用。</summary>
    [Fact] public async Task RemovedOrPasswordlessAccountDeletesCredential()
    {
        var vault = new MemoryCredentialVault();
        using var store = new PuloniaTaskStore(_directory) { CredentialVault = vault };
        var settings = await store.LoadAccountSettingsAsync();
        var removed = new PuloniaGenshinAccountProfile { Name = "将移除", Uid = "100000001" };
        var passwordless = new PuloniaGenshinAccountProfile { Name = "改用图片", Uid = "100000002", LoginMode = PuloniaGenshinAccountLoginMode.Password };
        var kept = new PuloniaGenshinAccountProfile { Name = "保留", Uid = "100000003" };
        settings.Accounts.AddRange([removed, passwordless, kept]);
        var credentials = new Dictionary<string, (string UserName, string Password)>
        {
            [removed.Id] = ("a", "1"), [passwordless.Id] = ("b", "2"), [kept.Id] = ("c", "3")
        };
        settings = (await store.SaveAccountSettingsAsync(settings, credentials)).Settings;
        Assert.Equal(3, vault.Items.Count);
        settings.Accounts.RemoveAll(a => a.Id == removed.Id);
        settings.Accounts.Single(a => a.Id == passwordless.Id).LoginMode = PuloniaGenshinAccountLoginMode.Image;
        var saved = await store.SaveAccountSettingsAsync(settings);
        Assert.Null(saved.Accounts.Single(a => a.Id == passwordless.Id).CredentialReference);
        Assert.Equal(PuloniaTaskStore.GetCredentialTarget(kept.Id), saved.Accounts.Single(a => a.Id == kept.Id).CredentialReference);
        Assert.Equal(new[] { PuloniaTaskStore.GetCredentialTarget(removed.Id), PuloniaTaskStore.GetCredentialTarget(passwordless.Id) }
            .Order(StringComparer.Ordinal), vault.Deletes.Order(StringComparer.Ordinal));
        Assert.Equal(PuloniaTaskStore.GetCredentialTarget(kept.Id), Assert.Single(vault.Items).Key);
    }

    /// <summary>凭据写入失败不回滚已保存的目录，返回新修订和失败说明，调用方可直接重试。</summary>
    [Fact] public async Task CredentialWriteFailureKeepsSavedDirectory()
    {
        var vault = new MemoryCredentialVault { BeforeWrite = _ => throw new InvalidOperationException("凭据管理器不可用") };
        using var store = new PuloniaTaskStore(_directory) { CredentialVault = vault };
        var settings = await store.LoadAccountSettingsAsync();
        var account = new PuloniaGenshinAccountProfile { Name = "账号甲", Uid = "100000001" };
        settings.Accounts.Add(account);
        var result = await store.SaveAccountSettingsAsync(settings, Credentials(account.Id, "user", "secret"));
        Assert.Contains("凭据管理器不可用", result.CredentialError);
        Assert.Equal(1, result.Settings.Revision);
        Assert.Equal(2, (await store.LoadAccountSettingsAsync()).Accounts.Count);
        vault.BeforeWrite = null;
        var retry = await store.SaveAccountSettingsAsync(result.Settings, Credentials(account.Id, "user", "secret"));
        Assert.Null(retry.CredentialError); Assert.Single(vault.Items);
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

    /// <summary>建立只含一个账号的凭据草稿。</summary>
    private static Dictionary<string, (string UserName, string Password)> Credentials(string accountId, string userName, string password)
        => new() { [accountId] = (userName, password) };

    /// <summary>内存凭据库，记录写入与删除，测试不接触用户真实的 Windows 凭据。</summary>
    private sealed class MemoryCredentialVault : IPuloniaCredentialVault
    {
        /// <summary>当前保存的凭据，键为目标名。</summary>
        public Dictionary<string, (string UserName, string Password)> Items { get; } = new(StringComparer.Ordinal);
        /// <summary>已删除的目标名，按调用顺序记录。</summary>
        public List<string> Deletes { get; } = [];
        /// <summary>成功写入次数。</summary>
        public int Writes { get; private set; }
        /// <summary>写入前的检查或故障注入，参数为目标名。</summary>
        public Action<string>? BeforeWrite { get; set; }
        /// <inheritdoc />
        public void Write(string target, string userName, string password)
        {
            BeforeWrite?.Invoke(target);
            Items[target] = (userName, password); Writes++;
        }
        /// <inheritdoc />
        public void Delete(string target) { Deletes.Add(target); Items.Remove(target); }
    }

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
        public Task<(string Uid, string ImagePath)> CaptureCurrentAccountAsync(GameRuntime runtime, string accountId, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("纯任务不能采集账号"); }
    }

    /// <summary>仅清理本测试创建的独占目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
