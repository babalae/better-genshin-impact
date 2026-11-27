using System.ComponentModel;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using BetterGenshinImpact.View.Controls;
using BetterGenshinImpact.ViewModel.Controls;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>需求状态、账号隔离与真实 WPF 模板验证；不启动主程序。</summary>
public sealed class MaterialDemandTests : IDisposable
{
    /// <summary>库存红灯覆盖达标灯，问题消除后恢复原需求，普通提醒不覆盖。</summary>
    [Fact]
    public void InventoryErrorOverridesDemandLightUntilResolved()
    {
        var row = new AccountRecordsDemandRow
        {
            Item = new AccountRecordsInventoryRow { Name = "摩拉", Event = new InventoryEvent { State = "Error", Message = "数量冲突" } },
            Demand = new MaterialDemand { TargetQuantity = 1 },
            Progress = new MaterialDemandProgress(MaterialDemandState.Completed, "实测数量已达标", "js", 1)
        };
        Assert.Equal(0, row.StateRank);
        Assert.False(row.CanArchive);
        Assert.False(row.CanConfirm);
        Assert.Contains("库存记录", row.StatusText);
        Assert.Contains("数量冲突", row.NoticeDetails);
        row.ApplySnapshot(new AccountRecordsDemandRow
        {
            Item = new AccountRecordsInventoryRow { Name = "摩拉", Event = new InventoryEvent { State = "Warning" } },
            Demand = row.Demand, Progress = row.Progress
        });
        Assert.Equal(3, row.StateRank);
        Assert.True(row.CanArchive);
        row.Item.Error = new InventoryError { Message = "错误堆积" };
        Assert.Equal(0, row.StateRank);
        row.Demand = null;
        row.Progress = null;
        Assert.Equal(5, row.StateRank);
    }

