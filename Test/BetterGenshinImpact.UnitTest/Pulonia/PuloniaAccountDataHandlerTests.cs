using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 账户目录与按 UID 存放数据的其他模块（如狛荷屋）的扩展点：保存前检查 UID 修改，被拒绝时不保存；
/// 保存后数据随 UID 改名、随账户删除，失败只作为警告返回，不回滚目录。用记录调用的替身代替真实模块。
/// </summary>
public sealed class PuloniaAccountDataHandlerTests : IDisposable
{
    /// <summary>本测试独占的目录，不接触用户真实账户与凭据。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>修改 UID 时先检查再保存，保存成功后通知改名，且在“账户目录已变化”事件之前完成。</summary>
    [Fact] public async Task UidChangeIsCheckedThenRenamedAfterSave()
    {
        using var store = new PuloniaTaskStore(_directory);
        var handler = new RecordingHandler(store);
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary), [handler]);
        var settings = await AddAccountAsync(accounts, "乙", "100000002");
        var renamedBeforeEvent = false;
        accounts.AccountsChanged += (_, _) => renamedBeforeEvent = handler.Renamed.Count == 1;

        settings.Accounts[1].Uid = "100000003";
        var result = await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>());

        Assert.Equal([("100000002", (string?)"100000003")], handler.Checked);
        Assert.Equal([("100000002", "100000003")], handler.Renamed);
        // 改名时目录已经保存为新 UID
        Assert.Equal(["100000003"], handler.SavedUidsAtRename);
        Assert.True(renamedBeforeEvent);
        Assert.Null(result.DataWarning);
        Assert.Empty(handler.Removed);
    }

    /// <summary>关联数据模块拒绝 UID 修改时抛出带账户名称的原因，账户目录不保存，也不通知改名。</summary>
    [Fact] public async Task RejectedUidChangeDoesNotSave()
    {
        using var store = new PuloniaTaskStore(_directory);
        var handler = new RecordingHandler { Rejection = "UID 100000003 已有测试数据，不能改为这个 UID。" };
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary), [handler]);
        var settings = await AddAccountAsync(accounts, "乙", "100000002");
        var revision = settings.Revision;

        settings.Accounts[1].Uid = "100000003";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>()));

        Assert.Contains("乙", error.Message);
        Assert.Contains("已有测试数据", error.Message);
        var saved = await accounts.LoadAccountsAsync();
        Assert.Equal(revision, saved.Revision);
        Assert.Equal("100000002", saved.Accounts[1].Uid);
        Assert.Empty(handler.Renamed);
    }

    /// <summary>删除有 UID 的账户后通知删除它的数据；只改名称或服务器时不检查 UID。</summary>
    [Fact] public async Task RemovedAccountDataIsDeletedAfterSave()
    {
        using var store = new PuloniaTaskStore(_directory);
        var handler = new RecordingHandler();
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary), [handler]);
        var settings = await AddAccountAsync(accounts, "乙", "100000002");
        settings.Accounts[1].Name = "丙";
        settings = (await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>())).Settings;
        Assert.Empty(handler.Checked);

        settings.Accounts.RemoveAt(1);
        var result = await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>());

        Assert.Equal(["100000002"], handler.Removed);
        Assert.Null(result.DataWarning);
        Assert.Single((await accounts.LoadAccountsAsync()).Accounts);
    }

    /// <summary>保存后改名失败时目录保持已保存，保存结果带警告，说明哪个账户的哪类数据未能处理。</summary>
    [Fact] public async Task RenameFailureIsReturnedAsWarning()
    {
        using var store = new PuloniaTaskStore(_directory);
        var handler = new RecordingHandler { RenameFailure = new IOException("文件被占用") };
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary), [handler]);
        var settings = await AddAccountAsync(accounts, "乙", "100000002");

        settings.Accounts[1].Uid = "100000003";
        var result = await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>());

        Assert.Equal("100000003", (await accounts.LoadAccountsAsync()).Accounts[1].Uid);
        Assert.NotNull(result.DataWarning);
        Assert.Contains("“乙”的测试数据", result.DataWarning);
        Assert.Contains("文件被占用", result.DataWarning);
    }

    /// <summary>
    /// 删除前列出实际会删除的数据（统筹设计 4.1）：凭据、不再被引用的图片（含被替换的旧图片）、完成记录、
    /// 有次数的首领领奖条目与关联模块报告的数据；没有数据的项不列出，不在目录中的账户没有任何影响。
    /// </summary>
    [Fact] public async Task RemovalImpactListsActualData()
    {
        using var store = new PuloniaTaskStore(_directory) { CredentialVault = new MemoryCredentialVault() };
        var handler = new RecordingHandler();
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary), [handler]);
        var settings = await accounts.LoadAccountsAsync();
        var my = settings.Accounts[0];
        var target = new PuloniaGenshinAccountProfile { Name = "乙", Uid = "100000002" };
        await store.CreateAccountImageAsync(target.Id, [1, 2, 3]);
        target.ImagePath = await store.CreateAccountImageAsync(target.Id, [4, 5, 6]);
        settings.Accounts.Add(target);
        await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)> { [target.Id] = ("user", "secret") });

        // 1. 刚建立的账户：只有凭据和两张图片
        var fresh = await accounts.DescribeAccountRemovalAsync(target.Id);
        Assert.Equal((0, 0, true, 2), (fresh.RouteCompletionCount, fresh.BossRewardEntryCount, fresh.HasCredential, fresh.ImageCount));
        Assert.Equal(["保存的登录凭据（账号密码）", "账户图片 2 张"], fresh.Describe());

        // 2. 写入两条完成记录、两个节点的首领领奖次数，关联模块报告该 UID 有数据
        Assert.True(await accounts.RecordRouteCompletionAsync(target.Id, Completion("A")));
        Assert.True(await accounts.RecordRouteCompletionAsync(target.Id, Completion("B")));
        await accounts.IncrementBossRewardCountAsync(target.Id, "plan", "task-1", "");
        await accounts.IncrementBossRewardCountAsync(target.Id, "plan", "task-1", "");
        await accounts.IncrementBossRewardCountAsync(target.Id, "plan", "task-2", "");
        handler.DataUids.Add("100000002");
        var impact = await accounts.DescribeAccountRemovalAsync(target.Id);
        Assert.Equal((2, 2), (impact.RouteCompletionCount, impact.BossRewardEntryCount));
        Assert.Equal(["保存的登录凭据（账号密码）", "账户图片 2 张", "路线 CD 完成记录 2 条", "首领领奖次数 2 项", "测试数据"],
            impact.Describe());

        // 3. 没有任何数据的“我的账户”与不在目录中的账户：列表为空
        Assert.Empty((await accounts.DescribeAccountRemovalAsync(my.Id)).Describe());
        Assert.Empty((await accounts.DescribeAccountRemovalAsync("acc_missing")).Describe());
    }

    /// <summary>建立一条路线的完成记录，路线文件不需要存在。</summary>
    /// <param name="routeName">路线文件名（不含扩展名）</param>
    private PuloniaRouteCompletion Completion(string routeName)
    {
        var rule = PuloniaRouteCooldown.Resolve(Path.Combine(_directory, "落落莓", routeName + ".json"), null, new JObject());
        return new PuloniaRouteCompletion
        {
            Route = rule.FileIdentity, CompletedAt = DateTimeOffset.UtcNow, RunId = PuloniaId.NewRunId(DateTimeOffset.Now),
            TaskAddress = "plan/" + routeName, Attempt = 1, Instance = PuloniaInstanceKey.Primary, Rule = PuloniaRouteCompletionRule.From(rule)
        };
    }

    /// <summary>在账户目录末尾加入一个有 UID 的账户并保存，返回保存后的目录。</summary>
    private static async Task<PuloniaGenshinAccountSettings> AddAccountAsync(IPuloniaAccountService accounts, string name, string uid)
    {
        var settings = await accounts.LoadAccountsAsync();
        settings.Accounts.Add(new PuloniaGenshinAccountProfile { Name = name, Uid = uid });
        return (await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>())).Settings;
    }

    /// <summary>仅清理本测试创建的独占目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    /// <summary>记录各次调用的关联数据模块替身；可配置拒绝原因和改名失败。</summary>
    /// <param name="store">改名时读取磁盘上的账户目录；为空时不读取</param>
    private sealed class RecordingHandler(PuloniaTaskStore? store = null) : IPuloniaAccountDataHandler
    {
        /// <summary>检查时返回的拒绝原因；为空时允许修改。</summary>
        public string? Rejection { get; init; }

        /// <summary>改名时抛出的异常；为空时成功。</summary>
        public Exception? RenameFailure { get; init; }

        /// <summary>收到的检查请求。</summary>
        public List<(string OldUid, string? NewUid)> Checked { get; } = [];

        /// <summary>收到的改名通知。</summary>
        public List<(string OldUid, string NewUid)> Renamed { get; } = [];

        /// <summary>收到改名通知时磁盘上账户目录中的 UID，用来确认改名发生在保存之后。</summary>
        public List<string> SavedUidsAtRename { get; } = [];

        /// <summary>收到的删除通知。</summary>
        public List<string> Removed { get; } = [];

        /// <inheritdoc />
        public string DataName => "测试数据";

        /// <inheritdoc />
        public Task<string?> CheckUidChangeAsync(string oldUid, string? newUid, CancellationToken ct = default)
        {
            Checked.Add((oldUid, newUid));
            return Task.FromResult(Rejection);
        }

        /// <inheritdoc />
        public async Task OnUidChangedAsync(string oldUid, string newUid, CancellationToken ct = default)
        {
            if (RenameFailure is not null)
                throw RenameFailure;
            Renamed.Add((oldUid, newUid));
            if (store is not null)
                SavedUidsAtRename.AddRange((await store.LoadAccountSettingsAsync(ct)).Accounts.Select(account => account.Uid).OfType<string>());
        }

        /// <inheritdoc />
        public Task OnAccountRemovedAsync(string uid, CancellationToken ct = default)
        {
            Removed.Add(uid);
            return Task.CompletedTask;
        }

        /// <summary>报告有数据的 UID。</summary>
        public HashSet<string> DataUids { get; } = [];

        /// <inheritdoc />
        public Task<bool> HasDataAsync(string uid, CancellationToken ct = default) => Task.FromResult(DataUids.Contains(uid));
    }

    /// <summary>内存凭据库，测试不接触用户真实的 Windows 凭据。</summary>
    private sealed class MemoryCredentialVault : IPuloniaCredentialVault
    {
        /// <summary>当前保存的凭据，键为目标名。</summary>
        private readonly Dictionary<string, (string UserName, string Password)> _items = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public void Write(string target, string userName, string password) => _items[target] = (userName, password);

        /// <inheritdoc />
        public void Delete(string target) => _items.Remove(target);
    }
}
