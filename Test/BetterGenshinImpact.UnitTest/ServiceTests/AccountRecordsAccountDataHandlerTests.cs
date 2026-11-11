using System.Globalization;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>
/// 狛荷屋接入 Pulonia 账户目录：在临时目录用真实的库存存储与账户服务验证 UID 修改时数据目录改名、
/// 新 UID 已有记录或清空 UID 时拒绝、删除账户时一并删除狛荷屋记录。与其他人工操作测试共用门禁，串行执行。
/// </summary>
[Collection("AccountRecordsManualOperations")]
public sealed class AccountRecordsAccountDataHandlerTests : IDisposable
{
    /// <summary>每个测试独立的数据根。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BetterGI.AccountRecordsHandlerTests", Guid.NewGuid().ToString("N"));

    /// <summary>狛荷屋库存存储。</summary>
    private readonly InventoryStore _store;

    /// <summary>Pulonia 存储，保存账户目录。</summary>
    private readonly PuloniaTaskStore _puloniaStore;

    /// <summary>接入了狛荷屋的账户服务。</summary>
    private readonly PuloniaAccountService _accounts;

    /// <summary>被测的狛荷屋扩展点实现。</summary>
    private readonly AccountRecordsAccountDataHandler _handler;

    /// <summary>建立测试字典、库存存储与账户服务，不依赖资源包或用户目录。</summary>
    public AccountRecordsAccountDataHandlerTests()
    {
        Directory.CreateDirectory(_root);
        var csv = Path.Combine(_root, "items.csv");
        File.WriteAllText(csv, "item_name,material_type\n摩拉,货币\n");
        _store = new InventoryStore(new InventoryCatalog(csv), Path.Combine(_root, "records"));
        _handler = new AccountRecordsAccountDataHandler(_store);
        _puloniaStore = new PuloniaTaskStore(Path.Combine(_root, "pulonia"));
        _accounts = new PuloniaAccountService(_puloniaStore, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary), [_handler]);
    }

    /// <summary>修改 UID 后狛荷屋记录随之改名，按新 UID 能读出原库存；新 UID 已有记录时拒绝且目录不变。</summary>
    [Fact]
    public async Task UidChangeRenamesRecordsAndRejectsExistingTarget()
    {
        var settings = await AddAccountsAsync(("乙", "100000002"), ("丙", "100000003"));
        await ReportAsync("100000002", 8);
        await ReportAsync("100000003", 9);

        // 1. 乙改为没有记录的 UID：目录改名，库存保留
        settings.Accounts[1].Uid = "100000004";
        settings = (await _accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>())).Settings;
        Assert.False(await _store.HasUidDataAsync("100000002"));
        Assert.Equal(8, (await _store.LoadAsync("100000004")).Inventory.Items["摩拉"][0].Quantity);

        // 2. 同一次保存中丙改为新 UID、乙改为丙原来的 UID：该 UID 仍有狛荷屋记录，拒绝（不处理链式改名），目录与数据都不变
        settings.Accounts[2].Uid = "100000005";
        settings.Accounts[1].Uid = "100000003";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>()));
        Assert.Contains("已有狛荷屋记录", error.Message);
        var saved = await _accounts.LoadAccountsAsync();
        Assert.Equal(["100000004", "100000003"], saved.Accounts.Skip(1).Select(account => account.Uid));
        Assert.Equal(8, (await _store.LoadAsync("100000004")).Inventory.Items["摩拉"][0].Quantity);
        Assert.Equal(9, (await _store.LoadAsync("100000003")).Inventory.Items["摩拉"][0].Quantity);
    }

    /// <summary>有狛荷屋记录的账户不能清空 UID；没有记录时允许。</summary>
    [Fact]
    public async Task ClearingUidIsRejectedOnlyWithRecords()
    {
        await ReportAsync("100000002", 8);
        Assert.Contains("不能清空 UID", await _handler.CheckUidChangeAsync("100000002", null));
        Assert.Null(await _handler.CheckUidChangeAsync("100000006", null));
        Assert.Null(await _handler.CheckUidChangeAsync("100000002", "100000007"));
    }

    /// <summary>删除账户后，它的狛荷屋记录一并删除，其他账户的记录保留。</summary>
    [Fact]
    public async Task RemovingAccountDeletesItsRecords()
    {
        var settings = await AddAccountsAsync(("乙", "100000002"), ("丙", "100000003"));
        await ReportAsync("100000002", 8);
        await ReportAsync("100000003", 9);

        settings.Accounts.RemoveAt(1);
        var result = await _accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>());

        Assert.Null(result.DataWarning);
        Assert.False(await _store.HasUidDataAsync("100000002"));
        Assert.True(await _store.HasUidDataAsync("100000003"));
        Assert.Equal(["100000003"], await _store.ListUidsAsync());
    }

    /// <summary>在账户目录末尾加入有 UID 的账户并保存。</summary>
    private async Task<PuloniaGenshinAccountSettings> AddAccountsAsync(params (string Name, string Uid)[] accounts)
    {
        var settings = await _accounts.LoadAccountsAsync();
        foreach (var (name, uid) in accounts)
            settings.Accounts.Add(new PuloniaGenshinAccountProfile { Name = name, Uid = uid });
        return (await _accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>())).Settings;
    }

    /// <summary>为 UID 上报一条摩拉库存，建立它的狛荷屋数据目录。</summary>
    private Task ReportAsync(string uid, long quantity) => _store.ReportAsync(uid,
        new AccountRecordSource { Reference = "scan", DisplayName = "scan" },
        [new InventoryReport { Name = "摩拉", Quantity = quantity, RecordedAt = DateTimeOffset.UtcNow.AddHours(-1).ToString("O", CultureInfo.InvariantCulture) }]);

    /// <summary>释放存储并清理本测试的目录。</summary>
    public void Dispose()
    {
        _puloniaStore.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
