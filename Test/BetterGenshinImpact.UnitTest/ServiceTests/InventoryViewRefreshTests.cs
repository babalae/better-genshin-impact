using System.IO;
using System.Threading;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BetterGenshinImpact.View.Controls;
using BetterGenshinImpact.View.Windows;
using BetterGenshinImpact.Service.AccountRecords;
using BetterGenshinImpact.Service.AccountRecords.Model;
using BetterGenshinImpact.ViewModel.Controls;
using BetterGenshinImpact.ViewModel.Pages;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

/// <summary>用独立 STA 消息循环验证账号切换和原位刷新，不启动主程序或显示窗口。</summary>
public sealed class InventoryViewRefreshTests
{
    /// <summary>实际布局底部多卡片，验证模板按钮绑定到各自卡片及当前视图模型。</summary>
    [Fact]
    public Task NoticeCards_InstantiateAndBindIndependentActions() => RunSta(async () =>
    {
        var catalog = new InventoryCatalog();
        var vm = new AccountRecordsInventoryViewModel(new InventoryStore(catalog), catalog, new InventoryIconCache());
        vm.NoticeCards.Add(new() { Name = "材料", State = "Warning", Title = "提醒", Message = "历史错误已核实", CanArchive = true });
        vm.NoticeCards.Add(new() { Name = "材料", State = "Pending", Title = "增减待核实", Message = "+1" });
        vm.Status = "测试";
        var view = new AccountRecordsInventoryView { DataContext = vm };
        view.Measure(new Size(1000, 700));
        view.Arrange(new Rect(0, 0, 1000, 700));
        view.UpdateLayout();
        var cards = Assert.Single(VisualChildren(view).OfType<ItemsControl>().Where(control => ReferenceEquals(control.ItemsSource, vm.NoticeCards)));
        Assert.Equal(2, cards.Items.Count);
        // 多条只露出下一条的一部分，避免通知区随事件数量增长。
        var scroll = Assert.Single(VisualChildren(view).OfType<ScrollViewer>().Where(control => ReferenceEquals(control.Content, cards)));
        var firstCard = Assert.IsAssignableFrom<FrameworkElement>(cards.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Equal(Math.Min(260, firstCard.ActualHeight * 1.2), scroll.MaxHeight, 1);
        var buttons = VisualChildren(cards).OfType<Button>().Where(button => Equals(button.Content, "归档")).ToArray();
        Assert.Equal(2, buttons.Length);
        Assert.All(buttons, button => Assert.Same(vm.ArchiveNoticeCardCommand, button.Command));
        Assert.Same(vm.NoticeCards[0], buttons[0].CommandParameter);
        Assert.Equal(Visibility.Visible, buttons[0].Visibility);
        Assert.Equal(Visibility.Collapsed, buttons[1].Visibility);
        // 在两个都有多条提示的物品之间切换，不能沿用上一个物品的滚动位置。
        vm.NoticeRow = new AccountRecordsInventoryRow { Name = "材料" };
        view.UpdateLayout();
        scroll.ScrollToBottom();
        view.UpdateLayout();
        Assert.True(scroll.VerticalOffset > 0);
        vm.NoticeRow = new AccountRecordsInventoryRow { Name = "另一材料" };
        view.UpdateLayout();
        Assert.Equal(0, scroll.VerticalOffset);
        vm.NoticeCards.RemoveAt(1);
        view.UpdateLayout();
        Assert.Equal(260, scroll.MaxHeight);
        await Task.CompletedTask;
    });

    /// <summary>同账号含异常导入、未变文件时间及已有加载任务均不能跳过导入后的完整刷新。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Import_RefreshesSameUidWithErrorsAndConcurrentLoad(bool loading) => RunSta(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "BetterGI.InventoryViewTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var csv = Path.Combine(root, "items.csv");
            File.WriteAllText(csv, "item_name,material_type\n摩拉,货币\n");
            var catalog = new InventoryCatalog(csv);
            var store = new InventoryStore(catalog, Path.Combine(root, "records"));
            const string uid = "100000001";
            var source = new AccountRecordSource { Reference = "scan", DisplayName = "scan" };
            var time = DateTimeOffset.UtcNow.AddHours(-1);
            await store.ReportAsync(uid, source, [new() { Name = "摩拉", Quantity = 10, RecordedAt = time.ToString("O") }]);
            var inventory = new AccountRecordsInventoryViewModel(store, catalog, new InventoryIconCache());
            var page = new AccountRecordsPageViewModel(store, inventory) { SelectedUid = uid };
            await inventory.SelectUidAsync(uid);
            var stamp = await store.GetInventoryUpdatedAtAsync(uid);
            var package = await store.ExportInventoryAsync(uid);
            var items = (JObject)package["accounts"]![uid]!["inventory"]!;
            items["摩拉"]![0]!["quantity"] = 20;
            items["摩拉"]![0]!["recordedAt"] = time.AddMinutes(1).ToString("O");
            items["未收录名称"] = items["摩拉"]!.DeepClone();
            var result = await store.ImportInventoryAsync(package, uid, false);
            Assert.Single(result.Accepted);
            Assert.Single(result.Rejected);
            // 模拟文件时间没有变化，导入后的同步不能仅依赖时间戳检查。
            File.SetLastWriteTimeUtc(Path.Combine(root, "records", uid, "inventory.json"), stamp);
            var previous = loading ? inventory.SelectUidAsync(uid) : Task.CompletedTask;
            await page.RefreshAfterImportAsync(uid);
            await previous;
            Assert.Equal(uid, page.SelectedUid);
            Assert.False(inventory.IsLoading);
            Assert.Equal(20, inventory.Rows.Single(row => row.Name == "摩拉").Quantity);
            Assert.True(inventory.Rows.Single(row => row.Name == "未收录名称").HasError);
        }
        finally { Directory.Delete(root, true); }
    });

    /// <summary>在 STA 中实际实例化并布局已有弹窗的到期编辑扩展，不显示窗口或启动主程序。</summary>
    [Fact]
    public Task ExpirationEditor_InstantiatesAndLaysOutExistingDialog() => RunSta(async () =>
    {
        var catalog = new InventoryCatalog();
        var row = new AccountRecordsInventoryRow
        {
            Name = "须臾树脂", Quantity = 2,
            Expirations = [new() { Quantity = 1, ExpiresAt = DateTimeOffset.UtcNow.AddDays(2).ToString("O") }]
        };
        var vm = new AccountRecordsInventoryEditViewModel(new InventoryStore(catalog), catalog, "100000001", row,
            new Dictionary<string, string> { [row.Name] = "材料" });
        Assert.True(vm.ShowExpirationEditor);
        Assert.Single(vm.ExpirationRows);
        var dialog = new AccountRecordsInventoryEditDialog(vm);
        try
        {
            var content = (FrameworkElement)dialog.Content;
            content.Measure(new Size(460, 480));
            content.Arrange(new Rect(0, 0, 460, 480));
            content.UpdateLayout();
            Assert.True(content.ActualWidth > 0);
            var batches = Assert.Single(VisualChildren(content).OfType<ItemsControl>()
                .Where(control => ReferenceEquals(control.ItemsSource, vm.ExpirationRows)));
            Assert.Equal(Visibility.Visible, ((FrameworkElement)batches.Parent).Visibility);
            var editor = Assert.Single(VisualChildren(batches).OfType<TextBox>()
                .Where(control => control.Text == vm.ExpirationRows[0].ExpiresAtText));
            Assert.Same(vm.ExpirationRows[0], editor.DataContext);
            var remove = Assert.Single(VisualChildren(batches).OfType<Button>()
                .Where(control => Equals(control.Content, "删除")));
            Assert.Same(vm.RemoveExpirationCommand, remove.Command);
            Assert.Same(vm.ExpirationRows[0], remove.CommandParameter);
            vm.AddExpirationCommand.Execute(null);
            Assert.Equal(2, vm.ExpirationRows.Count);
            remove.Command!.Execute(remove.CommandParameter);
            Assert.Single(vm.ExpirationRows);
        }
        finally { dialog.Close(); }
        await Task.CompletedTask;
    });

    /// <summary>遍历实际模板生成的控件，验证模板内部绑定而非仅验证外层窗口尺寸。</summary>
    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in VisualChildren(child)) yield return descendant;
        }
    }

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

            // 核实旧错误后再次叠加新错误和增量，归档提醒只能影响本卡片。
            await store.ReportAsync(uid, scan, [new() { Name = "摩拉", Quantity = 12, RecordedAt = time.AddMinutes(4).ToString("O") }]);
            await store.ReportDeltaAsync(uid, scan, [new() { Name = "摩拉", Delta = 1, RecordedAt = time.AddMinutes(5).ToString("O") }]);
            await store.ReportDeltaAsync(uid, scan, [new() { Name = "摩拉", Delta = -100, RecordedAt = time.AddMinutes(6).ToString("O") }]);
            await vm.RefreshTimeWarningsCommand.ExecuteAsync(null);
            var warningCard = Assert.Single(vm.NoticeCards.Where(card => card.State == "Warning"));
            Assert.Contains(vm.NoticeCards, card => card.State == "Error");
            Assert.Contains(vm.NoticeCards, card => card.State == "Pending");
            await vm.ArchiveNoticeCardCommand.ExecuteAsync(warningCard);
            var archived = (await store.LoadAsync(uid)).Inventory;
            Assert.All(archived.Events.Where(item => warningCard.EventIds.Contains(item.Id)), item => Assert.True(item.Archived));
            Assert.Contains(archived.Events, item => item.State == "Error" && !item.Archived);
            Assert.Contains(archived.Events, item => item.State == "Pending" && !item.Archived);
            var staleErrorCard = Assert.Single(vm.NoticeCards.Where(card => card.State == "Error"));
            vm.CloseNoticeCommand.Execute(null);
            Assert.Empty(vm.NoticeCards);
            await vm.ArchiveNoticeCardCommand.ExecuteAsync(staleErrorCard);
            Assert.All((await store.LoadAsync(uid)).Inventory.Events.Where(item => staleErrorCard.EventIds.Contains(item.Id)), item => Assert.False(item.Archived));

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
