using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Pages;
using BetterGenshinImpact.ViewModel.Pages;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using BetterGenshinImpact.ViewModel.Windows.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 按账户查看路线 CD 与首领领奖的界面逻辑（统筹设计 5.6、9.3）：任务计划页“CD 按账户”的默认值与恢复、运行预览的账户选择、
/// CD 总览的固定账户模式、账户页的“查看路线 CD”请求与“首领领奖”列表，以及首领讨伐设置中按账户显示的累计次数。
/// 只构造视图模型（账户页另在 STA 线程实际实例化并布局），不启动主程序、不显示窗口、不触碰游戏。
/// </summary>
public sealed class PuloniaAccountProgressViewTests : IDisposable
{
    /// <summary>本测试独占的目录，不接触用户真实账户与计划。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>
    /// “CD 按账户”：账户目录读取后才显示；默认按计划的运行账户；临时改选只记在会话中、不修改计划，选回默认传 null；
    /// 切换计划、离开页面时恢复；本实例当前账户变化后跟随当前账户的计划随之改变。
    /// </summary>
    [Fact]
    public async Task TaskPlanCooldownAccount_DefaultsToPlanAccountAndResets()
    {
        using var store = new PuloniaTaskStore(_directory);
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
        var (my, alt) = await AddAltAccountAsync(accounts);
        await using var service = CreateService(store, accounts);
        var session = CreateSession(store, service, accounts);
        var page = new PuloniaTaskPlanViewModel(session, new PuloniaTaskHistoryViewModel(service, store));
        var follow = AddDocument(session, new PuloniaTaskPlan { Name = "跟随", RootTask = new PuloniaTask { Name = "根" } });
        var specified = AddDocument(session,
            new PuloniaTaskPlan { Name = "指定", AccountId = alt.Id, RootTask = new PuloniaTask { Name = "根" } });

        // 1. 账户目录读取前不显示；读取后列出全部账户，跟随当前账户的计划默认按本实例当前账户
        page.SelectedDocument = follow;
        Assert.False(page.ShowCooldownAccountSelector);
        await session.ReloadCooldownsAsync();
        Assert.True(page.ShowCooldownAccountSelector);
        Assert.Equal([my.Id, alt.Id], page.CooldownAccountOptions.Select(option => option.Key));
        Assert.Equal(my.Id, page.SelectedCooldownAccountId);
        Assert.False(page.HasCooldownAccountNotice);

        // 2. 临时改选其他账户：会话记下临时账户，说明运行仍按默认账户，计划不变
        page.SelectedCooldownAccountId = alt.Id;
        Assert.Equal(alt.Id, session.CooldownViewAccountId);
        Assert.Equal(alt.Id, page.SelectedCooldownAccountId);
        Assert.Equal("仅改变本页显示，运行仍按“我的账户”", page.CooldownAccountNotice);
        Assert.Null(follow.Plan.AccountId);

        // 3. 选回默认账户时恢复按计划设置
        page.SelectedCooldownAccountId = my.Id;
        Assert.Null(session.CooldownViewAccountId);
        Assert.False(page.HasCooldownAccountNotice);

        // 4. 切换计划时恢复：指定账户的计划按该账户
        page.SelectedCooldownAccountId = alt.Id;
        page.SelectedDocument = specified;
        Assert.Null(session.CooldownViewAccountId);
        Assert.Equal(alt.Id, page.SelectedCooldownAccountId);

        // 5. 本实例当前账户变化后，跟随当前账户的计划改按新账户
        page.SelectedDocument = follow;
        await accounts.SetCurrentAccountAsync(PuloniaInstanceKey.Primary, alt.Id);
        await session.ReloadCooldownsAsync();
        Assert.Equal(alt.Id, page.SelectedCooldownAccountId);

        // 6. 离开页面时恢复，新一条龙页等共用会话的页面不受影响
        page.SelectedCooldownAccountId = my.Id;
        Assert.Equal(my.Id, session.CooldownViewAccountId);
        await page.OnNavigatedFromAsync();
        Assert.Null(session.CooldownViewAccountId);
        Assert.Equal(alt.Id, session.GetCooldownAccount(follow)?.Id);
    }

