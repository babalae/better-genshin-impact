using System.ComponentModel;
using System.Windows.Data;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using BetterGenshinImpact.ViewModel.Controls;
using OpenCvSharp;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>需求后台刷新保留交互状态，并阻止旧账号结果覆盖。</summary>
public sealed class DemandViewRefreshTests
{
    /// <summary>只改图标不触碰库存文件时，两张表仍能原位加载、替换和清除图片。</summary>
    [Fact]
    public Task IconOnlyChangesRefreshBothTablesWithoutReplacingRows() => RunSta(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "DemandViewRefreshTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var csv = Path.Combine(root, "items.csv");
            await File.WriteAllTextAsync(csv, "item_name,material_type,page\n甜甜花,植物,材料\n");
            var catalog = new InventoryCatalog(csv);
            var store = new InventoryStore(catalog, Path.Combine(root, "records"));
            var icons = new InventoryIconCache(Path.Combine(root, "icons"));
            const string uid = "100000001";
            await store.SetLocalQuantityAsync(uid, "甜甜花", 5);
            await store.SetDemandAsync(uid, "甜甜花", 10, null);
            var inventory = new AccountRecordsInventoryViewModel(store, catalog, icons);
            var demands = new AccountRecordsDemandsViewModel(store, catalog, icons);
            await inventory.SelectUidAsync(uid);
            await demands.SelectUidAsync(uid);
            var inventoryRow = Assert.Single(inventory.Rows);
            var demandRow = Assert.Single(demands.Rows);
            demands.OnlyDemands = true;
            demands.ShowStatusCommand.Execute(demandRow);
            var category = Assert.Single(demands.Categories);
            Assert.Null(await ReadIconAsync(inventoryRow));
            Assert.Null(await ReadIconAsync(demandRow.Item));
            var stamp = await store.GetInventoryUpdatedAtAsync(uid);

            using var input = new Mat(24, 24, MatType.CV_8UC3, Scalar.All(128));
            foreach (var size in new[] { 36, 72 })
            {
                icons.Store("甜甜花", input.ToBytes(".png"), size);
                await inventory.ResumeAsync(uid);
                await demands.ResumeAsync(uid);
                Assert.Same(inventoryRow, Assert.Single(inventory.Rows));
                Assert.Same(demandRow, Assert.Single(demands.Rows));
                Assert.Equal(size, Assert.IsAssignableFrom<BitmapSource>(await ReadIconAsync(inventoryRow)).PixelWidth);
                Assert.Equal(size, Assert.IsAssignableFrom<BitmapSource>(await ReadIconAsync(demandRow.Item)).PixelWidth);
            }
            icons.Delete("甜甜花");
            await inventory.ResumeAsync(uid);
            await demands.ResumeAsync(uid);
            Assert.Null(await ReadIconAsync(inventoryRow));
            Assert.Null(await ReadIconAsync(demandRow.Item));
            Assert.Same(demandRow, demands.NoticeRow);
            Assert.Same(category, Assert.Single(demands.Categories));
            Assert.True(demands.OnlyDemands);
            Assert.Equal(stamp, await store.GetInventoryUpdatedAtAsync(uid));
        }
        finally { Directory.Delete(root, true); }
    });

    /// <summary>等待实际异步图标解码完成，包括缓存缺失时的空结果。</summary>
    private static async Task<ImageSource?> ReadIconAsync(AccountRecordsInventoryRow row)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Loaded(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(row.Icon)) completion.TrySetResult();
        }
        row.PropertyChanged += Loaded;
        try
        {
            if (row.Icon is { } loaded) return loaded;
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return row.Icon;
        }
        finally { row.PropertyChanged -= Loaded; }
    }

    /// <summary>后台数量更新保留行、分类和固定提示，删除需求则关闭对应提示。</summary>
    [Fact]
    public Task BackgroundRefreshPreservesRowsAndNotice() => RunSta(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "DemandViewRefreshTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var csv = Path.Combine(root, "items.csv");
            await File.WriteAllTextAsync(csv, "item_name,material_type,page\n甜甜花,植物,材料\n");
            var catalog = new InventoryCatalog(csv);
            var store = new InventoryStore(catalog, Path.Combine(root, "records"));
            const string uid = "100000001";
            await store.SetLocalQuantityAsync(uid, "甜甜花", 5);
            await store.SetDemandAsync(uid, "甜甜花", 10, null);
            var vm = new AccountRecordsDemandsViewModel(store, catalog, new InventoryIconCache(Path.Combine(root, "icons")));
            await vm.SelectUidAsync(uid);
            var row = Assert.Single(vm.Rows);
            var item = row.Item;
            var category = Assert.Single(vm.Categories);
            vm.SearchText = "甜";
            vm.OnlyDemands = true;
            category = Assert.Single(vm.Categories);
            vm.ShowStatusCommand.Execute(row);
            var loading = false;
            vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.IsLoading) && vm.IsLoading) loading = true; };
            await store.SetLocalQuantityAsync(uid, "甜甜花", 12);
            await vm.ResumeAsync(uid);
            Assert.False(loading);
            Assert.Same(row, Assert.Single(vm.Rows));
            Assert.Same(item, row.Item);
            Assert.Same(category, Assert.Single(vm.Categories));
            Assert.Same(row, vm.NoticeRow);
            Assert.True(row.IsNoticeActive);
            Assert.Equal(MaterialDemandState.Completed, row.State);
            Assert.Equal(12, row.Item.Quantity);
            Assert.Equal("甜", vm.SearchText);
            Assert.True(vm.OnlyDemands);
            await store.DeleteDemandAsync(uid, "甜甜花", row.Demand!.Revision);
            await vm.ResumeAsync(uid);
            Assert.Same(row, Assert.Single(vm.Rows));
            Assert.Null(vm.NoticeRow);
            Assert.False(row.IsNoticeActive);
            Assert.Empty(vm.Items.Cast<object>());
            // 后台读取正在运行时切换账号，旧结果不能重新填回新账号。
            var pending = vm.ResumeAsync(uid);
            await vm.SelectUidAsync("100000002");
            await pending;
            Assert.Equal("100000002", vm.Uid);
            Assert.Empty(vm.Rows);
        }
        finally { Directory.Delete(root, true); }
    });

    /// <summary>需求只显示实际库存和需求物品，不补齐同族；置顶和筛选按单项生效。</summary>
    [Fact]
    public Task DemandRowsDoNotExpandOrPinFamilies() => RunSta(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "DemandViewRefreshTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var csv = Path.Combine(root, "items.csv");
            await File.WriteAllTextAsync(csv, "item_name,material_type,page\n史莱姆凝液,培养素材,材料\n史莱姆清,培养素材,材料\n史莱姆原浆,培养素材,材料\n");
            var catalog = new InventoryCatalog(csv);
            var store = new InventoryStore(catalog, Path.Combine(root, "records"));
            const string uid = "100000001";
            await store.SetLocalQuantityAsync(uid, "史莱姆清", 9);
            await store.SetDemandAsync(uid, "史莱姆原浆", 1, null);
            var vm = new AccountRecordsDemandsViewModel(store, catalog, new InventoryIconCache(Path.Combine(root, "icons")));
            await vm.SelectUidAsync(uid);
            Assert.Equal(2, vm.Rows.Count);
            Assert.DoesNotContain(vm.Rows, row => row.Item.Name == "史莱姆凝液");
            vm.OnlyDemands = true;
            Assert.Equal("史莱姆原浆", Assert.Single(vm.Items.Cast<AccountRecordsDemandRow>()).Item.Name);
            vm.OnlyDemands = false;
            var selected = vm.Rows.Single(row => row.Item.Name == "史莱姆清");
            await vm.TogglePriorCommand.ExecuteAsync(selected);
            Assert.Same(selected, vm.Items.Cast<AccountRecordsDemandRow>().First());
            Assert.Same(selected, Assert.Single(vm.Rows.Where(row => row.Prior)));
            Assert.Same(selected, Assert.Single(vm.Rows.Where(row => row.ShowPriorSeparator)));
        }
        finally { Directory.Delete(root, true); }
    });

    /// <summary>已有行数量、目标或默认时间排序变化时重排，同时保留行对象。</summary>
    [Theory]
    [InlineData("Item.Quantity")]
    [InlineData("TargetQuantity")]
    [InlineData("Default")]
    public Task Resume_ReordersChangedSortValues(string member) => RunSta(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "DemandSortRefresh", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var csv = Path.Combine(root, "items.csv");
            File.WriteAllText(csv, "item_name,material_type,page\n甜甜花,植物,材料\n薄荷,植物,材料\n");
            var catalog = new InventoryCatalog(csv);
            var store = new InventoryStore(catalog, Path.Combine(root, "records"));
            const string uid = "123456789";
            var time = DateTimeOffset.UtcNow.AddHours(-1);
            var source = new AccountRecordSource { Reference = "scan", DisplayName = "扫描" };
            await store.ReportAsync(uid, source, [
                new() { Name = "甜甜花", Quantity = 1, RecordedAt = time.ToString("O") },
                new() { Name = "薄荷", Quantity = 2, RecordedAt = time.AddMinutes(1).ToString("O") }]);
            await store.SetDemandAsync(uid, "甜甜花", 10, null);
            await store.SetDemandAsync(uid, "薄荷", 20, null);
            var vm = new AccountRecordsDemandsViewModel(store, catalog, new InventoryIconCache(Path.Combine(root, "icons")));
            await vm.SelectUidAsync(uid);
            if (member != "Default")
                ((ListCollectionView)vm.Items).CustomSort = new AccountRecordsDemandComparer(
                    [new SortDescription(member, ListSortDirection.Ascending)]);
            var first = vm.Items.Cast<AccountRecordsDemandRow>().First();
            Assert.Equal(member == "Default" ? "薄荷" : "甜甜花", first.Item.Name);
            var flower = vm.Rows.Single(row => row.Item.Name == "甜甜花");
            if (member == "TargetQuantity")
                await store.SetDemandAsync(uid, "甜甜花", 30, flower.Demand!.Revision);
            else
                await store.ReportAsync(uid, source, [new() { Name = "甜甜花", Quantity = 3, RecordedAt = time.AddMinutes(2).ToString("O") }]);
            await vm.ResumeAsync(uid);
            Assert.Equal(member == "Default" ? "甜甜花" : "薄荷", vm.Items.Cast<AccountRecordsDemandRow>().First().Item.Name);
            Assert.Same(flower, vm.Rows.Single(row => row.Item.Name == "甜甜花"));
        }
        finally { Directory.Delete(root, true); }
    });
    /// <summary>在独立 STA 调度器中执行绑定测试，无需启动主程序。</summary>
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
}
