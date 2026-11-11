using System.IO;
using System.Threading;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Controls;
using BetterGenshinImpact.View.Pages;
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
    /// <summary>切换加载中或读取失败后保存显示偏好，不得覆盖最新保留上限。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Preferences_PreserveRetentionDuringFailedUidSwitch(bool duringLoading) => RunSta(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "BetterGI.InventoryViewTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = new InventoryCatalog();
            var store = new InventoryStore(catalog, root);
            var vm = new AccountRecordsInventoryViewModel(store, catalog, new InventoryIconCache());
            await vm.LoadPreferencesAsync();
            // 模拟其他入口更新设置，使本页缓存也落后于磁盘。
            await store.ConfigureAsync(new() { RecentLimit = 10, SnapshotLimit = 30, EventLimit = 50000 });
            var loading = vm.SelectUidAsync("invalid-uid");
            if (!duringLoading) await loading;
            await vm.ToggleExpirationColumnCommand.ExecuteAsync(null);
            await loading;
            await vm.ToggleImageCacheCommand.ExecuteAsync(null);
            await vm.ToggleMissingIconRowNumbersCommand.ExecuteAsync(null);
            await vm.ToggleSourceNamesCommand.ExecuteAsync(null);
            await vm.ToggleSnapshotsCommand.ExecuteAsync(null);
            var settings = await store.GetSettingsAsync();
            Assert.Equal(10, settings.RecentLimit);
            Assert.Equal(30, settings.SnapshotLimit);
            Assert.Equal(50000, settings.EventLimit);
        }
        finally { Directory.Delete(root, true); }
    });

    /// <summary>
    /// UID 下拉框来自 Pulonia 账户目录：只列出填写了狛荷屋可用 UID 的账户，显示“名称（UID）”、顺序与账户列表一致；
    /// 首次打开选中本实例当前账户；账户改名原位更新，UID 修改后仍选中同一账户；账户页的选中请求优先；没有账户时提示。
    /// </summary>
    [Fact]
    public Task UidOptions_FollowAccountDirectory() => RunSta(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "BetterGI.InventoryViewTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var csv = Path.Combine(root, "items.csv");
            File.WriteAllText(csv, "item_name,material_type\n摩拉,货币\n");
            var catalog = new InventoryCatalog(csv);
            var store = new InventoryStore(catalog, Path.Combine(root, "records"));
            using var puloniaStore = new PuloniaTaskStore(Path.Combine(root, "pulonia"));
            var accounts = new PuloniaAccountService(puloniaStore, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
            // 测试数据：我的账户（无 UID）、乙、首位为 0 的丙（狛荷屋不支持）、丁；本实例当前账户为丁
            var settings = await accounts.LoadAccountsAsync();
            settings.Accounts.Add(new PuloniaGenshinAccountProfile { Name = "乙", Uid = "100000002" });
            settings.Accounts.Add(new PuloniaGenshinAccountProfile { Name = "丙", Uid = "012345678" });
            settings.Accounts.Add(new PuloniaGenshinAccountProfile { Name = "丁", Uid = "100000004" });
            settings = (await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>())).Settings;
            await accounts.SetCurrentAccountAsync(PuloniaInstanceKey.Primary, settings.Accounts[3].Id);
            var page = new AccountRecordsPageViewModel(store, new AccountRecordsInventoryViewModel(store, catalog, new InventoryIconCache()), accounts);

            await page.LoadCommand.ExecuteAsync(null);
            Assert.Equal(["乙（100000002）", "丁（100000004）"], page.UidOptions.Select(option => option.DisplayText));
            Assert.Equal("100000004", page.SelectedUid);
            Assert.False(page.HasNoAccounts);

            // 账户页改名乙、修改丁的 UID：乙的选项原位改名，丁仍被选中并改为新 UID
            var first = page.UidOptions[0];
            settings = await accounts.LoadAccountsAsync();
            settings.Accounts[1].Name = "戊";
            settings.Accounts[3].Uid = "100000005";
            await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>());
            await page.LoadCommand.ExecuteAsync(null);
            Assert.Same(first, page.UidOptions[0]);
            Assert.Equal(["戊（100000002）", "丁（100000005）"], page.UidOptions.Select(option => option.DisplayText));
            Assert.Equal("100000005", page.SelectedUid);

            // 账户页“打开狛荷屋”的请求优先于原来的选择
            page.RequestSelectUid("100000002");
            await page.LoadCommand.ExecuteAsync(null);
            Assert.Equal("100000002", page.SelectedUid);

            // 只剩没有 UID 的“我的账户”时列表为空，提示到账户页填写 UID
            settings = await accounts.LoadAccountsAsync();
            await accounts.SetCurrentAccountAsync(PuloniaInstanceKey.Primary, settings.Accounts[0].Id);
            settings.Accounts.RemoveRange(1, settings.Accounts.Count - 1);
            await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>());
            await page.LoadCommand.ExecuteAsync(null);
            Assert.Empty(page.UidOptions);
            Assert.Null(page.SelectedUid);
            Assert.True(page.HasNoAccounts);
        }
        finally { Directory.Delete(root, true); }
    });

    /// <summary>实际实例化并布局狛荷屋页面：UID 下拉框绑定账户选项并显示“名称（UID）”；有账户时不显示“管理账户”提示，没有时显示。</summary>
    [Fact]
    public Task Page_InstantiatesAndLaysOutAccountSelector() => RunSta(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "BetterGI.InventoryViewTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var csv = Path.Combine(root, "items.csv");
            File.WriteAllText(csv, "item_name,material_type\n摩拉,货币\n");
            var catalog = new InventoryCatalog(csv);
            var store = new InventoryStore(catalog, Path.Combine(root, "records"));
            using var puloniaStore = new PuloniaTaskStore(Path.Combine(root, "pulonia"));
            var accounts = await CreateAccountsAsync(puloniaStore, "100000002");
            var vm = new AccountRecordsPageViewModel(store, new AccountRecordsInventoryViewModel(store, catalog, new InventoryIconCache()), accounts);
            await vm.LoadCommand.ExecuteAsync(null);
            var page = new AccountRecordsPage(vm);
            // 页签模板里的局部样式 BasedOn WPF-UI 默认样式，需要 App.xaml 合并的字典，测试中无法布局整页；
            // 这里只布局本次修改的 UID 选择器与“管理账户”提示，它们在页面构造时已经实例化。
            var selector = Assert.IsType<ComboBox>(page.FindName("UidSelector"));
            Layout(selector, 220, 34);
            Assert.Same(vm.UidOptions, selector.ItemsSource);
            Assert.Same(vm.UidOptions[0], selector.SelectedItem);
            Assert.Contains(VisualChildren(selector).OfType<TextBlock>(), text => text.Text == "账户 100000002（100000002）");
            var notice = Assert.Single(LogicalChildren(page).OfType<Border>()
                .Where(border => border.Child is DockPanel panel && panel.Children.OfType<TextBlock>().Any(text => text.Text.StartsWith("账户目录中还没有"))));
            Assert.Equal(Visibility.Collapsed, notice.Visibility);
            var manage = Assert.Single(((DockPanel)notice.Child).Children.OfType<Button>());
            Assert.Same(vm.ManageAccountsCommand, manage.Command);

            // 删除唯一有 UID 的账户后重新读取：选项清空，提示可见并能布局
            var settings = await accounts.LoadAccountsAsync();
            settings.Accounts.RemoveAt(1);
            await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>());
            await vm.LoadCommand.ExecuteAsync(null);
            Layout(selector, 220, 34);
            Assert.Null(selector.SelectedItem);
            Assert.Equal(Visibility.Visible, notice.Visibility);
            Layout(notice, 800, 60);
            Assert.True(manage.ActualWidth > 0);
        }
        finally { Directory.Delete(root, true); }
    });

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
            using var puloniaStore = new PuloniaTaskStore(Path.Combine(root, "pulonia"));
            var page = new AccountRecordsPageViewModel(store, inventory, await CreateAccountsAsync(puloniaStore, uid)) { SelectedUid = uid };
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

    /// <summary>按给定尺寸测量与排列单个控件，并完成一次布局。</summary>
    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    /// <summary>遍历页面构造时建立的逻辑树，不触发模板应用。</summary>
    private static IEnumerable<DependencyObject> LogicalChildren(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in LogicalChildren(child)) yield return descendant;
        }
    }

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
            using var puloniaStore = new PuloniaTaskStore(Path.Combine(root, "pulonia"));
            var page = new AccountRecordsPageViewModel(store, vm, await CreateAccountsAsync(puloniaStore, uid)) { SelectedUid = uid };
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

            // 新增错误刷新卡片后，旧卡片不能继续舍弃，也不能改变库存。
            var old = new AccountRecordSource { Reference = "old", DisplayName = "old" };
            await store.ReportAsync(uid, old, [new() { Name = "摩拉", Quantity = 8, RecordedAt = time.ToString("O") }]);
            await vm.RefreshTimeWarningsCommand.ExecuteAsync(null);
            vm.ShowStatusCommand.Execute(row);
            var previousErrorCard = Assert.Single(vm.NoticeCards.Where(card => card.State == "Error"));
            Assert.True(previousErrorCard.CanResolve);
            await store.ReportDeltaAsync(uid, scan, [new() { Name = "摩拉", Delta = -100, RecordedAt = time.AddMinutes(3).ToString("O") }]);
            await vm.RefreshTimeWarningsCommand.ExecuteAsync(null);
            Assert.Equal("Delta", row.Event?.Kind);
            var currentErrorCard = Assert.Single(vm.NoticeCards.Where(card => card.State == "Error"));
            Assert.True(currentErrorCard.CanResolve);
            Assert.Equal(2, currentErrorCard.EventIds.Length);
            await vm.ResolveNoticeCardCommand.ExecuteAsync(previousErrorCard);
            var unchanged = (await store.LoadAsync(uid)).Inventory;
            Assert.Equal(12, unchanged.Items["摩拉"][0].Quantity);
            Assert.True(unchanged.Errors.ContainsKey("摩拉"));
            Assert.All(unchanged.Events.Where(item => currentErrorCard.EventIds.Contains(item.Id)), item => Assert.False(item.Archived));
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

    /// <summary>在给定的临时 Pulonia 存储中建立账户目录：“我的账户”加给定 UID 的账户，供狛荷屋页面读取 UID 列表。</summary>
    private static async Task<PuloniaAccountService> CreateAccountsAsync(PuloniaTaskStore store, params string[] uids)
    {
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
        var settings = await accounts.LoadAccountsAsync();
        foreach (var uid in uids)
            settings.Accounts.Add(new PuloniaGenshinAccountProfile { Name = "账户 " + uid, Uid = uid });
        await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>());
        return accounts;
    }

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