    /// <summary>
    /// 运行预览的“CD 按账户”：默认是任务计划页当前显示的账户；改选后设置 PreviewAccountId 并重新预测，
    /// 选回按计划设置的账户时恢复默认；只影响本窗口，不改变页面的临时选择。
    /// </summary>
    [Fact]
    public async Task RunPreviewAccount_StartsFromPageAccountAndOnlyAffectsWindow()
    {
        using var store = new PuloniaTaskStore(_directory);
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
        var (my, alt) = await AddAltAccountAsync(accounts);
        await using var service = new PuloniaTaskService(store, new PuloniaTaskBuilder(store),
            [new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry())], new PuloniaGameTaskCoordinator(null!),
            new TaskStopService(NullLogger<TaskStopService>.Instance), accountService: accounts);
        var session = CreateSession(store, service, accounts);
        var plan = await store.SavePlanAsync(new PuloniaTaskPlan
        {
            Name = "预览",
            RootTask = new PuloniaTask
            {
                Name = "根",
                Children = [new PuloniaTask { Name = "计算", TaskType = "csharp",
                    Parameters = new JObject { ["operation"] = "sample.sum", ["values"] = new JArray(1, 2) } }]
            }
        });
        var document = AddDocument(session, plan);
        await session.ReloadCooldownsAsync();

        // 1. 页面按计划设置显示时，预览也按计划设置（跟随当前账户：我的账户）
        var preview = new PuloniaTaskRunPreviewViewModel(session, document);
        Assert.True(preview.ShowAccountSelector);
        Assert.Equal(my.Id, preview.SelectedAccountId);
        Assert.Null(preview.PreviewAccountId);
        await preview.LoadCommand.ExecuteAsync(null);
        Assert.Null(preview.ErrorText);
        Assert.Contains("CD 按账户：我的账户", preview.SummaryText);

        // 2. 改选后重新预测，说明开始执行后仍按计划的运行账户；页面的选择不变
        preview.SelectedAccountId = alt.Id;
        Assert.Equal(alt.Id, preview.PreviewAccountId);
        await preview.LoadCommand.ExecutionTask!;
        Assert.Contains("CD 按账户：乙", preview.SummaryText);
        Assert.Equal("仅改变本窗口显示，开始执行后仍按“我的账户”判定", preview.AccountNotice);
        Assert.Null(session.CooldownViewAccountId);

