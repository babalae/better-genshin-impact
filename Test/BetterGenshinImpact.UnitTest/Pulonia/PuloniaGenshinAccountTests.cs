using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Instance;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>账户目录、实例键与实例当前账户、快照和账户进度隔离回归，不启动程序或游戏。</summary>
public sealed class PuloniaGenshinAccountTests : IDisposable
{
    /// <summary>本测试独占的目录，不接触用户真实账户与凭据。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>首次读取时建立并落盘只含“我的账户”的目录，再次读取得到同一个账户。</summary>
    [Fact] public async Task FirstLoadCreatesMyAccount()
    {
        using var store = new PuloniaTaskStore(_directory);
        var first = await store.LoadAccountSettingsAsync();
        var account = Assert.Single(first.Accounts);
        Assert.Equal(PuloniaGenshinAccountProfile.MyAccountName, account.Name);
        Assert.Null(account.Uid);
        Assert.Equal(0, first.Revision);
        Assert.Equal(PuloniaGenshinAccountSettings.CurrentSchemaVersion, first.SchemaVersion);
        Assert.True(File.Exists(Path.Combine(_directory, "accounts.json")));
        Assert.Equal(account.Id, Assert.Single((await store.LoadAccountSettingsAsync()).Accounts).Id);
    }

    /// <summary>目录保存检查修订冲突，失败时保留已保存资料并使用无 BOM 编码。</summary>
    [Fact] public async Task AccountDirectoryRejectsStaleRevisionAndKeepsSavedData()
    {
        using var store = new PuloniaTaskStore(_directory);
        var old = await store.LoadAccountSettingsAsync();
        var current = await store.LoadAccountSettingsAsync();
        current.Accounts.Add(new() { Name = "账户甲", Uid = "100000001" });
        var saved = await store.SaveAccountSettingsAsync(current);
        Assert.Equal(1, saved.Revision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAccountSettingsAsync(old));
        Assert.Equal(2, (await store.LoadAccountSettingsAsync()).Accounts.Count);
        Assert.False((await File.ReadAllBytesAsync(Path.Combine(_directory, "accounts.json"))).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }));
        await File.WriteAllTextAsync(Path.Combine(_directory, "accounts.json"), "broken");
        await Assert.ThrowsAnyAsync<Exception>(() => store.LoadAccountSettingsAsync());
    }

    /// <summary>旧格式（无 schema_version、带 multi_account_enabled）不再被正式读取放行。</summary>
    [Fact] public async Task LegacyAccountDirectoryIsRejectedByStrictRead()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "accounts.json"),
            """{"revision":1,"multi_account_enabled":false,"accounts":[{"id":"default","name":"默认账号","uid":null,"login_mode":0,"image_path":null,"list_text":"","credential_reference":null}]}""");
        using var store = new PuloniaTaskStore(_directory);
        await Assert.ThrowsAnyAsync<Exception>(() => store.LoadAccountSettingsAsync());
    }

    /// <summary>账户目录规则：名称必填且不重复、UID 不重复且完整、最多一个账户没有 UID、至少一个账户。</summary>
    [Fact] public void AccountDirectoryRulesAreEnforced()
    {
        // 合法：一个没有 UID 的“我的账户”加一个有 UID 的账户
        var valid = PuloniaGenshinAccountSettings.CreateInitial(PuloniaTaskServerTimeZone.UtcPlus8);
        valid.Accounts.Add(new() { Name = "小号", Uid = "100000001", ServerTimeZone = PuloniaTaskServerTimeZone.Europe });
        PuloniaTaskStore.ValidateAccountSettings(valid);

        // 名称重复（忽略首尾空白）
        var duplicateName = PuloniaGenshinAccountSettings.CreateInitial(PuloniaTaskServerTimeZone.UtcPlus8);
        duplicateName.Accounts.Add(new() { Name = " 我的账户 ", Uid = "100000001" });
        Assert.Contains("重复", Assert.Throws<InvalidOperationException>(() => PuloniaTaskStore.ValidateAccountSettings(duplicateName)).Message);

        // UID 重复
        var duplicateUid = PuloniaGenshinAccountSettings.CreateInitial(PuloniaTaskServerTimeZone.UtcPlus8);
        duplicateUid.Accounts.Add(new() { Name = "甲", Uid = "100000001" });
        duplicateUid.Accounts.Add(new() { Name = "乙", Uid = "100000001" });
        Assert.Contains("100000001", Assert.Throws<InvalidOperationException>(() => PuloniaTaskStore.ValidateAccountSettings(duplicateUid)).Message);

        // 两个账户都没有 UID
        var twoWithoutUid = PuloniaGenshinAccountSettings.CreateInitial(PuloniaTaskServerTimeZone.UtcPlus8);
        twoWithoutUid.Accounts.Add(new() { Name = "新账户" });
        Assert.Contains("最多一个账户", Assert.Throws<InvalidOperationException>(() => PuloniaTaskStore.ValidateAccountSettings(twoWithoutUid)).Message);

        // UID 不完整、名称为空、空目录
        var shortUid = PuloniaGenshinAccountSettings.CreateInitial(PuloniaTaskServerTimeZone.UtcPlus8);
        shortUid.Accounts.Add(new() { Name = "甲", Uid = "123" });
        Assert.Throws<InvalidOperationException>(() => PuloniaTaskStore.ValidateAccountSettings(shortUid));
        var emptyName = new PuloniaGenshinAccountSettings { Accounts = [new() { Name = " ", Uid = "100000001" }] };
        Assert.Throws<InvalidOperationException>(() => PuloniaTaskStore.ValidateAccountSettings(emptyName));
        Assert.Throws<InvalidOperationException>(() => PuloniaTaskStore.ValidateAccountSettings(new PuloniaGenshinAccountSettings()));
    }

    /// <summary>新增账户的默认服务器时区按通用配置映射：+8、+1、-5 分别对应三个服务器，其他偏移按 UTC+8。</summary>
    [Theory]
    [InlineData(8, PuloniaTaskServerTimeZone.UtcPlus8)]
    [InlineData(1, PuloniaTaskServerTimeZone.Europe)]
    [InlineData(-5, PuloniaTaskServerTimeZone.America)]
    [InlineData(9, PuloniaTaskServerTimeZone.UtcPlus8)]
    public void ServerTimeZoneFollowsGeneralConfig(int hours, PuloniaTaskServerTimeZone expected)
        => Assert.Equal(expected, PuloniaServerTime.ToServerTimeZone(TimeSpan.FromHours(hours)));

    /// <summary>实例键与实例目录名：主实例、桌面分身、云原神（小写实例名），目录名把冒号换成短横线。</summary>
    [Fact] public void InstanceKeysMapToDirectories()
    {
        Assert.Equal("primary", PuloniaInstanceKeyProvider.GetInstanceKey(BetterGiInstanceType.Primary, null));
        Assert.Equal("child_session", PuloniaInstanceKeyProvider.GetInstanceKey(BetterGiInstanceType.ChildSession, null));
        Assert.Equal("webview:小号a", PuloniaInstanceKeyProvider.GetInstanceKey(BetterGiInstanceType.WebView, "小号A"));
        Assert.Equal("primary", PuloniaInstanceKey.ToDirectoryName("primary"));
        Assert.Equal("child_session", PuloniaInstanceKey.ToDirectoryName("child_session"));
        Assert.Equal("webview-小号a", PuloniaInstanceKey.ToDirectoryName("webview:小号a"));
        Assert.Equal("云原神 · 小号a", PuloniaInstanceKey.DescribeDirectory("webview-小号a"));
        // 运行记录的执行实例按实例键显示
        Assert.Equal("主实例", PuloniaInstanceKey.Describe("primary"));
        Assert.Equal("桌面分身", PuloniaInstanceKey.Describe("child_session"));
        Assert.Equal("云原神 · 小号a", PuloniaInstanceKey.Describe("webview:小号a"));
        foreach (var invalid in new[] { "", "webview:", "webview:A", "webview:a/b", "child_session:3", "other" })
            Assert.Throws<PuloniaTaskValidationException>(() => PuloniaInstanceKey.Validate(invalid));
    }

    /// <summary>实例首次使用时当前账户为列表第一个账户并立即写入；可改为“未确定”，不能改为不存在的账户。</summary>
    [Fact] public async Task InstanceCurrentAccountDefaultsToFirstAccount()
    {
        using var store = new PuloniaTaskStore(_directory);
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider("webview:小号a"));
        var my = Assert.Single((await accounts.LoadAccountsAsync()).Accounts);
        Assert.Equal(my.Id, await accounts.GetCurrentAccountIdAsync(accounts.CurrentInstanceKey));
        var path = Path.Combine(_directory, "instances", "webview-小号a", "settings.json");
        Assert.True(File.Exists(path));
        Assert.Equal(1, JObject.Parse(await File.ReadAllTextAsync(path)).Value<int>("schema_version"));

        PuloniaCurrentAccountChangedEventArgs? changed = null;
        accounts.CurrentAccountChanged += (_, e) => changed = e;
        await accounts.SetCurrentAccountAsync(accounts.CurrentInstanceKey, null);
        Assert.Null(await accounts.GetCurrentAccountAsync(accounts.CurrentInstanceKey));
        Assert.Equal(JTokenType.Null, JObject.Parse(await File.ReadAllTextAsync(path))["current_account_id"]!.Type);
        Assert.Equal("webview:小号a", changed!.InstanceKey);
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.SetCurrentAccountAsync("primary", "acc_missing"));
    }

    /// <summary>删除账户前检查引用：被计划指定或是某个实例的当前账户时拒绝；清空被指定账户的 UID 同样拒绝。</summary>
    [Fact] public async Task AccountRemovalChecksPlansAndInstances()
    {
        using var store = new PuloniaTaskStore(_directory);
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider("primary"));
        var settings = await accounts.LoadAccountsAsync();
        var my = settings.Accounts[0];
        var alt = new PuloniaGenshinAccountProfile { Name = "小号", Uid = "100000002" };
        settings.Accounts.Add(alt);
        settings = (await accounts.SaveAccountsAsync(settings, Credentials())).Settings;
        // 主实例当前账户为“我的账户”，计划指定“小号”
        Assert.Equal(my.Id, await accounts.GetCurrentAccountIdAsync("primary"));
        await store.SavePlanAsync(new PuloniaTaskPlan { Name = "指定小号", AccountId = alt.Id });

        Assert.Contains("指定小号", await accounts.CheckAccountRemovalAsync(alt.Id));
        Assert.Contains("主实例", await accounts.CheckAccountRemovalAsync(my.Id));
        var withoutAlt = PuloniaTaskJson.Read<PuloniaGenshinAccountSettings>(PuloniaTaskJson.Write(settings));
        withoutAlt.Accounts.RemoveAll(account => account.Id == alt.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.SaveAccountsAsync(withoutAlt, Credentials()));
        var withoutMy = PuloniaTaskJson.Read<PuloniaGenshinAccountSettings>(PuloniaTaskJson.Write(settings));
        withoutMy.Accounts.RemoveAll(account => account.Id == my.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.SaveAccountsAsync(withoutMy, Credentials()));
        // 清空被指定账户的 UID：先给“我的账户”填 UID，避免触发“最多一个账户没有 UID”
        var clearUid = PuloniaTaskJson.Read<PuloniaGenshinAccountSettings>(PuloniaTaskJson.Write(settings));
        clearUid.Accounts[0].Uid = "100000001";
        clearUid.Accounts.Single(account => account.Id == alt.Id).Uid = null;
        Assert.Contains("指定小号", (await Assert.ThrowsAsync<InvalidOperationException>(
            () => accounts.SaveAccountsAsync(clearUid, Credentials()))).Message);

        // 实例改为其他账户后可以删除“我的账户”
        await accounts.SetCurrentAccountAsync("primary", alt.Id);
        Assert.Null(await accounts.CheckAccountRemovalAsync(my.Id));
        var changes = 0;
        accounts.AccountsChanged += (_, _) => changes++;
        var saved = await accounts.SaveAccountsAsync(withoutMy, Credentials());
        Assert.Equal(alt.Id, Assert.Single(saved.Settings.Accounts).Id);
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// 完成记录与首领领奖次数按账户存放：一个账户的记录不影响另一个账户；删除账户时在保存目录后删除它的进度目录，
    /// 其他账户的进度保留。
    /// </summary>
    [Fact] public async Task AccountProgressIsIsolatedAndDeletedWithAccount()
    {
        using var store = new PuloniaTaskStore(_directory);
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
        var settings = await accounts.LoadAccountsAsync();
        var my = settings.Accounts[0];
        var alt = new PuloniaGenshinAccountProfile { Name = "小号", Uid = "100000002" };
        settings.Accounts.Add(alt);
        settings = (await accounts.SaveAccountsAsync(settings, Credentials())).Settings;
        var rule = PuloniaRouteCooldown.Resolve(Path.Combine(_directory, "落落莓", "A.json"), null, new JObject());
        var now = DateTimeOffset.UtcNow;
        var completion = new PuloniaRouteCompletion
        {
            Route = rule.FileIdentity, CompletedAt = now, RunId = PuloniaId.NewRunId(DateTimeOffset.Now), TaskAddress = "plan/task",
            Attempt = 1, Instance = PuloniaInstanceKey.Primary, Rule = PuloniaRouteCompletionRule.From(rule)
        };
        Assert.True(await accounts.RecordRouteCompletionAsync(alt.Id, completion));
        await accounts.IncrementBossRewardCountAsync(alt.Id, "plan", "task", "");

        // 1. 只有写入的账户按 CD 跳过；另一个账户没有记录
        Assert.True(Status(alt.Id).IsSkipped);
        Assert.False(Status(my.Id).IsSkipped);
        Assert.Equal(0, await accounts.GetBossRewardCountAsync(my.Id, "plan", "task", ""));
        Assert.True(Directory.Exists(Path.Combine(_directory, "accounts", alt.Id)));

        // 2. 删除账户：进度目录随账户一起删除
        settings.Accounts.RemoveAll(account => account.Id == alt.Id);
        await accounts.SaveAccountsAsync(settings, Credentials());
        Assert.False(Directory.Exists(Path.Combine(_directory, "accounts", alt.Id)));
        Assert.Empty(await accounts.ListRouteCompletionsAsync(alt.Id));

        PuloniaRouteCooldownStatus Status(string accountId) => PuloniaRouteCooldown.GetStatus(rule,
            accounts.GetRouteCompletionIndexAsync(accountId).GetAwaiter().GetResult().Find(rule.FileIdentity),
            now.AddHours(1), PuloniaServerTime.GetUtcOffsetMinutes(PuloniaTaskServerTimeZone.UtcPlus8));
    }

    /// <summary>
    /// 计划指定账户时快照在准备时固定账户资料（普通计划也适用），后续编辑不污染快照；
    /// 纯计算运行不确定运行账户，也不触碰游戏。指定的账户不存在或没有 UID 时拒绝准备。
    /// </summary>
    [Fact] public async Task PurePlanFreezesAccountWithoutTouchingGame()
    {
        using var store = new PuloniaTaskStore(_directory);
        var settings = await store.LoadAccountSettingsAsync();
        var account = new PuloniaGenshinAccountProfile { Name = "账户甲", Uid = "100000001" };
        settings.Accounts.Add(account); settings = await store.SaveAccountSettingsAsync(settings);
        var registry = new PuloniaCSharpTaskRegistry();
        registry.Register("test.identity", (_, _, _) => Task.FromResult(PuloniaTaskOutcome.Success("纯计算完成")));
        var switcher = new UnexpectedGameSwitcher();
        await using var service = new PuloniaTaskService(store, new(store), [new PuloniaCSharpTaskExecutor(registry)],
            new PuloniaGameTaskCoordinator(null!), new TaskStopService(NullLogger<TaskStopService>.Instance), accountSwitcher: switcher);
        var plan = await store.SavePlanAsync(new PuloniaTaskPlan { Name = "纯计算",
            AccountId = account.Id, RootTask = new() { Children = [new() { Name = "计算", TaskType = "csharp",
                Parameters = new JObject { ["operation"] = "test.identity" } }] } });
        var requestId = await service.EnqueueAsync(new() { PlanId = plan.Id });
        settings.Accounts.Single(a => a.Id == account.Id).Uid = "100000002";
        settings = await store.SaveAccountSettingsAsync(settings);
        var run = await service.WaitForCompletionAsync(requestId).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, run.Status); Assert.Null(run.VerifiedUid);
        var snapshot = PuloniaTaskJson.ReadSnapshot(run.SnapshotJson);
        Assert.Equal(account.Id, snapshot.AccountId);
        Assert.Equal("100000001", PuloniaTaskJson.Read<PuloniaGenshinAccountProfile>(snapshot.GenshinAccountJson!).Uid);
        // 不含游戏节点的运行不确定运行账户
        Assert.Null(run.AccountId); Assert.Null(run.AccountSource); Assert.Equal(0, switcher.Calls);

        // 指定的账户没有 UID 或不存在时，准备阶段直接拒绝
        var my = settings.Accounts[0];
        var noUid = await store.SavePlanAsync(new PuloniaTaskPlan { Name = "指定无 UID", AccountId = my.Id });
        Assert.Contains("UID", (await Assert.ThrowsAsync<PuloniaTaskValidationException>(
            () => service.EnqueueAsync(new() { PlanId = noUid.Id }))).Message);
        var missing = await store.SavePlanAsync(new PuloniaTaskPlan { Name = "指定不存在", AccountId = "acc_missing" });
        await Assert.ThrowsAsync<PuloniaTaskValidationException>(() => service.EnqueueAsync(new() { PlanId = missing.Id }));
    }

    /// <summary>账户图片每次都新建可读文件名，不覆盖旧图片，旧快照引用的内容保持不变。</summary>
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

    /// <summary>凭据在账户目录保存成功后才写入，同一账户始终使用固定目标名，改密码时覆盖而不另建条目。</summary>
    [Fact] public async Task CredentialsAreCommittedAfterDirectoryWithStableTarget()
    {
        var vault = new MemoryCredentialVault();
        using var store = new PuloniaTaskStore(_directory) { CredentialVault = vault };
        var settings = await store.LoadAccountSettingsAsync();
        var account = new PuloniaGenshinAccountProfile { Name = "账户甲", Uid = "100000001" };
        settings.Accounts.Add(account);
        var target = PuloniaTaskStore.GetCredentialTarget(account.Id);
        // 写入凭据时账户目录必须已经引用该目标名，证明先保存目录再提交凭据。
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

    /// <summary>账户目录保存失败（修订冲突或资料无效）时不写入任何凭据，不会留下孤立条目。</summary>
    [Fact] public async Task FailedDirectorySaveWritesNoCredential()
    {
        var vault = new MemoryCredentialVault();
        using var store = new PuloniaTaskStore(_directory) { CredentialVault = vault };
        var stale = await store.LoadAccountSettingsAsync();
        await store.SaveAccountSettingsAsync(await store.LoadAccountSettingsAsync());
        var account = new PuloniaGenshinAccountProfile { Name = "账户甲", Uid = "100000001" };
        stale.Accounts.Add(account);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAccountSettingsAsync(stale, Credentials(account.Id, "user", "secret")));
        var current = await store.LoadAccountSettingsAsync();
        current.Accounts.Add(new PuloniaGenshinAccountProfile { Name = "账户乙", Uid = "123" });
        var invalid = current.Accounts[^1];
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAccountSettingsAsync(current, Credentials(invalid.Id, "user", "secret")));
        current.Accounts.RemoveAt(current.Accounts.Count - 1);
        var imageOnly = new PuloniaGenshinAccountProfile { Name = "图片账户", Uid = "100000002", LoginMode = PuloniaGenshinAccountLoginMode.Image };
        current.Accounts.Add(imageOnly);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAccountSettingsAsync(current, Credentials(imageOnly.Id, "user", "secret")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAccountSettingsAsync(current, Credentials("acc_missing", "user", "secret")));
        Assert.Equal(0, vault.Writes); Assert.Empty(vault.Items);
        Assert.Single((await store.LoadAccountSettingsAsync()).Accounts);
    }

    /// <summary>账户被移除或改用不使用密码的登录方式后，保存成功时删除旧凭据并清除引用。</summary>
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
        var account = new PuloniaGenshinAccountProfile { Name = "账户甲", Uid = "100000001" };
        settings.Accounts.Add(account);
        var result = await store.SaveAccountSettingsAsync(settings, Credentials(account.Id, "user", "secret"));
        Assert.Contains("凭据管理器不可用", result.CredentialError);
        Assert.Equal(1, result.Settings.Revision);
        Assert.Equal(2, (await store.LoadAccountSettingsAsync()).Accounts.Count);
        vault.BeforeWrite = null;
        var retry = await store.SaveAccountSettingsAsync(result.Settings, Credentials(account.Id, "user", "secret"));
        Assert.Null(retry.CredentialError); Assert.Single(vault.Items);
    }

    /// <summary>OCR 选择只规范化空白和掩码，不以任意相似文字放行。</summary>
    [Theory]
    [InlineData("123******45", "123******45", true)]
    [InlineData(" 123＊＊＊45 ", "123***45", true)]
    [InlineData("123******46", "123******45", false)]
    [InlineData("abc@mail.com", "abcd@mail.com", false)]
    public void AccountListRequiresFullIdentifier(string actual, string target, bool expected)
        => Assert.Equal(expected, PuloniaGenshinAccountSwitcher.MatchesListText(actual, target));

    /// <summary>建立只含一个账户的凭据草稿。</summary>
    private static Dictionary<string, (string UserName, string Password)> Credentials(string accountId, string userName, string password)
        => new() { [accountId] = (userName, password) };

    /// <summary>不填写任何凭据的空草稿。</summary>
    private static Dictionary<string, (string UserName, string Password)> Credentials() => new();

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
        public Task<string> PrepareAsync(GameRuntime runtime, PuloniaGenshinAccountProfile account, Action switchStarted, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("纯任务不能准备游戏"); }
        /// <inheritdoc />
        public Task VerifyAsync(GameRuntime runtime, string expectedUid, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("纯任务不能核验游戏"); }
        /// <inheritdoc />
        public Task<string?> TryReadUidAsync(GameRuntime runtime, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("纯任务不能读取 UID"); }
        /// <inheritdoc />
        public Task<(string Uid, string ImagePath)> CaptureCurrentAccountAsync(GameRuntime runtime, string accountId, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("纯任务不能采集账户"); }
    }

    /// <summary>仅清理本测试创建的独占目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