    /// <summary>旧界面的确认与归档不能绕过后来出现的库存错误。</summary>
    [Fact]
    public async Task InventoryErrorsBlockDemandConfirmationAndArchive()
    {
        await _store.SetDemandAsync(Uid, "摩拉", 0, null);
        var demand = (await _store.ReadDemandsAsync(Uid)).Items["摩拉"];
        await _store.ConfirmDemandAsync(Uid, "摩拉", demand.Revision);
        demand = (await _store.ReadDemandsAsync(Uid)).Items["摩拉"];
        await _store.ReportFailureAsync(Uid, _source, "摩拉", "读取失败");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ConfirmDemandAsync(Uid, "摩拉", demand.Revision));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ArchiveDemandsAsync(Uid,
            new Dictionary<string, string> { ["摩拉"] = demand.Revision }));
        Assert.Equal(demand.Revision, (await _store.ReadDemandsAsync(Uid)).Items["摩拉"].Revision);
    }

    /// <summary>隔离的临时根目录。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MaterialDemandTests", Guid.NewGuid().ToString("N"));
    /// <summary>测试账号。</summary>
    private const string Uid = "100000001";
    /// <summary>复用库存的 JS 来源。</summary>
    private readonly AccountRecordSource _source = new() { Reference = "js.collect", DisplayName = "采集脚本" };
    /// <summary>独立测试目录的物品字典。</summary>
    private readonly InventoryCatalog _catalog;
    /// <summary>真实库存与需求存储。</summary>
    private readonly InventoryStore _store;

    /// <summary>创建不依赖外部资源包或用户配置的字典。</summary>
    public MaterialDemandTests()
    {
        Directory.CreateDirectory(_root);
        var csv = Path.Combine(_root, "items.csv");
        File.WriteAllText(csv, "item_name,material_type,page\n甜甜花,植物,材料\n铁块,矿物,材料\n苹果,食材,食物\n");
        _catalog = new InventoryCatalog(csv);
        _store = new InventoryStore(_catalog, Path.Combine(_root, "records"));
    }

    /// <summary>零目标可以保存并由已知库存确认，负目标被拒绝且不覆盖现有需求。</summary>
    [Fact]
    public async Task ZeroTargetIsAcceptedAndNegativeTargetPreservesDemand()
    {
        await Report(0, DateTimeOffset.UtcNow.AddMinutes(-1));
        await _store.SetDemandAsync(Uid, "甜甜花", 0, null);
        var demand = (await _store.ReadDemandsAsync(Uid)).Items["甜甜花"];
        Assert.Equal(0, demand.TargetQuantity);
        Assert.Equal(MaterialDemandState.Completed, (await Progress()).State);
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SetDemandAsync(Uid, "甜甜花", -1, demand.Revision));
        Assert.Equal(demand.Revision, (await _store.ReadDemandsAsync(Uid)).Items["甜甜花"].Revision);
    }

    /// <summary>归档保留完成证据和库存，重建同名需求不复用旧需求或历史。</summary>
    [Fact]
    public async Task ArchivePreservesEvidenceAndAllowsNewDemand()
    {
        await Report(5, DateTimeOffset.UtcNow.AddMinutes(-1));
        await _store.SetDemandAsync(Uid, "甜甜花", 10, null);
        var demand = (await _store.ReadDemandsAsync(Uid)).Items["甜甜花"];
        var completedAt = demand.CreatedAt.AddSeconds(1);
        await Report(10, completedAt);
        var revisions = new Dictionary<string, string> { ["甜甜花"] = demand.Revision };
        await _store.ArchiveDemandsAsync(Uid, revisions);
        var document = await _store.ReadDemandsAsync(Uid);
        Assert.Empty(document.Items);
        var archive = Assert.Single(document.History);
        Assert.Equal(10, archive.Demand.TargetQuantity);
        Assert.Equal("Quantity", archive.ConfirmationMethod);
        Assert.Equal(completedAt, archive.CompletedAt);
        Assert.Equal(_source.Reference, archive.Source);
        Assert.Equal(_source.DisplayName, archive.SourceDisplayName);
        Assert.Equal(10, (await _store.ReadAsync(Uid)).Inventory.Items["甜甜花"][0].Quantity);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ArchiveDemandsAsync(Uid, revisions));
        await _store.SetDemandAsync(Uid, "甜甜花", 20, null);
        Assert.Equal(MaterialDemandState.Unmet, (await Progress()).State);
        Assert.Single((await _store.ReadDemandsAsync(Uid)).History);
    }

    /// <summary>批量归档全量核实，未完成或过期版本使整批保持原样。</summary>
    [Fact]
    public async Task ArchiveRejectsIncompleteAndStaleBatchAtomically()
    {
        await _store.SetDemandAsync(Uid, "甜甜花", 10, null);
        await _store.SetDemandAsync(Uid, "铁块", 5, null);
        var original = (await _store.ReadDemandsAsync(Uid)).Items["甜甜花"];
        await _store.ConfirmDemandAsync(Uid, "甜甜花", original.Revision);
        var demands = await _store.ReadDemandsAsync(Uid);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ArchiveDemandsAsync(Uid,
            demands.Items.ToDictionary(pair => pair.Key, pair => pair.Value.Revision)));
        Assert.Equal(2, (await _store.ReadDemandsAsync(Uid)).Items.Count);
        Assert.Empty((await _store.ReadDemandsAsync(Uid)).History);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ArchiveDemandsAsync(Uid,
            new Dictionary<string, string> { ["甜甜花"] = original.Revision }));
        await _store.ArchiveDemandsAsync(Uid, new Dictionary<string, string> { ["甜甜花"] = demands.Items["甜甜花"].Revision });
        var archived = Assert.Single((await _store.ReadDemandsAsync(Uid)).History);
        Assert.Equal("Manual", archived.ConfirmationMethod);
        Assert.Equal(demands.Items["甜甜花"].ConfirmedAt, archived.CompletedAt);
    }

    /// <summary>关闭增量达标开关后不能使用旧绿灯归档；开启时记录增量确认方式。</summary>
    [Fact]
    public async Task ArchiveRechecksDeltaSetting()
    {
        await Report(5, DateTimeOffset.UtcNow.AddMinutes(-1));
        await _store.SetDemandAsync(Uid, "甜甜花", 10, null);
        var demand = (await _store.ReadDemandsAsync(Uid)).Items["甜甜花"];
        await _store.ReportDeltaAsync(Uid, _source, [new() { Name = "甜甜花", Delta = 5, RecordedAt = demand.CreatedAt.AddSeconds(1).ToString("O") }]);
        var revisions = new Dictionary<string, string> { ["甜甜花"] = demand.Revision };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.ArchiveDemandsAsync(Uid, revisions));
        await _store.UpdateSettingsAsync(settings => settings.UseDeltaForDemands = true);
        await _store.ArchiveDemandsAsync(Uid, revisions);
        Assert.Equal("Delta", Assert.Single((await _store.ReadDemandsAsync(Uid)).History).ConfirmationMethod);
    }

    /// <summary>未达标保持灰灯；启用增量判断后可推算达标，后续实测替代推算依据。</summary>
    [Fact]
    public async Task QuantityChangesDriveProgressAndReuseInventorySource()
    {
        await _store.UpdateSettingsAsync(settings => settings.UseDeltaForDemands = true);
        await Report(5, DateTimeOffset.UtcNow.AddMinutes(-1));
        await _store.SetDemandAsync(Uid, "甜甜花", 10, null);
        var demand = (await _store.ReadDemandsAsync(Uid)).Items["甜甜花"];
        Assert.Equal(MaterialDemandState.Unmet, (await Progress()).State);
        await Report(5, demand.CreatedAt.AddSeconds(1));
        Assert.Equal(MaterialDemandState.Unmet, (await Progress()).State);
        await _store.ReportDeltaAsync(Uid, _source, [new() { Name = "甜甜花", Delta = 5, RecordedAt = demand.CreatedAt.AddSeconds(2).ToString("O") }]);
        var possible = await Progress();
        Assert.Equal(MaterialDemandState.Completed, possible.State);
        Assert.Contains("增量推算", possible.Message);
        Assert.Equal("js.collect", possible.Source);
        Assert.Equal(10m, possible.EstimatedQuantity);
        Assert.Equal(5, (await _store.ReadAsync(Uid)).Inventory.Items["甜甜花"][0].Quantity);
        await Report(10, demand.CreatedAt.AddSeconds(3));
        Assert.Equal(MaterialDemandState.Completed, (await Progress()).State);
        Assert.Null((await _store.ReadDemandsAsync(Uid)).Items["甜甜花"].ConfirmedAt);
    }

    /// <summary>数量变化、回到原值或上报失败都不会让未达标需求变黄，需求读取无需归档历史。</summary>
    [Fact]
    public async Task BelowTargetStaysGreyAfterQuantityChangesWithoutLoadingHistory()
    {
        await Report(5, DateTimeOffset.UtcNow.AddMinutes(-1));
        await _store.SetDemandAsync(Uid, "甜甜花", 20, null);
        var demand = (await _store.ReadDemandsAsync(Uid)).Items["甜甜花"];
        await Report(-1, demand.CreatedAt.AddSeconds(1));
        Assert.Equal(MaterialDemandState.Unmet, (await Progress()).State);
        await Report(6, demand.CreatedAt.AddSeconds(2));
        Assert.Equal(MaterialDemandState.Unmet, (await Progress()).State);
        await Report(5, demand.CreatedAt.AddSeconds(3));
        Assert.Equal(MaterialDemandState.Unmet, (await Progress()).State);
        Assert.Equal("未达标", (await Progress()).Message);
        // 库存仍保留历史，但需求不再为判断执行经过加载它。
        var workspace = await _store.ReadAsync(Uid);
        Assert.Contains(workspace.Inventory.Events, item => item.Kind == "Report" && item.Archived && item.Quantity == 6);
        var (current, _) = await _store.ReadDemandWorkspaceAsync(Uid);
        Assert.DoesNotContain(current.Inventory.Events, item => item.Archived);
    }

    /// <summary>无确认库存时不把增量当作总量；手工确认不修改库存，旧版本不能覆盖新需求。</summary>
    [Fact]
    public async Task ManualConfirmationIsIndependentAndRejectsStaleEdits()
    {
        await _store.SetDemandAsync(Uid, "铁块", 5, null);
        var demand = (await _store.ReadDemandsAsync(Uid)).Items["铁块"];
        await _store.ReportDeltaAsync(Uid, _source, [new() { Name = "铁块", Delta = 5, RecordedAt = demand.CreatedAt.AddSeconds(1).ToString("O") }]);
        var workspace = await _store.ReadAsync(Uid);
        var progress = MaterialDemandPolicy.Evaluate(demand, null, workspace.Inventory.Events, useDelta: true);
        Assert.Equal(MaterialDemandState.Unmet, progress.State);
        Assert.Null(progress.EstimatedQuantity);
        await _store.ConfirmDemandAsync(Uid, "铁块", demand.Revision);
        var confirmed = (await _store.ReadDemandsAsync(Uid)).Items["铁块"];
        Assert.Equal(MaterialDemandState.Completed, MaterialDemandPolicy.Evaluate(confirmed, null, workspace.Inventory.Events).State);
        Assert.False((await _store.ReadAsync(Uid)).Inventory.Items.ContainsKey("铁块"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.DeleteDemandAsync(Uid, "铁块", demand.Revision));
        await _store.SetDemandAsync(Uid, "铁块", 6, confirmed.Revision);
        Assert.Null((await _store.ReadDemandsAsync(Uid)).Items["铁块"].ConfirmedAt);
    }

    /// <summary>需求随账号目录改名；清空库存保留需求，删除账号删除需求。</summary>
    [Fact]
    public async Task DemandLifecycleFollowsAccountButNotInventoryClear()
    {
        await _store.SetDemandAsync(Uid, "甜甜花", 10, null);
        await _store.ClearAsync(Uid, false, false);
        Assert.Single((await _store.ReadDemandsAsync(Uid)).Items);
        await _store.RenameUidAsync(Uid, "100000002");
        Assert.Empty((await _store.ReadDemandsAsync(Uid)).Items);
        Assert.Single((await _store.ReadDemandsAsync("100000002")).Items);
        await _store.ClearAsync("100000002", true, true);
        Assert.Empty((await _store.ReadDemandsAsync("100000002")).Items);
    }

    /// <summary>增量判断默认关闭；开启允许推算绿灯，关闭重新按实测判断，但不改变库存事件。</summary>
    [Fact]
    public async Task DeltaSettingRecomputesDemandWithoutChangingInventory()
    {
        Assert.False((await _store.GetSettingsAsync()).UseDeltaForDemands);
        await Report(5, DateTimeOffset.UtcNow.AddMinutes(-1));
        await _store.SetDemandAsync(Uid, "甜甜花", 10, null);
        var demand = (await _store.ReadDemandsAsync(Uid)).Items["甜甜花"];
        await _store.ReportDeltaAsync(Uid, _source, [new() { Name = "甜甜花", Delta = 5, RecordedAt = demand.CreatedAt.AddSeconds(1).ToString("O") }]);
        Assert.Equal(MaterialDemandState.Pending, (await Progress()).State);
        Assert.Contains("可能达标", (await Progress()).Message);
        await _store.UpdateSettingsAsync(settings => settings.UseDeltaForDemands = true);
        Assert.Equal(MaterialDemandState.Completed, (await Progress()).State);
        Assert.Contains("增量推算", (await Progress()).Message);
        await _store.UpdateSettingsAsync(settings => settings.UseDeltaForDemands = false);
        Assert.Equal(MaterialDemandState.Pending, (await Progress()).State);
        Assert.Contains("可能达标", (await Progress()).Message);
        Assert.Contains((await _store.ReadAsync(Uid)).Inventory.Events, item => item.Kind == "Delta" && item.State == "Pending");
    }

    /// <summary>待核实增量不受目标创建时间限制，只有推算达标才提示可能完成。</summary>
    [Theory]
    [InlineData(false, 10, 5, MaterialDemandState.Pending)]
    [InlineData(true, 10, 5, MaterialDemandState.Completed)]
    [InlineData(false, 11, 5, MaterialDemandState.Unmet)]
    [InlineData(true, 11, 5, MaterialDemandState.Unmet)]
    [InlineData(false, 5, -1, MaterialDemandState.Unmet)]
    [InlineData(true, 5, -1, MaterialDemandState.Unmet)]
    public async Task PreexistingDeltaUsesCurrentEstimate(bool useDelta, long target, long delta, MaterialDemandState expected)
    {
        await _store.UpdateSettingsAsync(settings => settings.UseDeltaForDemands = useDelta);
        await Report(5, DateTimeOffset.UtcNow.AddMinutes(-2));
        await _store.ReportDeltaAsync(Uid, _source, [new() { Name = "甜甜花", Delta = delta,
            RecordedAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O") }]);
        await _store.SetDemandAsync(Uid, "甜甜花", target, null);
        var progress = await Progress();
        Assert.Equal(expected, progress.State);
        Assert.Equal(5m + delta, progress.EstimatedQuantity);
        Assert.Equal(_source.Reference, progress.Source);
        if (expected == MaterialDemandState.Unmet) Assert.Equal("未达标", progress.Message);
        if (expected == MaterialDemandState.Pending) Assert.Equal("可能达标，待核实", progress.Message);
    }

    /// <summary>已有库存满足新目标时立即达标，并可按实测依据归档。</summary>
    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    public async Task ExistingQuantityCompletesNewDemand(long quantity)
    {
        await Report(quantity, DateTimeOffset.UtcNow.AddMinutes(-1));
        await _store.SetDemandAsync(Uid, "甜甜花", 10, null);
        var progress = await Progress();
        Assert.Equal(MaterialDemandState.Completed, progress.State);
        Assert.Equal("Quantity", progress.ConfirmationMethod);
        Assert.Equal(_source.Reference, progress.Source);
        var demand = (await _store.ReadDemandsAsync(Uid)).Items["甜甜花"];
        await _store.ArchiveDemandsAsync(Uid, new Dictionary<string, string> { ["甜甜花"] = demand.Revision });
        Assert.Single((await _store.ReadDemandsAsync(Uid)).History);
    }

    /// <summary>库存脚本核实达标不应覆盖人工登记的目标来源。</summary>
    [Fact]
    public Task CompletedDemandKeepsCreationSource() => RunSta(async () =>
    {
        await Report(20, DateTimeOffset.UtcNow.AddMinutes(-1));
        await _store.SetDemandAsync(Uid, "甜甜花", 10, null);
        var vm = new AccountRecordsDemandsViewModel(_store, _catalog, new InventoryIconCache(Path.Combine(_root, "icons")));
        await vm.SelectUidAsync(Uid);
        var row = Assert.Single(vm.Rows);
        Assert.Equal(MaterialDemandState.Completed, row.State);
        Assert.Equal("狛荷屋-人工创建", row.Source);
        Assert.Equal(_source.Reference, row.Progress!.Source);
        Assert.Equal("AccountRecords.SetDemandAsync", (await _store.ReadDemandsAsync(Uid)).Items["甜甜花"].Source);
        await _store.UpdateSettingsAsync(settings => settings.PreferReadableSource = false);
        await vm.SelectUidAsync(Uid);
        Assert.Equal("AccountRecords.SetDemandAsync", Assert.Single(vm.Rows).Source);
        await _store.UpdateSettingsAsync(settings => settings.PreferReadableSource = true);
        await vm.SelectUidAsync(Uid);
        Assert.Equal("狛荷屋-人工创建", Assert.Single(vm.Rows).Source);
    });

    /// <summary>需求与库存并集、筛选和排序，以及独立视图实例化和实际布局。</summary>
    [Fact]
    public Task DemandViewUsesUnionAndIndependentFilters() => RunSta(async () =>
    {
        await Report(5, DateTimeOffset.UtcNow.AddMinutes(-1));
        await _store.ReportAsync(Uid, _source, [new() { Name = "苹果", Quantity = 3, RecordedAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O") }]);
        await _store.SetDemandAsync(Uid, "铁块", 10, null);
        var vm = new AccountRecordsDemandsViewModel(_store, _catalog, new InventoryIconCache(Path.Combine(_root, "icons")));
        await vm.SelectUidAsync(Uid);
        Assert.Equal("", vm.Status);
        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal("铁块", vm.Items.Cast<AccountRecordsDemandRow>().First().Item.Name);
        Assert.Null(vm.Rows.Single(row => row.Item.Name == "苹果").State);
        Assert.Equal(MaterialDemandState.Unmet, vm.Rows.Single(row => row.Item.Name == "铁块").State);
        Assert.Equal("狛荷屋-人工创建", vm.Rows.Single(row => row.Item.Name == "铁块").Source);
        Assert.Equal("", vm.Rows.Single(row => row.Item.Name == "苹果").Source);
        vm.OnlyDemands = true;
        Assert.Single(vm.Items.Cast<object>());
        Assert.Single(vm.Categories);
        Assert.Equal("矿物", Assert.Single(vm.Categories[0].Children).Category);
        vm.SearchText = "不存在的物品";
        Assert.Empty(vm.Items.Cast<object>());
        Assert.Single(vm.Categories);
        vm.SearchText = "";
        vm.OnlyDemands = false;
        Assert.Equal(2, vm.Categories.Count);
        vm.SearchText = "苹果";
        Assert.Single(vm.Items.Cast<object>());
        vm.SearchText = "";
        var view = new AccountRecordsDemandsView { DataContext = vm };
        var notice = vm.Rows.Single(row => row.Item.Name == "铁块");
        vm.ShowStatusCommand.Execute(notice);
        Assert.Same(notice, vm.NoticeRow);
        Assert.True(vm.HasStatus && vm.HasNotice && notice.IsNoticeActive);
        Assert.False(vm.ShowStatusMessage);
        view.Measure(new Size(1200, 600));
        view.Arrange(new Rect(0, 0, 1200, 600));
        view.UpdateLayout();
        var grid = Assert.IsType<DataGrid>(view.FindName("DemandGrid"));
        Assert.Equal(3, grid.Items.Count);
        Assert.True(grid.ActualWidth > 0);
        vm.CloseNoticeCommand.Execute(null);
        Assert.False(vm.HasStatus || notice.IsNoticeActive);
        Assert.True(notice.HasDemand);
        var container = Assert.IsType<DataGridRow>(grid.ItemContainerGenerator.ContainerFromIndex(0));
        var menu = Assert.IsType<ContextMenu>(container.ContextMenu);
        var inventoryActions = menu.Items.OfType<MenuItem>().Where(item => item.Header?.ToString() is
            "查看物品详情…" or "修改数量 / 修订字典").ToArray();
        Assert.Equal(2, inventoryActions.Length);
        Assert.All(inventoryActions, item => Assert.NotNull(item.Command));
        // 通过真实 Sorting 事件验证降序也不能把无需求物品提到前面。
        var sort = typeof(DataGrid).GetMethod("OnSorting", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var quantityColumn = grid.Columns.Single(column => column.SortMemberPath == "Item.Quantity");
        sort.Invoke(grid, [new DataGridSortingEventArgs(quantityColumn)]);
        sort.Invoke(grid, [new DataGridSortingEventArgs(quantityColumn)]);
        Assert.Equal(ListSortDirection.Descending, quantityColumn.SortDirection);
        Assert.Equal("铁块", vm.Items.Cast<AccountRecordsDemandRow>().First().Item.Name);
        Assert.All(vm.Rows, row => Assert.False(row.Prior || row.ShowPriorSeparator));
        // 需求置顶覆盖有需求优先顺序，但不改变库存置顶。
        await _store.SetPriorAsync(Uid, "甜甜花", true);
        await vm.TogglePriorCommand.ExecuteAsync(vm.Rows.Single(row => row.Item.Name == "苹果"));
        Assert.Equal("苹果", vm.Items.Cast<AccountRecordsDemandRow>().First().Item.Name);
        Assert.True(vm.Rows.Single(row => row.Item.Name == "苹果").ShowPriorSeparator);
        Assert.False(vm.Rows.Single(row => row.Item.Name == "甜甜花").Prior);
        Assert.Equal("苹果", Assert.Single((await _store.ReadDemandsAsync(Uid)).Prior));
        Assert.Equal("甜甜花", Assert.Single((await _store.ReadAsync(Uid)).Inventory.Prior));
        var timeColumn = grid.Columns.Single(column => column.SortMemberPath == "CreatedAt");
        Assert.Equal(Visibility.Collapsed, timeColumn.Visibility);
        await vm.ToggleDemandCreatedAtCommand.ExecuteAsync(null);
        view.UpdateLayout();
        Assert.Equal(Visibility.Visible, timeColumn.Visibility);
        Assert.True((await _store.GetSettingsAsync()).ShowDemandCreatedAt);
        await vm.SelectUidAsync("100000002");
        Assert.Empty(vm.Rows);
        Assert.Empty(vm.Categories);
        Assert.Equal("", vm.Status);
    });

    /// <summary>提交真实总量，便于覆盖业务验证和事件归档。</summary>
    private Task<InventoryReportResult> Report(long quantity, DateTimeOffset time) => _store.ReportAsync(Uid, _source,
        [new() { Name = "甜甜花", Quantity = quantity, RecordedAt = time.ToString("O") }]);

    /// <summary>从真实存储重新读取需求状态，避免测试使用过期快照。</summary>
    private async Task<MaterialDemandProgress> Progress()
    {
        var (workspace, demands) = await _store.ReadDemandWorkspaceAsync(Uid);
        return MaterialDemandPolicy.Evaluate(demands.Items["甜甜花"], workspace.Inventory.Items.GetValueOrDefault("甜甜花")?.FirstOrDefault(),
            workspace.Inventory.Events.Where(item => item.Name == "甜甜花"), workspace.Settings.UseDeltaForDemands);
    }

    /// <summary>提供独立 STA 调度循环，验证 XAML 而不创建主程序。</summary>
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
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    /// <summary>仅清理本测试生成的随机临时目录。</summary>
    public void Dispose() => Directory.Delete(_root, true);
}
