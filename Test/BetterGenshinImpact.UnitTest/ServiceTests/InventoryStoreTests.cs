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
    private const string Uid = "100000001";

    /// <summary>清空用户字典后删除补充项，即使有旧备份也不恢复；原始物品目录仍保留。</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    [InlineData("{}")]
    public async Task UserDictionary_EmptyContentDoesNotRestoreBackup(string content)
    {
        var path = Path.Combine(_root, "records", "item-overrides.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var original = JsonConvert.SerializeObject(new Dictionary<string, InventoryItemOverride>
        { ["测试补充物品"] = new() { Category = "测试分类" } });
        File.WriteAllText(path, original);
        File.WriteAllText(path + ".bak", original);
        Assert.Contains("测试补充物品", (await _store.LoadAsync(Uid)).Definitions.Keys);
        File.WriteAllText(path, content);
        var definitions = (await _store.LoadAsync(Uid)).Definitions;
        Assert.DoesNotContain("测试补充物品", definitions.Keys);
        Assert.Contains("摩拉", definitions.Keys);
        Assert.Equal(content, File.ReadAllText(path));
    }
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
        var result = await _store.ImportInventoryAsync(parsed, "100000002", false);
        Assert.Single(result.Accepted);
        var record = Assert.Single((await _store.LoadAsync("100000002")).Inventory.Items["摩拉"]);
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
        await Assert.ThrowsAsync<InvalidDataException>(() => _store.ImportInventoryAsync(package, "100000002", false));
        Assert.DoesNotContain("100000002", await _store.ListUidsAsync());
    }

    /// <summary>旧导入进入异常；文件不能篡改已登记的来源名称或清除未提及的材料。</summary>
    [Fact]
    public async Task Exchange_OldRecordKeepsCurrentAndLocalSourceName()
    {
        await _store.ReportAsync(Uid, Source("a"), [Report("摩拉", 8)]);
        var package = await _store.ExportInventoryAsync(Uid);
        package["sources"]!["a"]!["displayName"] = "外部改名";
        await _store.ReportAsync("100000002", Source("a"), [Report("摩拉", 9, 1), Report("甜甜花", 3, 1)]);
        var result = await _store.ImportInventoryAsync(package, "100000002", false);
        Assert.Single(result.Rejected);
        var workspace = await _store.LoadAsync("100000002");
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
        await _store.ImportInventoryAsync(package, "100000002", false);
        Assert.Equal("", (await _store.LoadAsync("100000002")).Definitions["甜甜花"]);
        await _store.DefineAsync("甜甜花", "本地分类");
        await _store.ImportInventoryAsync(package, "100000002", true);
        Assert.Equal("本地分类", (await _store.LoadAsync("100000002")).Definitions["甜甜花"]);
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

    /// <summary>保留记录重传不因当前值更新或待核实增量而制造新错误。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedDuplicate_PrecedesConflictAndVerification(bool withPendingDelta)
    {
        var original = Report("摩拉", 80);
        await _store.ReportAsync(Uid, Source("first"), [original]);
        await _store.ReportAsync(Uid, Source("second"), [Report("摩拉", 90, 1)]);
        if (withPendingDelta)
            await _store.ReportDeltaAsync(Uid, Source("delta"),
                [new InventoryDeltaReport { Name = "摩拉", Delta = -1, RecordedAt = _time.AddMinutes(2).ToString("O") }]);
        var result = await _store.ReportAsync(Uid, Source("first"), [original]);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Empty(result.Accepted);
        Assert.Empty(result.Rejected);
        var data = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(90L, data.Items["摩拉"][0].Quantity);
        Assert.Empty(data.Errors);
        Assert.DoesNotContain(data.Events, item => item.State == "Error" || item.State == "Completed");
        if (withPendingDelta) Assert.Contains(data.Events, item => item.State == "Pending" && !item.Archived);
    }

    /// <summary>重复识别不能吞掉同时间不同数量或同批矛盾的输入。</summary>
    [Fact]
    public async Task AcceptedDuplicate_DoesNotHideConflictingQuantity()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var different = await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 81)]);
        Assert.Single(different.Rejected);
        Assert.Equal(0, different.DuplicateCount);
        var conflictingBatch = await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80), Report("摩拉", 82)]);
        Assert.Single(conflictingBatch.Rejected);
        Assert.Equal(0, conflictingBatch.DuplicateCount);
        Assert.Equal(80L, (await _store.LoadAsync(Uid)).Inventory.Items["摩拉"][0].Quantity);
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
            other.ReportAsync("100000002", Source("a"), [Report("甜甜花", 99)]));
        Assert.Equal(2, (await _store.LoadAsync(Uid)).Inventory.Items.Count);
        Assert.Equal(99L, (await _store.LoadAsync("100000002")).Inventory.Items["甜甜花"][0].Quantity);
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

    /// <summary>可编辑字典语法错误保留原文，不静默覆盖为旧备份。</summary>
    [Theory]
    [InlineData("item-overrides.json")]
    [InlineData("sources.json")]
    public async Task Dictionary_InvalidJsonPreservesEditedFile(string file)
    {
        var path = Path.Combine(_root, "records", file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{broken");
        File.WriteAllText(path + ".bak", "{}");
        await Assert.ThrowsAsync<InvalidDataException>(() => _store.LoadAsync(Uid));
        Assert.Equal("{broken", File.ReadAllText(path));
        File.WriteAllText(path, "{}");
        await _store.LoadAsync(Uid);
    }

    /// <summary>各上报路径使用相同来源回退规则，重建服务后仍能读取。</summary>
    [Fact]
    public async Task Sources_EmptyDisplayNamesFallBackAcrossReportPaths()
    {
        await _store.ReportAsync(Uid, new AccountRecordSource { Reference = "scan", DisplayName = " " }, [Report("摩拉", 80)]);
        await _store.ReportDeltaAsync(Uid, new AccountRecordSource { Reference = "delta", DisplayName = "" },
            [new InventoryDeltaReport { Name = "摩拉", Delta = 1, RecordedAt = _time.AddMinutes(1).ToString("O") }]);
        await _store.ReportFailureAsync(Uid, new AccountRecordSource { Reference = "failure", DisplayName = " " }, "摩拉", "测试失败");
        var reloaded = await new InventoryStore(_catalog, Path.Combine(_root, "records")).LoadAsync(Uid);
        foreach (var name in new[] { "scan", "delta", "failure" }) Assert.Equal(name, reloaded.Sources[name].DisplayName);
        await Assert.ThrowsAsync<ArgumentException>(async () => await _store.ReportAsync(Uid,
            new AccountRecordSource { Reference = "bad", DisplayName = new string('x', 1025) }, [Report("摩拉", 81, 2)]));
        Assert.False((await _store.LoadAsync(Uid)).Sources.ContainsKey("bad"));
    }

    /// <summary>无基准增量拒绝并保留事件；不得建立虚假的确认库存。</summary>
    [Fact]
    public async Task Delta_RequiresBaselineAndDeduplicatesTimestamp()
    {
        var delta = new InventoryDeltaReport { Name = "摩拉", Delta = -3, RecordedAt = _time.AddMinutes(1).ToString("O") };
        var rejected = await _store.ReportDeltaAsync(Uid, Source("delta"), [delta]);
        Assert.Single(rejected.Rejected);
        var data = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Empty(data.Items);
        Assert.Equal("Error", Assert.Single(data.Events).State);
        await _store.ReportDeltaAsync(Uid, Source("delta"), [delta]);
        Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events);
    }

    /// <summary>归档不解除去重；删除物品后可用同一输入重建，旧证据仍保留。</summary>
    [Fact]
    public async Task Delta_CanBeReimportedAfterItemDeletion()
    {
        var delta = new InventoryDeltaReport { Name = "摩拉", Delta = 3, RecordedAt = _time.AddMinutes(1).ToString("O") };
        await _store.ReportDeltaAsync(Uid, Source("delta"), [delta]);
        var original = Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events);
        await _store.UpdateEventAsync(Uid, original.Id, false, true);
        await _store.ReportDeltaAsync(Uid, Source("delta"), [delta]);
        Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events);
        await _store.DeleteItemAsync(Uid, "摩拉");
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var result = await _store.ReportDeltaAsync(Uid, Source("delta"), [delta]);
        Assert.Single(result.Accepted);
        var events = (await _store.LoadAsync(Uid)).Inventory.Events;
        Assert.Equal(2, events.Count);
        Assert.True(events[0].ItemDeleted);
        Assert.True(events[0].Archived);
        Assert.Equal("Pending", events[1].State);
        Assert.False(events[1].ItemDeleted);
        await _store.ReportDeltaAsync(Uid, Source("delta"), [delta]);
        Assert.Equal(2, (await _store.LoadAsync(Uid)).Inventory.Events.Count);
    }
    /// <summary>累加不改确认值，净零仍待核实；重复输入只接受一次。</summary>
    [Fact]
    public async Task Delta_PreservesConfirmedQuantityAndNetZeroRemainsPending()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var first = new InventoryDeltaReport { Name = "摩拉", Delta = 3, RecordedAt = _time.AddMinutes(1).ToString("O") };
        await _store.ReportDeltaAsync(Uid, Source("delta"), [first, first,
            new InventoryDeltaReport { Name = "摩拉", Delta = -3, RecordedAt = _time.AddMinutes(2).ToString("O") }]);
        var data = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(80L, data.Items["摩拉"][0].Quantity);
        Assert.Equal(2, data.Events.Count);
        Assert.All(data.Events, item => Assert.Equal("Pending", item.State));
        Assert.Equal(0L, data.Events.Sum(item => item.Quantity));
    }

    /// <summary>旧扫描拒绝且不消除增量；新扫描可核实，推算差异本身不计错。</summary>
    [Fact]
    public async Task Verification_RejectsOldScanThenArchivesOnConfirmation()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        await _store.ReportDeltaAsync(Uid, Source("delta"), [new InventoryDeltaReport
        { Name = "摩拉", Delta = -3, RecordedAt = _time.AddMinutes(2).ToString("O") }]);
        var old = Report("摩拉", 80, 3); old.VerifiedAt = _time.AddMinutes(1).ToString("O");
        Assert.Single((await _store.ReportAsync(Uid, Source("scan"), [old])).Rejected);
        var pending = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(80L, pending.Items["摩拉"][0].Quantity);
        Assert.Contains(pending.Events, item => item.Kind == "Delta" && item.State == "Pending");
        var fresh = Report("摩拉", 76, 4); fresh.VerifiedAt = fresh.RecordedAt;
        await _store.ReportAsync(Uid, Source("scan"), [fresh]);
        var workspace = await _store.LoadAsync(Uid);
        Assert.False(workspace.Sources["scan"].Attempts.Last().Failed);
        var verified = Assert.Single(workspace.Inventory.Events.Where(item => item.Kind == "Delta"));
        Assert.Equal("Completed", verified.State);
        Assert.Contains(verified.History, line => line.Contains("结果=错误"));
        await _store.UpdateEventAsync(Uid, verified.Id, true, false);
        Assert.True((await _store.LoadAsync(Uid)).Inventory.Events.Single(item => item.Id == verified.Id).KeepNotice);
        await _store.UpdateEventAsync(Uid, verified.Id, false, true);
        Assert.True((await _store.LoadAsync(Uid)).Inventory.Events.Single(item => item.Id == verified.Id).Archived);
    }

    /// <summary>来源达到五次红灯后提醒，重复批次不重复计数，后续健康记录解除提醒。</summary>
    [Fact]
    public async Task Verification_SourceThresholdAndDuplicateBatch()
    {
        await _store.ConfigureAsync(new AccountRecordsSettings { WarningTrackingEnabled = true });
        for (var i = 0; i < 5; i++)
            await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", -1, i)]);
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", -1, 4)]);
        Assert.Equal(5, (await _store.LoadAsync(Uid)).Sources["scan"].Attempts.Count);
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80, 5)]);
        Assert.Contains((await _store.LoadAsync(Uid)).Inventory.Events, item => item.Kind == "SourceWarning" && !item.Archived);
        for (var i = 6; i < 12; i++) await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80, i)]);
        Assert.DoesNotContain((await _store.LoadAsync(Uid)).Inventory.Events, item => item.Kind == "SourceWarning" && !item.Archived);
    }

    /// <summary>归档提醒保留错误证据，并仅使用归档之后的上报重新计算阈值。</summary>
    [Fact]
    public async Task Warning_AcknowledgeRestartsTrackingWithoutDeletingAttempts()
    {
        await _store.ConfigureAsync(new AccountRecordsSettings { WarningTrackingEnabled = true });
        for (var i = 0; i < 5; i++) await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", -1, i)]);
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80, 5)]);
        var warning = Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events.Where(item => item.Kind == "SourceWarning" && !item.Archived));
        await _store.UpdateEventAsync(Uid, warning.Id, false, true);
        var acknowledged = await _store.LoadAsync(Uid);
        Assert.Equal(5, acknowledged.Sources["scan"].Attempts.Count(item => item.Failed));
        Assert.NotNull(acknowledged.Sources["scan"].WarningTrackingSince);
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80, 6)]);
        Assert.DoesNotContain((await _store.LoadAsync(Uid)).Inventory.Events, item => item.Kind == "SourceWarning" && !item.Archived);
        for (var i = 7; i < 12; i++) await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", -1, i)]);
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80, 12)]);
        Assert.Contains((await _store.LoadAsync(Uid)).Inventory.Events, item => item.Kind == "SourceWarning" && !item.Archived);
    }

    /// <summary>关闭追踪归档来源提醒，但错误仍被记录；重新开启使用新周期。</summary>
    [Fact]
    public async Task Warning_DisableAndReenableKeepsErrorValidation()
    {
        await _store.ConfigureAsync(new AccountRecordsSettings { WarningTrackingEnabled = true });
        for (var i = 0; i < 5; i++) await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", -1, i)]);
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80, 5)]);
        var settings = (await _store.LoadAsync(Uid)).Settings;
        settings.WarningTrackingEnabled = false;
        await _store.ConfigureAsync(settings);
        Assert.DoesNotContain((await _store.LoadAsync(Uid)).Inventory.Events, item => item.Kind == "SourceWarning" && !item.Archived);
        Assert.NotEmpty((await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", -1, 6)])).Rejected);
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80, 7)]);
        Assert.DoesNotContain((await _store.LoadAsync(Uid)).Inventory.Events, item => item.Kind == "SourceWarning" && !item.Archived);
        settings.WarningTrackingEnabled = true;
        await _store.ConfigureAsync(settings);
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80, 8)]);
        Assert.DoesNotContain((await _store.LoadAsync(Uid)).Inventory.Events, item => item.Kind == "SourceWarning" && !item.Archived);
    }

    /// <summary>打开配置不启用追踪，编辑后的阈值与提示语在下一批上报生效。</summary>
    [Fact]
    public async Task WarningRules_CustomThresholdAndDefaultDisabled()
    {
        var path = await _store.GetWarningRulesPathAsync();
        Assert.False((await _store.GetSettingsAsync()).WarningTrackingEnabled);
        File.WriteAllText(path, JsonConvert.SerializeObject(new InventoryWarningRule
        { Source = "scan", WindowSize = 3, ErrorThreshold = 1, Message = "自定义来源提醒" }));
        await _store.ConfigureAsync(new AccountRecordsSettings { WarningTrackingEnabled = true });
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", -1)]);
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80, 1)]);
        Assert.Contains((await _store.LoadAsync(Uid)).Inventory.Events,
            item => item.Kind == "SourceWarning" && !item.Archived && item.Message == "自定义来源提醒");
        File.WriteAllText(path, "{}");
        await Assert.ThrowsAsync<InvalidDataException>(() => _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 81, 2)]));
        Assert.Equal(80, (await _store.LoadAsync(Uid)).Inventory.Items["摩拉"][0].Quantity);
    }

    /// <summary>人工反馈修改原上报结果，不制造新一次核实统计；人工来源不能上报增量。</summary>
    [Fact]
    public async Task ManualFeedback_MarksOriginalAttemptOnly()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var attemptId = (await _store.LoadAsync(Uid)).Inventory.Items["摩拉"][0].AttemptId;
        await _store.SetLocalQuantityAsync(Uid, "摩拉", 79, attemptId);
        var source = (await _store.LoadAsync(Uid)).Sources["scan"];
        Assert.True(Assert.Single(source.Attempts).Failed);
        Assert.Throws<ArgumentException>(() => { _ = _store.ReportDeltaAsync(Uid, Source("AccountRecords.SetLocalQuantityAsync"), []); });
    }

    /// <summary>开发增量导入必须先确认，不能因为文件有基准就绕过授权。</summary>
    [Fact]
    public async Task Exchange_DeveloperDeltasRequireExplicitConfirmation()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var package = await _store.ExportInventoryAsync(Uid);
        package["accounts"]![Uid]!["deltas"] = new Newtonsoft.Json.Linq.JArray(new Newtonsoft.Json.Linq.JObject
        { ["name"] = "摩拉", ["delta"] = -3, ["recordedAt"] = _time.AddMinutes(1).ToString("O"), ["source"] = "scan" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ImportInventoryAsync(package, "100000002", false));
        Assert.DoesNotContain("100000002", await _store.ListUidsAsync());
        await _store.ImportInventoryAsync(package, "100000002", false, true);
        Assert.Contains((await _store.LoadAsync("100000002")).Inventory.Events, item => item.Kind == "Delta" && item.State == "Pending");
    }

    /// <summary>仅删除本测试创建的目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    /// <summary>同一失败输入只保留一个事件和异常凭证。</summary>
    [Fact]
    public async Task RepeatedFailure_PreservesEventAndErrorId()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", -1)]);
        var first = (await _store.LoadAsync(Uid)).Inventory;
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", -1)]);
        var next = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(first.Errors["摩拉"].Id, next.Errors["摩拉"].Id);
        var item = Assert.Single(next.Events);
        Assert.Equal(2L, item.RepeatCount);
        Assert.Contains("EventCreated State=Error", item.History[0]);
        Assert.Contains("Quantity must be non-negative", item.History[0]);
    }

    /// <summary>归档保存原始说明，并将历史移出主库存文件。</summary>
    [Fact]
    public async Task Archive_PreservesMessageAndTrace()
    {
        await _store.ReportDeltaAsync(Uid, Source("delta"), [new InventoryDeltaReport { Name = "摩拉", Delta = 1, RecordedAt = _time.ToString("O") }]);
        var item = Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events);
        await _store.UpdateEventAsync(Uid, item.Id, false, true);
        var archived = Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events);
        Assert.Equal(item.Message, archived.Message);
        Assert.True(archived.Archived);
        var raw = JsonConvert.DeserializeObject<InventoryDocument>(File.ReadAllText(Path.Combine(_root, "records", Uid, "inventory.json")))!;
        Assert.Empty(raw.Events);
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "records", Uid, "events"), "*.json"));
    }

    /// <summary>字典中合法 JSON 的空条目应报字段错误，不出现空引用。</summary>
    [Fact]
    public async Task Dictionary_NullEntryReportsFileAndField()
    {
        var directory = Path.Combine(_root, "records");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "item-overrides.json"), "{\"测试物品\":null}");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => _store.LoadAsync(Uid));
        Assert.Contains("item-overrides.json", error.Message);
        Assert.Contains("测试物品", error.Message);
    }

    /// <summary>增量不按来源分组打乱时间，先增加再扣除允许成功。</summary>
    [Fact]
    public async Task Exchange_DeltasFollowTimeAcrossSources()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 0)]);
        var package = await _store.ExportInventoryAsync(Uid);
        package["sources"]!["spend"] = Newtonsoft.Json.Linq.JObject.FromObject(new { reference = "spend", displayName = "spend" });
        package["sources"]!["gain"] = Newtonsoft.Json.Linq.JObject.FromObject(new { reference = "gain", displayName = "gain" });
        package["accounts"]![Uid]!["deltas"] = new Newtonsoft.Json.Linq.JArray(
            Newtonsoft.Json.Linq.JObject.FromObject(new { name = "摩拉", delta = -5, source = "spend", recordedAt = _time.AddMinutes(2).ToString("O") }),
            Newtonsoft.Json.Linq.JObject.FromObject(new { name = "摩拉", delta = 10, source = "gain", recordedAt = _time.AddMinutes(1).ToString("O") }));
        var result = await _store.ImportInventoryAsync(package, Uid, false, true);
        Assert.Empty(result.Rejected);
        Assert.Equal(5L, (await _store.LoadAsync(Uid)).Inventory.Events.Sum(item => item.Quantity));
    }

    /// <summary>UID 严格校验位数，不保留短编号例外。</summary>
    [Theory]
    [InlineData("123456789", true)]
    [InlineData("1800000000", true)]
    [InlineData("100000001", true)]
    [InlineData("100000002", true)]
    [InlineData("00000", false)]
    [InlineData("00001", false)]
    [InlineData("12345678", false)]
    [InlineData("12345678901", false)]
    [InlineData("012345678", false)]
    [InlineData("123456789\n", false)]
    public void Uid_ValidatesLength(string uid, bool valid) => Assert.Equal(valid, InventoryStore.IsValidUid(uid));
    /// <summary>来源集合显式空值不能进入业务逻辑。</summary>
    [Fact]
    public async Task Dictionary_NullAttemptsReportsField()
    {
        var directory = Path.Combine(_root, "records");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "sources.json"), "{\"scan\":{\"Reference\":\"scan\",\"DisplayName\":\"scan\",\"Attempts\":null}}");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => _store.LoadAsync(Uid));
        Assert.Contains("Attempts", error.Message);
    }
    /// <summary>保留上限只清理归档历史，并保留增量去重及活动事件。</summary>
    [Fact]
    public async Task ArchiveRetention_PreservesPendingAndDeduplication()
    {
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        var path = Path.Combine(_root, "records", Uid, "inventory.json");
        var data = JsonConvert.DeserializeObject<InventoryDocument>(File.ReadAllText(path))!;
        for (var index = 0; index < 101; index++)
            data.Events.Add(new InventoryEvent { Name = "摩拉", Kind = "Delta", Source = "delta", Quantity = 1,
                State = "Completed", Archived = true, RecordedAt = _time.AddSeconds(index + 1), EndedAt = _time.AddMinutes(index + 1) });
        data.Events.Add(new InventoryEvent { Name = "甜甜花", State = "Pending", Message = "pending" });
        File.WriteAllText(path, JsonConvert.SerializeObject(data));
        await _store.ConfigureAsync(new AccountRecordsSettings { ArchivedEventLimit = 100 });
        var loaded = (await _store.LoadAsync(Uid)).Inventory;
        Assert.Equal(100, loaded.Events.Count(item => item.Archived));
        Assert.Single(loaded.Events.Where(item => !item.Archived));
        var result = await _store.ReportDeltaAsync(Uid, Source("delta"),
            [new InventoryDeltaReport { Name = "摩拉", Delta = 1, RecordedAt = _time.AddSeconds(1).ToString("O") }]);
        Assert.Empty(result.Accepted);
        Assert.Empty(result.Rejected);
        await _store.DeleteItemAsync(Uid, "摩拉");
        await _store.ReportAsync(Uid, Source("scan"), [Report("摩拉", 80)]);
        result = await _store.ReportDeltaAsync(Uid, Source("delta"),
            [new InventoryDeltaReport { Name = "摩拉", Delta = 1, RecordedAt = _time.AddSeconds(1).ToString("O") }]);
        Assert.Single(result.Accepted);
    }
    /// <summary>重复错误不计为新增；归档后重复不再提示未处理异常。</summary>
    [Fact]
    public async Task DuplicateDeltaError_ReportsExistingAndArchivedSeparately()
    {
        var report = new InventoryDeltaReport { Name = "摩拉", Delta = 1, RecordedAt = _time.ToString("O") };
        await _store.ReportDeltaAsync(Uid, Source("delta"), [report]);
        var repeated = await _store.ReportDeltaAsync(Uid, Source("delta"), [report]);
        Assert.Empty(repeated.Rejected);
        Assert.Equal(1, repeated.DuplicateCount);
        Assert.Equal(1, repeated.DuplicateErrorCount);
        var item = Assert.Single((await _store.LoadAsync(Uid)).Inventory.Events);
        await _store.UpdateEventAsync(Uid, item.Id, false, true);
        repeated = await _store.ReportDeltaAsync(Uid, Source("delta"), [report]);
        Assert.Equal(1, repeated.DuplicateCount);
        Assert.Equal(0, repeated.DuplicateErrorCount);
        Assert.Empty(repeated.Rejected);
    }
}
