using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.ViewModel.Controls;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>隔离验证快照格式、只读查询和独立窗口布局，不启动主程序。</summary>
public sealed class InventorySnapshotTests : IDisposable
{
    /// <summary>临时文件根。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "InventorySnapshotTests", Guid.NewGuid().ToString("N"));
    /// <summary>独立账号。</summary>
    private const string Uid = "123456789";
    /// <summary>简化物品字典。</summary>
    private readonly InventoryCatalog _catalog;
    /// <summary>隔离存储。</summary>
    private readonly InventoryStore _store;
    /// <summary>创建测试字典。</summary>
    public InventorySnapshotTests()
    {
        Directory.CreateDirectory(_root);
        var csv = Path.Combine(_root, "items.csv");
        File.WriteAllText(csv, "item_name,material_type,page\n甜甜花,植物,材料\n铁块,矿物,材料\n");
        _catalog = new(csv);
        _store = new(_catalog, Path.Combine(_root, "records"));
    }

    /// <summary>通过实际自动快照入口生成上一记录日的数据。</summary>
    private string CreateSnapshot(string name = "甜甜花")
    {
        var previous = DateTimeOffset.UtcNow.AddDays(-1);
        var document = new InventoryDocument { Uid = Uid, ModifiedAt = previous,
            Items = new() { [name] = [new() { Quantity = 12, RecordedAt = previous, Source = "test" }] } };
        typeof(InventoryStore).GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(_store, [document, new AccountRecordsSettings()]);
        return InventoryStore.RecordDay(previous).ToString("yyyy-MM-dd");
    }

    /// <summary>UID 改名不改写历史账号标识，仍能查看并删除历史库存；需求引用的分类必须保留。</summary>
    [Fact]
    public async Task RenamedAccountCanReadAndDeleteSnapshotInventoryWithoutLosingDemandDefinition()
    {
        await _store.AddUidAsync(Uid);
        const string name = "自定义材料";
        await _store.DefineAsync(name, "植物");
        await _store.SetDemandAsync(Uid, name, 20, null);
        var day = CreateSnapshot(name);
        const string renamed = "987654321";
        await _store.RenameUidAsync(Uid, renamed);
        Assert.Equal(12, (await _store.ReadSnapshotAsync(renamed, day)).Inventory.Items[name][0].Quantity);

        await _store.DeleteItemAsync(renamed, name);
        var package = await InventoryStore.ReadExchangeAsync(Path.Combine(_root, "records", renamed, "snapshots", day + ".json"));
        Assert.Null(package["accounts"]![Uid]!["inventory"]![name]);
        Assert.NotNull(package["accounts"]![Uid]!["demands"]!["items"]![name]);
        Assert.Equal("植物", (string?)package["itemDefinitions"]![name]!["category"]);
        Assert.Empty((await _store.ReadSnapshotAsync(renamed, day)).Inventory.Items);
    }

