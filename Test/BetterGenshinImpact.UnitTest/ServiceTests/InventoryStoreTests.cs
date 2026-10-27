using System.Globalization;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using Newtonsoft.Json;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>在临时目录验证库存合并、异常、快照和恢复，不启动游戏或 UI。</summary>
public sealed class InventoryStoreTests : IDisposable
{
    /// <summary>每个测试独立的数据根。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BetterGI.InventoryTests", Guid.NewGuid().ToString("N"));
    /// <summary>小型可控公共目录。</summary>
    private readonly InventoryCatalog _catalog;
    /// <summary>测试存储。</summary>
    private readonly InventoryStore _store;
    /// <summary>固定测试账号，与实际账号无关。</summary>
    private const string Uid = "1";
    /// <summary>所有测试上报早于当前时间。</summary>
    private readonly DateTimeOffset _time = DateTimeOffset.UtcNow.AddHours(-2);

    /// <summary>建立测试字典，不依赖资源包或用户目录。</summary>
    public InventoryStoreTests()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "items.csv");
        File.WriteAllText(path, "item_name,material_type\n甜甜花,\n摩拉,货币\n");
        _catalog = new InventoryCatalog(path);
        _store = new InventoryStore(_catalog, Path.Combine(_root, "records"));
    }

    /// <summary>构造明确带时区的上报。</summary>
    private InventoryReport Report(string name, long count, int minute = 0) => new()
    { Name = name, Quantity = count, RecordedAt = _time.AddMinutes(minute).ToString("O", CultureInfo.InvariantCulture) };

    /// <summary>来源引用使用原始目录。</summary>
    private static AccountRecordSource Source(string reference) => new() { Reference = reference, DisplayName = reference };

    /// <summary>交换只携带当前项，往返保留零值、原始 UTC 时间和来源。</summary>
    [Fact]
    public async Task Exchange_RoundTripPreservesCurrentRecord()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("摩拉", 8)]);
        await _store.ReportAsync(Uid, Source("b"), [Report("摩拉", 0, 1)]);
        var package = await _store.ExportInventoryAsync(Uid);
        var path = Path.Combine(_root, "exchange.json");
        await File.WriteAllTextAsync(path, package.ToString());
        var parsed = await InventoryStore.ReadExchangeAsync(path);
        var result = await _store.ImportInventoryAsync(parsed, "2", false);
        Assert.Single(result.Accepted);
        var record = Assert.Single((await _store.LoadAsync("2")).Inventory.Items["摩拉"]);
        Assert.Equal(0L, record.Quantity);
        Assert.Equal(_time.AddMinutes(1), record.RecordedAt);
        Assert.Equal("b", record.Source);
    }

    /// <summary>缺少历史时间不能误写为本次导入时间，任何结构错误都应在写入前拒绝。</summary>
    [Fact]
    public async Task Exchange_MissingTimeRejectedBeforeWrite()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("摩拉", 8)]);
        var package = await _store.ExportInventoryAsync(Uid);
        ((Newtonsoft.Json.Linq.JObject)package["accounts"]![Uid]!["inventory"]!["摩拉"]![0]!).Remove("recordedAt");
        await Assert.ThrowsAsync<InvalidDataException>(() => _store.ImportInventoryAsync(package, "2", false));
        Assert.DoesNotContain("2", await _store.ListUidsAsync());
    }

    /// <summary>旧导入进入异常；文件不能篡改已登记的来源名称或清除未提及的材料。</summary>
    [Fact]
    public async Task Exchange_OldRecordKeepsCurrentAndLocalSourceName()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("摩拉", 8)]);
        var package = await _store.ExportInventoryAsync(Uid);
        package["sources"]!["a"]!["displayName"] = "外部改名";
        await _store.ReportAsync("2", Source("a"), [Report("摩拉", 9, 1), Report("甜甜花", 3, 1)]);
        var result = await _store.ImportInventoryAsync(package, "2", false);
        Assert.Single(result.Rejected);
        var workspace = await _store.LoadAsync("2");
        Assert.Equal(9L, workspace.Inventory.Items["摩拉"][0].Quantity);
        Assert.Equal(3L, workspace.Inventory.Items["甜甜花"][0].Quantity);
        Assert.Equal("a", workspace.Sources["a"].DisplayName);
        Assert.Equal("RecordConflict", workspace.Inventory.Errors["摩拉"].Code);
    }

    /// <summary>外部字典须明确授权；授权后也不能覆盖本地已有分类。</summary>
    [Fact]
    public async Task Exchange_DefinitionsRequireConsentAndPreserveLocalCategory()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8)]);
        var package = await _store.ExportInventoryAsync(Uid);
        package["itemDefinitions"]!["甜甜花"] = new Newtonsoft.Json.Linq.JObject { ["category"] = "外部分类" };
        await _store.ImportInventoryAsync(package, "2", false);
        Assert.Equal("", (await _store.LoadAsync("2")).Definitions["甜甜花"]);
        await _store.DefineAsync("甜甜花", "本地分类");
        await _store.ImportInventoryAsync(package, "2", true);
        Assert.Equal("本地分类", (await _store.LoadAsync("2")).Definitions["甜甜花"]);
    }

    /// <summary>数量不变也更新，同一来源只留一项并移动到队首。</summary>
    [Fact]
    public async Task SameQuantity_MovesSourceToFrontWithoutDuplicate()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8)]);
        await _store.ReportAsync(Uid, Source("b"), [Report("甜甜花", 8, 1)]);
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8, 2)]);
        var records = (await _store.LoadAsync(Uid)).Inventory.Items["甜甜花"];
        Assert.Equal(new[] { "a", "b" }, records.Select(record => record.Source));
        Assert.Equal(_time.AddMinutes(2), records[0].RecordedAt);
    }

    /// <summary>部分异常不阻止有效项目；零值是真实记录。</summary>
    [Fact]
    public async Task InvalidNameAndNegativeQuantity_DoNotBlockValidZero()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("摩拉", 0), Report("甜甜花", -1), Report("未知物品", 5)]);
        var document = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(0L, document.Items["摩拉"][0].Quantity);
        Assert.False(document.Items.ContainsKey("未知物品"));
        Assert.Equal(2, document.Errors.Count);
    }

    /// <summary>识别失败不得把已有数量改成零或刷新其时间。</summary>
    [Fact]
    public async Task ReadFailure_PreservesLastValidRecord()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8)]);
        await _store.ReportFailureAsync(Uid, Source("a"), "甜甜花", "没有识别到数量");
        var document = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(8L, document.Items["甜甜花"][0].Quantity);
        Assert.Equal(_time, document.Items["甜甜花"][0].RecordedAt);
        Assert.Equal("ReadFailed", document.Errors["甜甜花"].Code);
    }

    /// <summary>清表保留 UID，而删除账号移除 UID；两者都移除库存。</summary>
    [Fact]
    public async Task ClearAndDeleteUid_HaveDistinctSemantics()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8)]);
        await _store.ClearAsync(Uid, false, false);
        Assert.Contains(Uid, await _store.ListUidsAsync());
        Assert.Empty((await _store.LoadAsync(Uid)).Inventory.Items);
        await _store.ClearAsync(Uid, true, true);
        Assert.DoesNotContain(Uid, await _store.ListUidsAsync());
    }

    /// <summary>旧记录保留双方证据；接受时保留原时间，之后可丢弃其他错误。</summary>
    [Fact]
    public async Task OlderRecord_RequiresExplicitAcceptance()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 10, 2)]);
        await _store.ReportAsync(Uid, Source("b"), [Report("甜甜花", 8)]);
        var document = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(10L, document.Items["甜甜花"][0].Quantity);
        var error = document.Errors["甜甜花"];
        Assert.Equal(10L, error.Current!.Quantity);
        await _store.ResolveAsync(Uid, "甜甜花", error.Id, true);
        document = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(8L, document.Items["甜甜花"][0].Quantity);
        Assert.Equal(_time, document.Items["甜甜花"][0].RecordedAt);
        Assert.Empty(document.Errors);
    }

    /// <summary>打开异常详情后发生新上报，旧处理请求不能覆盖新库存。</summary>
    [Fact]
    public async Task ConflictAcceptance_RejectsChangedCurrentRecord()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 10, 2)]);
        await _store.ReportAsync(Uid, Source("b"), [Report("甜甜花", 8)]);
        var error = (await _store.LoadAsync(Uid)).Inventory.Errors["甜甜花"];
        await _store.ReportAsync(Uid, Source("c"), [Report("甜甜花", 12, 3)]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ResolveAsync(Uid, "甜甜花", error.Id, true));
        Assert.Equal(12L, (await _store.LoadAsync(Uid)).Inventory.Items["甜甜花"][0].Quantity);
    }

    /// <summary>队列超过配置上限只淘汰最久未上报的来源。</summary>
    [Fact]
    public async Task RecentQueue_RespectsConfiguredLimit()
    {
        await _store.ConfigureAsync(new AccountRecordsSettings { RecentLimit = 2 });
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8)]);
        await _store.ReportAsync(Uid, Source("b"), [Report("甜甜花", 8, 1)]);
        await _store.ReportAsync(Uid, Source("c"), [Report("甜甜花", 8, 2)]);
        Assert.Equal(new[] { "c", "b" }, (await _store.LoadAsync(Uid)).Inventory.Items["甜甜花"].Select(record => record.Source));
    }

    /// <summary>不带时区的外部时间被明确拒绝，而不是按本机时间猜测。</summary>
    [Fact]
    public async Task AmbiguousTime_ProducesErrorWithoutInventory()
    {
        await _store.ReportAsync(Uid, Source("a"), [new InventoryReport { Name = "甜甜花", Quantity = 8, RecordedAt = "2026-10-26 12:00:00" }]);
        var document = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Empty(document.Items);
        Assert.False(document.Errors["甜甜花"].CanAccept);
    }

    /// <summary>同批重复但互相矛盾的材料不可通过先后顺序静默覆盖。</summary>
    [Fact]
    public async Task DuplicateConflict_RejectsOnlyConflictingItem()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 1), Report("甜甜花", 2), Report("摩拉", 3)]);
        var document = (await _store.LoadAsync(Uid)).Inventory;
        Assert.False(document.Items.ContainsKey("甜甜花"));
        Assert.Equal(3L, document.Items["摩拉"][0].Quantity);
        Assert.Contains("甜甜花", document.Errors.Keys);
    }

    /// <summary>多个服务实例串行修改同一账号，不丢失局部更新。</summary>
    [Fact]
    public async Task ConcurrentStores_PreserveDifferentItemsAndUidIsolation()
    {
        var other = new InventoryStore(_catalog, Path.Combine(_root, "records"));
        await Task.WhenAll(_store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8)]),
            other.ReportAsync(Uid, Source("b"), [Report("摩拉", 100)]),
            other.ReportAsync("2", Source("a"), [Report("甜甜花", 99)]));
        Assert.Equal(2, (await _store.LoadAsync(Uid)).Inventory.Items.Count);
        Assert.Equal(99L, (await _store.LoadAsync("2")).Inventory.Items["甜甜花"][0].Quantity);
    }

    /// <summary>北京时间凌晨四点前后恰好分属两个记录日。</summary>
    [Fact]
    public void RecordDay_UsesBeijingFourAm()
    {
        Assert.Equal(new DateOnly(2026, 10, 25), InventoryStore.RecordDay(DateTimeOffset.Parse("2026-10-26T03:59:59+08:00")));
        Assert.Equal(new DateOnly(2026, 10, 26), InventoryStore.RecordDay(DateTimeOffset.Parse("2026-10-26T04:00:00+08:00")));
    }

    /// <summary>隔多日运行只生成一份已知状态快照，重复读取不重复备份。</summary>
    [Fact]
    public async Task Snapshot_UsesLastKnownDayAndDoesNotInventGapDays()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8)]);
        var path = Path.Combine(_root, "records", Uid, "inventory.json");
        var document = JsonConvert.DeserializeObject<InventoryDocument>(File.ReadAllText(path))!;
        document.ModifiedAt = DateTimeOffset.UtcNow.AddDays(-12);
        File.WriteAllText(path, JsonConvert.SerializeObject(document));
        await _store.LoadAsync(Uid);
        await _store.LoadAsync(Uid);
        var snapshots = Directory.GetFiles(Path.Combine(_root, "records", Uid, "snapshots"), "*.json");
        Assert.Single(snapshots);
        Assert.Equal(InventoryStore.RecordDay(document.ModifiedAt.Value).ToString("yyyy-MM-dd") + ".json", Path.GetFileName(snapshots[0]));
        var snapshot = JsonConvert.DeserializeObject<InventoryDocument>(File.ReadAllText(snapshots[0]))!;
        Assert.Equal(_time, snapshot.Items["甜甜花"][0].RecordedAt);
    }

    /// <summary>用户字典补充独立保存，不改公共 CSV。</summary>
    [Fact]
    public async Task UserDefinition_AllowsExplicitAdditionButCannotReplaceBuiltInCategory()
    {
        await _store.DefineAsync("甜甜花", "烹饪材料");
        await _store.DefineAsync("测试新材料", "测试分类");
        await _store.SetLocalQuantityAsync(Uid, "测试新材料", 5);
        var workspace = await _store.LoadAsync(Uid);
        Assert.Equal("烹饪材料", workspace.Definitions["甜甜花"]);
        Assert.Equal("", _catalog.Items["甜甜花"]);
        Assert.Equal("AccountRecords.SetLocalQuantityAsync", workspace.Inventory.Items["测试新材料"][0].Source);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.DefineAsync("摩拉", "其他"));
    }

    /// <summary>坏 JSON 从上一份有效备份恢复，而不是创建空库存。</summary>
    [Fact]
    public async Task CorruptJson_RecoversPreviousAtomicSave()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 8)]);
        await _store.ReportAsync(Uid, Source("a"), [Report("甜甜花", 9, 1)]);
        File.WriteAllText(Path.Combine(_root, "records", Uid, "inventory.json"), "{broken");
        Assert.Equal(8L, (await _store.LoadAsync(Uid)).Inventory.Items["甜甜花"][0].Quantity);
    }

    /// <summary>仅删除本测试创建的目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
