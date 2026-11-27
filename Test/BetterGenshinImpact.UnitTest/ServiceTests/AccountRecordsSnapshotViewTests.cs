using System.ComponentModel;
using System.Threading;
using System.Windows.Threading;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using BetterGenshinImpact.ViewModel.Controls;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>独立验证快照浏览的展示顺序和筛选，不启动主程序。</summary>
public sealed class AccountRecordsSnapshotViewTests
{
    /// <summary>快照行号按可见顺序重排，不沿用文件中物品的插入顺序。</summary>
    [Fact]
    public Task RowNumbersFollowSortingAndFiltering() => RunSta(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "SnapshotViewTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var csv = Path.Combine(root, "items.csv");
            await File.WriteAllTextAsync(csv, "item_name,material_type,page\n甜甜花,植物,材料\n铁块,矿物,材料\n");
            var catalog = new InventoryCatalog(csv);
            var store = new InventoryStore(catalog, Path.Combine(root, "records"));
            const string uid = "100000001";
            var time = DateTimeOffset.UtcNow;
            await store.ReportAsync(uid, new AccountRecordSource { Reference = "test", DisplayName = "测试来源" },
                [new() { Name = "甜甜花", Quantity = 12, RecordedAt = time.AddMinutes(-1).ToString("O") },
                 new() { Name = "铁块", Quantity = 3, RecordedAt = time.ToString("O") }]);
            var directory = Path.Combine(root, "records", uid, "snapshots");
            Directory.CreateDirectory(directory);
            var day = InventoryStore.RecordDay(time.AddDays(-1)).ToString("yyyy-MM-dd");
            await File.WriteAllTextAsync(Path.Combine(directory, day + ".json"), (await store.ExportInventoryAsync(uid)).ToString());
            var vm = new AccountRecordsSnapshotsViewModel(store, catalog, new InventoryIconCache(Path.Combine(root, "icons")), uid);
            try
            {
                await vm.InitializeAsync();
                var initial = vm.Items.Cast<AccountRecordsInventoryRow>().ToArray();
                Assert.Equal(new[] { "铁块", "甜甜花" }, initial.Select(row => row.Name));
                Assert.Equal(new[] { "1.", "2." }, initial.Select(row => row.DisplayRowNumber));
                using (vm.Items.DeferRefresh())
                {
                    vm.Items.SortDescriptions.Clear();
                    vm.Items.SortDescriptions.Add(new(nameof(AccountRecordsInventoryRow.Quantity), ListSortDirection.Descending));
                }
                Assert.Equal("甜甜花", vm.Items.Cast<AccountRecordsInventoryRow>().First().Name);
                Assert.Equal(new[] { "1.", "2." }, vm.Items.Cast<AccountRecordsInventoryRow>().Select(row => row.DisplayRowNumber));
                vm.SearchText = "铁";
                Assert.Equal("1.", Assert.Single(vm.Items.Cast<AccountRecordsInventoryRow>()).DisplayRowNumber);
            }
            finally { vm.Close(); }
        }
        finally { Directory.Delete(root, true); }
    });

    /// <summary>使用独立 STA 调度器完成异步绑定与集合通知。</summary>
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