    /// <summary>当前快照通过真实导入校验，读取不写入任何记录，并拒绝非法日期路径。</summary>
    [Fact]
    public async Task SnapshotUsesExchangeWithoutWriting()
    {
        var day = CreateSnapshot();
        var path = Path.Combine(_root, "records", Uid, "snapshots", day + ".json");
        var package = await InventoryStore.ReadExchangeAsync(path);
        Assert.Equal("BetterGI.AccountRecords", (string?)package["format"]);
        Assert.Equal(12, (long)package["accounts"]![Uid]!["inventory"]!["甜甜花"]![0]!["quantity"]!);
        var before = File.ReadAllText(path);
        var workspace = await _store.ReadSnapshotAsync(Uid, day);
        Assert.Equal(12, workspace.Inventory.Items["甜甜花"][0].Quantity);
        Assert.Empty(workspace.Inventory.Events);
        Assert.Equal(before, File.ReadAllText(path));
        Assert.False(File.Exists(Path.Combine(_root, "records", Uid, "inventory.json")));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.ReadSnapshotAsync(Uid, "../inventory"));
    }

    /// <summary>快照保存实际记录时的需求、独立置顶和归档，并可随库存一并恢复。</summary>
    [Fact]
    public async Task SnapshotRestoresDemandDocumentAndCapturesCurrentState()
    {
        await _store.SetDemandAsync(Uid, "甜甜花", 0, null);
        var initial = await _store.ReadDemandsAsync(Uid);
        await _store.ConfirmDemandAsync(Uid, "甜甜花", initial.Items["甜甜花"].Revision);
        var confirmed = await _store.ReadDemandsAsync(Uid);
        await _store.ArchiveDemandsAsync(Uid, new Dictionary<string, string> { ["甜甜花"] = confirmed.Items["甜甜花"].Revision });
        await _store.SetDemandAsync(Uid, "铁块", 20, null);
        await _store.SetDemandPriorAsync(Uid, "铁块", true);
        var before = await _store.ReadDemandsAsync(Uid);
        var day = CreateSnapshot();
        var path = Path.Combine(_root, "records", Uid, "snapshots", day + ".json");
        var package = await InventoryStore.ReadExchangeAsync(path);
        Assert.NotNull(package["accounts"]![Uid]!["demandStates"]!["铁块"]);
        Assert.Equal(20, (long)package["accounts"]![Uid]!["demands"]!["items"]!["铁块"]!["targetQuantity"]!);
        Assert.Null(package["accounts"]![Uid]!["demands"]!["items"]!["铁块"]!["lastInventoryEventId"]);
        Assert.NotNull(package["sources"]![MaterialDemand.ManualSourceReference]);
        const string otherUid = "987654321";
        await _store.ImportInventoryAsync(package, otherUid, true);
        var restored = await _store.ReadDemandsAsync(otherUid);
        Assert.Equal(before.Items["铁块"].Revision, restored.Items["铁块"].Revision);
        Assert.Equal(before.Items["铁块"].CreatedAt, restored.Items["铁块"].CreatedAt);
        Assert.Equal(before.Items["铁块"].Source, restored.Items["铁块"].Source);
        Assert.Null(restored.Items["铁块"].ConfirmedAt);
        Assert.Contains("铁块", restored.Prior);
        Assert.Single(restored.History);
        Assert.Equal(before.History[0].Demand.ConfirmedAt, restored.History[0].Demand.ConfirmedAt);
        // 当天再次检查不因修改需求而覆盖既有快照。
        await _store.SetDemandAsync(Uid, "铁块", 30, before.Items["铁块"].Revision);
        var saved = File.ReadAllText(path);
        CreateSnapshot();
        Assert.Equal(saved, File.ReadAllText(path));
    }

    /// <summary>无需求节点不清空本地需求；显式空文档恢复空状态，非法目标在库存写入前拒绝。</summary>
    [Fact]
    public async Task ImportPreservesAbsentDemandsAndRejectsInvalidDemandsBeforeWriting()
    {
        await _store.SetDemandAsync(Uid, "铁块", 0, null);
        var day = CreateSnapshot();
        var package = await InventoryStore.ReadExchangeAsync(Path.Combine(_root, "records", Uid, "snapshots", day + ".json"));
        var account = (JObject)package["accounts"]![Uid]!;
        var demandBackup = account["demands"]!.DeepClone();
        account.Remove("demands");
        await _store.ImportInventoryAsync(package, Uid, true);
        Assert.Equal(0, (await _store.ReadDemandsAsync(Uid)).Items["铁块"].TargetQuantity);
        account["demands"] = demandBackup;
        account["demands"]!["items"]!["铁块"]!["targetQuantity"] = -1;
        const string otherUid = "987654321";
        await Assert.ThrowsAsync<InvalidDataException>(() => _store.ImportInventoryAsync(package, otherUid, true));
        Assert.False(File.Exists(Path.Combine(_root, "records", otherUid, "inventory.json")));
        var invalidDemand = (JObject)account["demands"]!["items"]!["铁块"]!;
        invalidDemand.Remove("targetQuantity");
        await Assert.ThrowsAsync<InvalidDataException>(() => _store.ImportInventoryAsync(package, otherUid, true));
        invalidDemand["targetQuantity"] = 0;
        invalidDemand["createdAt"] = "2026-01-01T00:00:00";
        await Assert.ThrowsAsync<InvalidDataException>(() => _store.ImportInventoryAsync(package, otherUid, true));
        account["demands"]!["items"] = new JObject();
        await _store.ImportInventoryAsync(package, Uid, true);
        Assert.Empty((await _store.ReadDemandsAsync(Uid)).Items);
    }

    /// <summary>默认上一日、缺失物品不显示零、损坏文件不阻止其他历史、XAML 独立布局。</summary>
    [Fact]
    public Task BrowserLoadsOnSelectionAndInstantiatesWindow() => RunSta(async () =>
    {
        var day = CreateSnapshot();
        var directory = Path.Combine(_root, "records", Uid, "snapshots");
        var oldDay = InventoryStore.RecordDay(DateTimeOffset.UtcNow.AddDays(-2)).ToString("yyyy-MM-dd");
        File.WriteAllText(Path.Combine(directory, oldDay + ".json"), "invalid json");
        var vm = new AccountRecordsSnapshotsViewModel(_store, _catalog, new InventoryIconCache(Path.Combine(_root, "icons")), Uid);
        await vm.InitializeAsync();
        Assert.Equal(day, vm.SelectedDate);
        Assert.Single(vm.Items.Cast<object>());
        Assert.Empty(vm.History);
        Assert.DoesNotContain("失败", vm.Status);
        var dialog = new AccountRecordsSnapshotsDialog(vm);
        dialog.ApplyTemplate();
        var content = Assert.IsType<Grid>(dialog.Content);
        content.Measure(new Size(1060, 720));
        content.Arrange(new Rect(0, 0, 1060, 720));
        content.UpdateLayout();
        Assert.True(content.ActualWidth > 0);
        vm.Mode = 1;
        vm.SelectedItem = vm.Catalog.Cast<SnapshotCatalogItem>().Single(item => item.Name == "铁块");
        await vm.LoadSelectionAsync();
        Assert.Equal(2, vm.History.Count);
        Assert.Null(vm.History[0].Quantity);
        Assert.Equal("无记录", vm.History[0].QuantityText);
        Assert.NotEmpty(vm.History[1].Error);
        vm.SelectedItem = vm.Catalog.Cast<SnapshotCatalogItem>().Single(item => item.Name == "甜甜花");
        await vm.LoadSelectionAsync();
        Assert.Equal(12, vm.History[0].Quantity);
        dialog.Close(); vm.Close();
    });

    /// <summary>单独 STA 调度器运行视图测试。</summary>
    private static Task RunSta(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }

    /// <summary>只清理本测试的随机临时目录。</summary>
    public void Dispose() => Directory.Delete(_root, true);

    /// <summary>降低上限时删除失败，同一上限再次确认仍能完成清理，过期确认继续被拒绝。</summary>
    [Fact]
    public async Task SnapshotLimit_RetriesCleanupAfterLockedFileIsReleased()
    {
        await _store.SetSnapshotLimitAsync(3, (await _store.GetSettingsAsync()).SnapshotLimit);
        var directory = Path.Combine(Path.Combine(_root, "records"), Uid, "snapshots");
        Directory.CreateDirectory(directory);
        foreach (var day in new[] { "2026-01-01", "2026-01-02", "2026-01-03" })
            File.WriteAllText(Path.Combine(directory, day + ".json"), "{}");
        using (var locked = new FileStream(Path.Combine(directory, "2026-01-01.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<IOException>(() => _store.SetSnapshotLimitAsync(1, 3));
        Assert.Equal(1, (await _store.GetSettingsAsync()).SnapshotLimit);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.SetSnapshotLimitAsync(2, 3));
        await _store.SetSnapshotLimitAsync(1, 1);
        Assert.Equal("2026-01-03.json", Path.GetFileName(Assert.Single(Directory.GetFiles(directory, "*.json"))));
    }

    /// <summary>通过公开导入入口补充背包页，再导出含需求的备份，应可重新导入。</summary>
    [Fact]
    public async Task DemandExport_ImportedPageOverrideMustRoundTrip()
    {
        // 原始目录不含背包页，验证导入的补充值确实随备份保留。
        var csv = Path.Combine(_root, "exchange-items.csv");
        File.WriteAllText(csv, "item_name,material_type,page\n甜甜花,植物,\n");
        var catalog = new InventoryCatalog(csv);
        var store = new InventoryStore(catalog, Path.Combine(_root, "exchange-records"));
        await store.ReportAsync(Uid, new AccountRecordSource { Reference = "scan", DisplayName = "扫描" },
            [new InventoryReport { Name = "甜甜花", Quantity = 10, RecordedAt = DateTimeOffset.UtcNow.AddHours(-1).ToString("O") }]);
        await store.SetDemandAsync(Uid, "甜甜花", 20, null);
        var incoming = await store.ExportInventoryAsync(Uid);
        incoming["itemDefinitions"]!["甜甜花"] = new JObject
        {
            ["category"] = "植物", ["page"] = "材料", ["comment"] = "补充背包页"
        };
        await store.ImportInventoryAsync(incoming, Uid, true);
        Assert.Equal("材料", (await store.GetItemClassificationsAsync()).Pages["甜甜花"]);

        var exported = await store.ExportInventoryAsync(Uid);
        var destination = new InventoryStore(catalog, Path.Combine(_root, "destination"));
        await destination.ImportInventoryAsync(exported, Uid, true);
        Assert.Equal(10, (await destination.ReadAsync(Uid)).Inventory.Items["甜甜花"][0].Quantity);
        Assert.Equal(20, (await destination.ReadDemandsAsync(Uid)).Items["甜甜花"].TargetQuantity);
        Assert.Equal("材料", (await destination.GetItemClassificationsAsync()).Pages["甜甜花"]);
    }

}
