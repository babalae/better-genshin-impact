using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 账户页：用临时目录中的账户数据构造视图模型与页面，在 STA 线程实际实例化 XAML 并布局，
/// 不启动主程序、不显示窗口、不触碰游戏。另验证删除账户时清理账户图片与运行账户入口的规则。
/// </summary>
public sealed class PuloniaAccountsPageTests : IDisposable
{
    /// <summary>本测试独占的目录，不接触用户真实账户与凭据。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>有多个账户时显示列表，各行带 UID 尾号、作为哪些实例的当前账户与登录资料状态，布局不抛出异常。</summary>
    [Fact] public Task PageInstantiatesAndLaysOutWithMultipleAccounts() => RunSta(async () =>
    {
        using var store = new PuloniaTaskStore(_directory);
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
        // 测试数据：“我的账户”加两个有 UID 的账户；云原神实例“小号a”的当前账户为“乙”
        var settings = await accounts.LoadAccountsAsync();
        settings.Accounts.Add(new PuloniaGenshinAccountProfile { Name = "乙", Uid = "100000002", ListText = "138****0000" });
        settings.Accounts.Add(new PuloniaGenshinAccountProfile { Name = "丙", Uid = "100000003", ServerTimeZone = PuloniaTaskServerTimeZone.Europe });
        settings = (await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>())).Settings;
        await accounts.SetCurrentAccountAsync("webview:小号a", settings.Accounts[1].Id);

        await using var service = CreateService(store, accounts);
        var vm = CreatePage(store, accounts, service);
        await vm.LoadAsync(force: false);
        Assert.Equal(3, vm.Accounts.Count);
        Assert.True(vm.ShowAccountList);
        Assert.False(vm.HasUnsavedChanges);
        Assert.Equal(settings.Accounts[0].Id, vm.CurrentAccountId);
        Assert.Equal(3, vm.CurrentAccountOptions.Count);
        Assert.Equal("主实例（本实例）", vm.Accounts[0].InstancesText);
        Assert.Equal("云原神 · 小号a", vm.Accounts[1].InstancesText);
        Assert.Equal("UID 尾号 0002", vm.Accounts[1].UidText);
        Assert.True(vm.Accounts[1].HasLoginProfile);
        Assert.False(vm.Accounts[2].HasLoginProfile);
        // 选中的“我的账户”没有 UID，不能测试切换
        Assert.False(vm.TestSwitchCommand.CanExecute(null));

        var page = new PuloniaAccountsPage(vm);
        Layout(page, 1100, 720);
        Assert.True(page.ActualWidth > 0);
        var list = Assert.Single(VisualChildren(page).OfType<ListBox>().Where(box => ReferenceEquals(box.ItemsSource, vm.Accounts)));
        Assert.Equal(Visibility.Visible, ((FrameworkElement)list.Parent).Visibility);
        Assert.Equal(3, list.Items.Count);
        Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(0));
        // 顶部当前账户下拉框按账户 ID 选中本实例当前账户
        var current = Assert.Single(VisualChildren(page).OfType<ComboBox>().Where(box => ReferenceEquals(box.ItemsSource, vm.CurrentAccountOptions)));
        Assert.Equal(settings.Accounts[0].Id, current.SelectedValue);

