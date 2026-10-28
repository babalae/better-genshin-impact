using System.IO;
using System.Threading;
using System.Windows.Threading;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using BetterGenshinImpact.ViewModel.Controls;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>用独立 STA 消息循环验证账号切换和原位刷新，不启动主程序或显示窗口。</summary>
public sealed class InventoryViewRefreshTests
{
    /// <summary>刷新同步整行和提示操作目标，保留行身份及排序键；账号加载失败不能露出其他账号数据。</summary>
    [Fact]
    public Task Refresh_KeepsIdentityAndRejectsMismatchedError() => RunSta(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "BetterGI.InventoryViewTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var csv = Path.Combine(root, "items.csv");
            File.WriteAllText(csv, "item_name,material_type\n摩拉,货币\n");
            var catalog = new InventoryCatalog(csv);
            var store = new InventoryStore(catalog, Path.Combine(root, "records"));
            var time = DateTimeOffset.UtcNow.AddHours(-1);
            const string uid = "100000001";
            var scan = new AccountRecordSource { Reference = "scan", DisplayName = "scan" };
            await store.ReportAsync(uid, scan, [new() { Name = "摩拉", Quantity = 10, RecordedAt = time.ToString("O") }]);
            var vm = new AccountRecordsInventoryViewModel(store, catalog, new InventoryIconCache());
            var page = new AccountRecordsPageViewModel(store, vm) { SelectedUid = uid };
            await vm.SelectUidAsync(uid);
            var row = Assert.Single(vm.Rows);
            var keys = row.SortValues;
            var collectionChanges = 0;
            vm.Rows.CollectionChanged += (_, _) => collectionChanges++;
            await store.ReportDeltaAsync(uid, scan, [new() { Name = "摩拉", Delta = 2, RecordedAt = time.AddMinutes(1).ToString("O") }]);
            await vm.RefreshTimeWarningsCommand.ExecuteAsync(null);
            Assert.Same(row, Assert.Single(vm.Rows));
            Assert.Equal(2m, row.PendingDelta);
            Assert.Equal("Pending", row.Event?.State);
            // 模拟从其他主页面反复返回：沿用行、排序、筛选和底部提示，不显示加载遮罩。
            vm.SearchText = "摩拉";
            vm.ShowStatusCommand.Execute(row);
            var notice = vm.Status;
            var replacedContent = 0;
            var rowNotifications = 0;
            var loadingTransitions = 0;
            row.PropertyChanged += (_, args) =>
            {
                rowNotifications++;
                if (string.IsNullOrEmpty(args.PropertyName)) replacedContent++;
            };
            vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.IsLoading) && vm.IsLoading) loadingTransitions++; };
            await page.LoadCommand.ExecuteAsync(null);
            await page.LoadCommand.ExecuteAsync(null);
            Assert.Same(row, Assert.Single(vm.Rows));
            Assert.Same(keys, row.SortValues);
            Assert.Same(row, vm.NoticeRow);
            Assert.Equal(notice, vm.Status);
            Assert.Equal("摩拉", vm.SearchText);
            Assert.Equal(0, collectionChanges);
            Assert.Equal(0, replacedContent);
            Assert.Equal(0, rowNotifications);
            Assert.Equal(0, loadingTransitions);
            var verified = await store.ReportAsync(uid, scan, [new()
            {
                Name = "摩拉", Quantity = 12, RecordedAt = time.AddMinutes(2).ToString("O")
            }]);
            Assert.Single(verified.Accepted);
            await page.LoadCommand.ExecuteAsync(null);
            Assert.Equal(12, row.Quantity);
            Assert.Null(row.PendingDelta);
            Assert.Same(keys, row.SortValues);
            Assert.Equal(0, collectionChanges);
            Assert.Equal(1, replacedContent);
            Assert.Equal(0, loadingTransitions);

            // 最新增量错误与较早可接受的库存冲突不是同一个操作目标。
            var old = new AccountRecordSource { Reference = "old", DisplayName = "old" };
            await store.ReportAsync(uid, old, [new() { Name = "摩拉", Quantity = 8, RecordedAt = time.ToString("O") }]);
            await vm.RefreshTimeWarningsCommand.ExecuteAsync(null);
            vm.ShowStatusCommand.Execute(row);
            Assert.False(vm.CanAcceptNotice);
            await store.ReportDeltaAsync(uid, scan, [new() { Name = "摩拉", Delta = -100, RecordedAt = time.AddMinutes(3).ToString("O") }]);
            await vm.RefreshTimeWarningsCommand.ExecuteAsync(null);
            Assert.Equal("Delta", row.Event?.Kind);
            Assert.False(vm.CanAcceptNotice);
            Assert.True(vm.CanResolveNotice);
            await vm.ResolveNoticeCommand.ExecuteAsync("accept");
            Assert.Equal(12, (await store.LoadAsync(uid)).Inventory.Items["摩拉"][0].Quantity);
            Assert.Equal(0, collectionChanges);

            // 在第一次切换尚未越过 Dispatcher.Yield 时再次请求同一目标，并让读取失败。
            var first = vm.SelectUidAsync("invalid-uid");
            var second = vm.SelectUidAsync("invalid-uid");
            await Task.WhenAll(first, second);
            Assert.Empty(vm.Rows);
            Assert.Null(vm.NoticeRow);
            Assert.False(vm.IsLoading);
            Assert.Contains("库存读取失败", vm.Status);
            await vm.SelectUidAsync(uid);
            Assert.Equal(12, Assert.Single(vm.Rows).Quantity);
        }
        finally { Directory.Delete(root, true); }
    });

    /// <summary>给异步 ViewModel 提供 WPF 调度上下文，完成后结束线程。</summary>
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