        // 3. 页面临时查看其他账户时，新窗口从该账户开始；选回默认账户时恢复按计划设置
        session.SetCooldownViewAccount(alt.Id);
        var fromPage = new PuloniaTaskRunPreviewViewModel(session, document);
        Assert.Equal((alt.Id, alt.Id), (fromPage.SelectedAccountId, fromPage.PreviewAccountId));
        fromPage.SelectedAccountId = my.Id;
        Assert.Null(fromPage.PreviewAccountId);
        Assert.False(fromPage.HasAccountNotice);
        await fromPage.LoadCommand.ExecutionTask!;
        Assert.Contains("CD 按账户：我的账户", fromPage.SummaryText);
        Assert.Equal(alt.Id, session.CooldownViewAccountId);
    }

    /// <summary>CD 总览的固定账户模式：只列出该账户的记录并隐藏账户筛选，关键词筛选照常；尚未选中账户时为空并说明。</summary>
    [Fact]
    public async Task RouteCooldownOverview_FixedAccountListsOnlyThatAccount()
    {
        using var store = new PuloniaTaskStore(_directory);
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
        var (my, alt) = await AddAltAccountAsync(accounts);
        await using var service = CreateService(store, accounts);
        var session = CreateSession(store, service, accounts);
        // 测试数据：我的账户完成“一号”，乙完成“一号”和“二号”（落落莓，成功后 46 小时内仍在 CD）
        await accounts.RecordRouteCompletionAsync(my.Id, Completion("一号"));
        await accounts.RecordRouteCompletionAsync(alt.Id, Completion("一号"));
        await accounts.RecordRouteCompletionAsync(alt.Id, Completion("二号"));
        using var overview = new PuloniaRouteCooldownOverviewViewModel(session);
        await session.ReloadCooldownsAsync();

        // 1. 未固定：列出全部账户的记录，显示账户筛选
        Assert.True(overview.IsAccountFilterVisible);
        Assert.Equal(3, overview.Entries.Count);

        // 2. 固定为乙：只列出乙的记录并隐藏账户筛选，账户筛选的选择不再生效
        overview.SetFixedAccount(alt.Id);
        Assert.False(overview.IsAccountFilterVisible);
        Assert.Equal(2, overview.Entries.Count);
        Assert.All(overview.Entries, entry => Assert.Equal(alt.Id, entry.Key.AccountId));
        overview.SelectedAccount = overview.AccountOptions.First(option => option.Key == my.Id);
        Assert.Equal(2, overview.Entries.Count);
        overview.SearchText = "二号";
        Assert.Equal("二号", Assert.Single(overview.Entries).RouteName);
        overview.SearchText = string.Empty;

        // 3. 尚未选中账户时为空
        overview.SetFixedAccount(null);
        Assert.Empty(overview.Entries);
        Assert.Equal("请先选择账户。", overview.EmptyText);
    }

    /// <summary>
    /// 账户页：“查看路线 CD”请求在进入页面后选中账户、切到“路线 CD”并按计划筛选；“首领领奖”按所选账户列出各计划（含一条龙配置）
    /// 首领讨伐节点当前批次的次数，没有记录时说明；两个新页签都能实际实例化并布局。
    /// </summary>
    [Fact]
    public Task AccountsPage_AppliesRouteCooldownRequestAndListsBossRewards() => RunSta(async () =>
    {
        using var store = new PuloniaTaskStore(_directory);
        var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(PuloniaInstanceKey.Primary));
        var (my, alt) = await AddAltAccountAsync(accounts);
        // 测试数据：普通计划“日常”（分组“周常”下的首领讨伐，批次 batch、上限 10）与乙的一条龙配置各有一个首领讨伐节点
        var boss = new PuloniaTask
        {
            Name = "讨伐", TaskType = BossTaskType,
            Parameters = new JObject
            {
                [PuloniaSoloTaskExecutor.BossRewardCountResetParameter] = "batch",
                [PuloniaSoloTaskExecutor.BossTotalRunCountLimitParameter] = 10
            }
        };
        var plan = await store.SavePlanAsync(new PuloniaTaskPlan
        {
            Name = "日常",
            RootTask = new PuloniaTask { Name = "根", Children = [new PuloniaTask { Name = "周常", TaskType = "group", Children = [boss] }] }
        });
        var dragonBoss = new PuloniaTask { Name = "首领", TaskType = BossTaskType };
        var dragon = await store.SavePlanAsync(new PuloniaTaskPlan
        {
            Name = "乙的一条龙", Purpose = PuloniaTaskPlanPurpose.OneDragon, AccountId = alt.Id,
            RootTask = new PuloniaTask { Name = "根", Children = [dragonBoss] }
        });
        // 乙在“日常”当前批次领奖 2 次（旧批次 1 次不计入），在一条龙中领奖 1 次；我的账户没有记录
        await accounts.IncrementBossRewardCountAsync(alt.Id, plan.Id, boss.Id, "batch");
        await accounts.IncrementBossRewardCountAsync(alt.Id, plan.Id, boss.Id, "batch");
        await accounts.IncrementBossRewardCountAsync(alt.Id, plan.Id, boss.Id, "old");
        await accounts.IncrementBossRewardCountAsync(alt.Id, dragon.Id, dragonBoss.Id, "");
        await using var service = CreateService(store, accounts);
        var vm = new PuloniaAccountsPageViewModel(accounts, store, new PuloniaGameTaskCoordinator(null!), new NoGameSwitcher(),
            CreateSession(store, service, accounts));

        // 1. 先记下请求再进入页面：账户列表就绪后选中乙、切到“路线 CD”，计划选项建立后按一条龙配置筛选
        vm.RequestShowRouteCooldowns(alt.Id, dragon.Id);
        await vm.OnNavigatedToAsync();
        Assert.Equal(alt.Id, vm.SelectedAccount?.Id);
        Assert.Equal(PuloniaAccountsPageViewModel.RouteCooldownTabIndex, vm.SelectedDetailTabIndex);
        Assert.False(vm.RouteCooldowns.IsAccountFilterVisible);
        await vm.RouteCooldowns.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(dragon.Id, vm.RouteCooldowns.SelectedPlan?.Key);

        // 2. 首领领奖：乙在两个计划中的当前批次次数
        await vm.BossRewards.LoadAsync();
        Assert.Equal([("日常", "周常 / 讨伐", "2 次（上限 10）"), ("一条龙：乙的一条龙", "首领", "1 次")],
            vm.BossRewards.Entries.Select(entry => (entry.PlanName, entry.NodeName, entry.CountText)));
        Assert.StartsWith("共 2 个自动首领讨伐任务，其中 2 个", vm.BossRewards.SummaryText);

        // 3. 切换到没有记录的账户：列表为空并说明
        vm.SelectedAccount = vm.Accounts.First(item => item.Id == my.Id);
        await vm.BossRewards.LoadAsync();
        Assert.Empty(vm.BossRewards.Entries);
        Assert.Equal("这个账户在当前重置批次内还没有首领领奖记录。", vm.BossRewards.EmptyText);
        Assert.False(vm.HasUnsavedChanges);

        // 4. 两个新页签都能实际实例化并布局
        var page = new PuloniaAccountsPage(vm);
        vm.SelectedDetailTabIndex = PuloniaAccountsPageViewModel.RouteCooldownTabIndex;
        Layout(page, 1100, 720);
        Assert.Contains(VisualChildren(page).OfType<ListBox>(), list => list.Name == "CooldownList");
        vm.SelectedDetailTabIndex = PuloniaAccountsPageViewModel.BossRewardTabIndex;
        Layout(page, 1100, 720);
        Assert.Contains(VisualChildren(page).OfType<TextBlock>(),
            text => text.Text == vm.BossRewards.EmptyText && text.Visibility == Visibility.Visible);
        await vm.OnNavigatedFromAsync();
    });

    /// <summary>首领讨伐设置：账户目录只有一个账户时只显示一行，不出现账户名。</summary>
    [Fact]
    public async Task AutoBossCount_SingleAccountShowsOneLine()
    {
        using var store = new PuloniaTaskStore(_directory);
        var my = (await store.LoadAccountSettingsAsync()).Accounts[0];
        await store.IncrementBossRewardCountAsync(my.Id, "plan", "task", "");
        var settings = new AutoBossSettingsViewModel(new JObject(), store);
        await settings.SetProgressScopeAsync("plan", "task", my.Id);
        Assert.Equal("累计成功领奖：1 次。", settings.CompletedRunCountText);
    }

    /// <summary>仅清理本测试创建的独占目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    /// <summary>自动首领讨伐节点的任务类型。</summary>
    private static string BossTaskType => PuloniaSoloTaskExecutor.ToTaskType(AutoBossTask.TaskId);

    /// <summary>在“我的账户”之外新增有 UID 的账户“乙”，返回两个已保存的账户。</summary>
    private static async Task<(PuloniaGenshinAccountProfile My, PuloniaGenshinAccountProfile Alt)> AddAltAccountAsync(
        IPuloniaAccountService accounts)
    {
        var settings = await accounts.LoadAccountsAsync();
        settings.Accounts.Add(new PuloniaGenshinAccountProfile { Name = "乙", Uid = "100000002" });
        settings = (await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>())).Settings;
        return (settings.Accounts[0], settings.Accounts[1]);
    }

    /// <summary>建立没有执行器、不触碰游戏的任务服务（队列为空，不会执行任何运行）；只用于构造编辑会话。</summary>
    private static PuloniaTaskService CreateService(PuloniaTaskStore store, IPuloniaAccountService accounts)
        => new(store, new PuloniaTaskBuilder(store), [], new PuloniaGameTaskCoordinator(null!),
            new TaskStopService(NullLogger<TaskStopService>.Instance), accountService: accounts);

    /// <summary>建立与页面共用账户服务的编辑会话。</summary>
    private static PuloniaPlanEditingSession CreateSession(PuloniaTaskStore store, IPuloniaTaskService service,
        IPuloniaAccountService accounts)
        => new(store, new PuloniaTaskClipboardService(), service, new PuloniaTaskResourceCatalog(), accounts);

    /// <summary>把计划直接放入会话的普通计划列表，不经过首次加载。</summary>
    private static PuloniaTaskPlanDocumentViewModel AddDocument(PuloniaPlanEditingSession session, PuloniaTaskPlan plan)
    {
        var document = new PuloniaTaskPlanDocumentViewModel(plan, [], session.Clipboard, isNew: false);
        session.Documents.Add(document);
        return document;
    }

    /// <summary>为“落落莓”目录下的一份路线建立刚刚完成的记录。</summary>
    /// <param name="routeName">路线文件名（不含扩展名）。</param>
    private PuloniaRouteCompletion Completion(string routeName)
    {
        var rule = PuloniaRouteCooldown.Resolve(Path.Combine(_directory, "落落莓", routeName + ".json"), null, new JObject());
        return new PuloniaRouteCompletion
        {
            Route = rule.FileIdentity, CompletedAt = DateTimeOffset.UtcNow,
            RunId = PuloniaId.NewRunId(DateTimeOffset.Now) + BetterGenshinImpact.Helpers.ShortId.Create(null, 4),
            TaskAddress = "plan/task", Attempt = 1, Instance = PuloniaInstanceKey.Primary, Rule = PuloniaRouteCompletionRule.From(rule)
        };
    }

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