        // 选中其他账户、切换到“登录资料”页签后重新布局，草稿修改标记为未保存
        vm.SelectedAccount = vm.Accounts[2];
        vm.SelectedDetailTabIndex = 1;
        Layout(page, 1100, 720);
        vm.Accounts[2].ListText = "139****1111";
        Assert.True(vm.HasUnsavedChanges);
        Assert.True(vm.SaveCommand.CanExecute(null));
        Assert.True(vm.TestSwitchCommand.CanExecute(null));
        // 调整顺序后保存，目录按新顺序写入，修改清除
        vm.MoveUpCommand.Execute(null);
        Assert.Equal("丙", vm.Accounts[1].Name);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.HasUnsavedChanges);
        var saved = await accounts.LoadAccountsAsync();
        Assert.Equal(["我的账户", "丙", "乙"], saved.Accounts.Select(account => account.Name));
        Assert.Equal("139****1111", saved.Accounts[1].ListText);

        // “狛荷屋”页签：实际实例化并布局；测试没有导航时按钮不可用，说明文字随所选账户的 UID 变化
        vm.SelectedDetailTabIndex = PuloniaAccountsPageViewModel.AccountRecordsTabIndex;
        Layout(page, 1100, 720);
        var open = Assert.Single(VisualChildren(page).OfType<Button>().Where(button => Equals(button.Content, "打开狛荷屋")));
        Assert.Same(vm.OpenAccountRecordsCommand, open.Command);
        Assert.False(open.IsEnabled);
        Assert.Equal("打开狛荷屋，查看 UID 100000003 的库存记录。", vm.AccountRecordsHint);
        vm.Accounts[1].Uid = "100000009";
        Assert.Equal("UID 有未保存的修改，请先保存，再打开狛荷屋。", vm.AccountRecordsHint);
        vm.SelectedAccount = vm.Accounts[0];
        Assert.StartsWith("这个账户还没有保存 UID", vm.AccountRecordsHint);
    });

    /// <summary>只有一个账户时列表收起、直接显示该账户的详情，页首说明按设计文字。</summary>
    [Fact] public Task PageCollapsesListWithSingleAccount() => RunSta(async () =>
    {
        using var store = new PuloniaTaskStore(_directory);
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider("webview:小号a"));
        await using var service = CreateService(store, accounts);
        var vm = CreatePage(store, accounts, service);
        await vm.LoadAsync(force: false);
        Assert.True(vm.IsSingleAccount);
        Assert.Same(vm.Accounts[0], vm.SelectedAccount);
        Assert.StartsWith("只有一个账号时不需要任何设置。", vm.HeaderText);
        Assert.False(vm.DeleteAccountCommand.CanExecute(null));
        // 云原神实例不能测试切换
        Assert.True(vm.IsCloudInstance);
        Assert.Equal("云原神实例不能切换账户。", vm.TestSwitchHint);

        var page = new PuloniaAccountsPage(vm);
        Layout(page, 900, 640);
        var list = Assert.Single(VisualChildren(page).OfType<ListBox>().Where(box => ReferenceEquals(box.ItemsSource, vm.Accounts)));
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)list.Parent).Visibility);
        // 详情的名称输入框绑定到唯一账户
        Assert.Contains(VisualChildren(page).OfType<TextBox>(), box => box.Text == PuloniaGenshinAccountProfile.MyAccountName);
    });

    /// <summary>删除账户后，它不再被引用的账户图片（含被替换的旧图片）一并删除，其他账户的图片保留。</summary>
    [Fact] public async Task RemovingAccountDeletesItsUnreferencedImages()
    {
        using var store = new PuloniaTaskStore(_directory);
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
        var settings = await accounts.LoadAccountsAsync();
        var target = new PuloniaGenshinAccountProfile { Name = "乙", Uid = "100000002" };
        var other = new PuloniaGenshinAccountProfile { Name = "丙", Uid = "100000003" };
        var oldImage = await store.CreateAccountImageAsync(target.Id, [1, 2, 3]);
        target.ImagePath = await store.CreateAccountImageAsync(target.Id, [4, 5, 6]);
        other.ImagePath = await store.CreateAccountImageAsync(other.Id, [7, 8, 9]);
        settings.Accounts.Add(target);
        settings.Accounts.Add(other);
        settings = (await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>())).Settings;
        Assert.Null(await accounts.CheckAccountRemovalAsync(target.Id));

        settings.Accounts.RemoveAll(account => account.Id == target.Id);
        await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>());
        Assert.False(File.Exists(store.GetAccountImagePath(oldImage)));
        Assert.False(File.Exists(store.GetAccountImagePath(target.ImagePath!)));
        Assert.True(File.Exists(store.GetAccountImagePath(other.ImagePath!)));
    }

    /// <summary>运行账户入口：只有一个账户且跟随当前账户时不显示；只能指定有 UID 的账户；指定的账户不可用时提示。</summary>
    [Fact] public void RunAccountRulesMatchBothPages()
    {
        var my = PuloniaGenshinAccountProfile.CreateMyAccount(PuloniaTaskServerTimeZone.UtcPlus8);
        var alt = new PuloniaGenshinAccountProfile { Name = "乙", Uid = "100000002" };
        Assert.False(PuloniaRunAccountText.ShouldShow(null, 1));
        Assert.True(PuloniaRunAccountText.ShouldShow(null, 2));
        Assert.True(PuloniaRunAccountText.ShouldShow(alt.Id, 1));
        var options = PuloniaRunAccountText.BuildOptions([my, alt]);
        Assert.Equal([PuloniaRunAccountText.FollowCurrentKey, alt.Id], options.Select(option => option.Key));
        Assert.False(PuloniaRunAccountText.CanSpecify(my.Id, [my, alt]));
        Assert.True(PuloniaRunAccountText.CanSpecify(alt.Id, [my, alt]));
        Assert.Equal("运行账户：跟随当前账户", PuloniaRunAccountText.Describe(null, [my, alt]));
        Assert.Equal("运行账户：乙", PuloniaRunAccountText.Describe(alt.Id, [my, alt]));
        Assert.Null(PuloniaRunAccountText.GetProblem(alt.Id, [my, alt]));
        Assert.Contains("已删除", PuloniaRunAccountText.GetProblem("acc_missing", [my]));
        Assert.Contains("没有 UID", PuloniaRunAccountText.GetProblem(my.Id, [my]));
    }

    /// <summary>仅清理本测试创建的独占目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    /// <summary>建立没有执行器、不触碰游戏的任务服务（队列为空，不会执行任何运行）；账户页只用它构造编辑会话。</summary>
    private static PuloniaTaskService CreateService(PuloniaTaskStore store, IPuloniaAccountService accounts)
        => new(store, new PuloniaTaskBuilder(store), [], new PuloniaGameTaskCoordinator(null!),
            new TaskStopService(NullLogger<TaskStopService>.Instance), accountService: accounts);

    /// <summary>建立账户页视图模型：编辑会话与页面共用同一账户服务，游戏操作替换为会使测试失败的空实现。</summary>
    private static PuloniaAccountsPageViewModel CreatePage(PuloniaTaskStore store, IPuloniaAccountService accounts,
        IPuloniaTaskService service)
        => new(accounts, store, new PuloniaGameTaskCoordinator(null!), new NoGameSwitcher(),
            new PuloniaPlanEditingSession(store, new PuloniaTaskClipboardService(), service, new PuloniaTaskResourceCatalog(), accounts));

    /// <summary>按给定尺寸测量与排列，并完成一次布局。</summary>
    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    /// <summary>遍历实际模板生成的控件，验证模板内部绑定而非仅验证外层尺寸。</summary>
    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in VisualChildren(child)) yield return descendant;
        }
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

    /// <summary>页面测试不执行游戏操作；任何调用都使测试失败。</summary>
    private sealed class NoGameSwitcher : IPuloniaGenshinAccountSwitcher
    {
        /// <inheritdoc />
        public Task<string> PrepareAsync(GameRuntime runtime, PuloniaGenshinAccountProfile account, Action switchStarted, CancellationToken ct)
            => throw new InvalidOperationException("页面测试不应切换账户。");

        /// <inheritdoc />
        public Task VerifyAsync(GameRuntime runtime, string expectedUid, CancellationToken ct)
            => throw new InvalidOperationException("页面测试不应核验 UID。");

        /// <inheritdoc />
        public Task<string?> TryReadUidAsync(GameRuntime runtime, CancellationToken ct)
            => throw new InvalidOperationException("页面测试不应读取 UID。");

        /// <inheritdoc />
        public Task<(string Uid, string ImagePath)> CaptureCurrentAccountAsync(GameRuntime runtime, string accountId, CancellationToken ct)
            => throw new InvalidOperationException("页面测试不应采集图片。");
    }
}
