using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Remote;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.Service.Instance;
using BetterGenshinImpact.View.Controls;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 任务计划页与新一条龙页共用的“运行于”（统筹设计 9.4）：默认实例规则、显示条件、单选与多选、子实例下线后恢复默认、
/// 摘要文字，多选提交时本实例本地入队、子实例经替身通道下发各一次，以及视图在 STA 线程的实际实例化与布局。
/// 子实例端点由测试直接提供，下发经记录型替身通道返回运行 ID，不启动主程序、不显示窗口、不连接真实实例、不触碰游戏。
/// </summary>
public sealed class PuloniaRunTargetsTests : IDisposable
{
    /// <summary>本测试独占的目录，不接触用户真实账户与计划。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bgi-" + BetterGenshinImpact.Helpers.ShortId.Create(null, 8));

    /// <summary>主实例（本实例）的实例键。</summary>
    private const string Primary = PuloniaInstanceKey.Primary;

    /// <summary>模拟的在线云原神子实例“A”。</summary>
    private static readonly InstanceEndpoint ChildA = new()
    {
        InstanceType = BetterGiInstanceType.WebView, ProcessId = 2001, WindowsSessionId = 1, InstanceName = "A"
    };

    /// <summary>模拟的在线云原神子实例“B”。</summary>
    private static readonly InstanceEndpoint ChildB = new()
    {
        InstanceType = BetterGiInstanceType.WebView, ProcessId = 2002, WindowsSessionId = 1, InstanceName = "B"
    };

    /// <summary>子实例“A”的实例键（实例名转小写）。</summary>
    private static readonly string KeyA = PuloniaInstanceKey.ForWebView("A");

    /// <summary>子实例“B”的实例键（实例名转小写）。</summary>
    private static readonly string KeyB = PuloniaInstanceKey.ForWebView("B");

    /// <summary>
    /// 默认实例：指定账户时选当前账户就是该账户的第一个实例（本实例在首位）；没有匹配时选本实例；
    /// 跟随当前账户时选本实例，即使其他实例的当前账户“未确定”。
    /// </summary>
    [Fact]
    public void DefaultTargetKey_FollowsPlanAccountRules()
    {
        (string, string?)[] instances = [(Primary, "acc_my"), (KeyA, "acc_alt"), (KeyB, "acc_alt")];

        // 1. 指定账户：当前账户就是该账户的第一个实例
        Assert.Equal(KeyA, PuloniaRunTargetsViewModel.GetDefaultTargetKey("acc_alt", instances, Primary));
        Assert.Equal(Primary, PuloniaRunTargetsViewModel.GetDefaultTargetKey("acc_my", instances, Primary));
        // 2. 没有实例的当前账户是该账户：本实例
        Assert.Equal(Primary, PuloniaRunTargetsViewModel.GetDefaultTargetKey("acc_none", instances, Primary));
        // 3. 跟随当前账户：本实例；“未确定”（null）的当前账户不能与“跟随当前账户”（null）误配
        Assert.Equal(Primary, PuloniaRunTargetsViewModel.GetDefaultTargetKey(null, instances, Primary));
        Assert.Equal(Primary, PuloniaRunTargetsViewModel.GetDefaultTargetKey(null, [(Primary, "acc_my"), (KeyA, null)], Primary));
    }

    /// <summary>
    /// 显示条件：首次读取前与没有在线子实例时隐藏，只有本实例一项；有在线子实例时显示，每项为“实例名 — 当前账户”。
    /// </summary>
    [Fact]
    public async Task Choice_IsVisibleOnlyWithOnlineChildren()
    {
        await using var context = await Context.CreateAsync(_directory);

        // 1. 有子实例：首次读取前隐藏，读取后列出本实例与在线子实例
        var withChildren = context.CreateTargets(() => [ChildA, ChildB]);
        Assert.False(withChildren.IsChoiceVisible);
        Assert.True(Assert.Single(withChildren.Options) is { IsLocal: true, IsSelected: true, InstanceKey: Primary });
        await withChildren.RefreshAsync();
        Assert.True(withChildren.IsChoiceVisible);
        Assert.Equal([Primary, KeyA, KeyB], withChildren.Options.Select(option => option.InstanceKey));
        Assert.Equal(["主实例 — 我的账户", "云原神 · A — 乙", "云原神 · B — 我的账户"],
            withChildren.Options.Select(option => option.Display));
        Assert.Equal([true, false, false], withChildren.Options.Select(option => option.IsLocal));
        // 账户目录有两个账户，可就地修改当前账户
        Assert.All(withChildren.Options, option => Assert.True(option.CanChangeAccount));
        Assert.Equal(string.Empty, withChildren.Message);

        // 2. 主实例但没有在线子实例：隐藏
        var noChildren = context.CreateTargets(() => []);
        await noChildren.RefreshAsync();
        Assert.False(noChildren.IsChoiceVisible);
        Assert.Single(noChildren.Options);

        // 3. 不是主实例（不能下发）：隐藏
        var notRoot = context.CreateTargets(null);
        await notRoot.RefreshAsync();
        Assert.False(notRoot.IsChoiceVisible);
        Assert.Single(notRoot.Options);
    }

    /// <summary>
    /// 指定账户的计划只能单选：默认选中当前账户就是该账户的实例，勾选一个即取消其他；选中的实例下线后按规则恢复默认。
    /// </summary>
    [Fact]
    public async Task SpecifiedAccountPlan_IsSingleSelect_AndRestoresDefaultWhenSelectedGoesOffline()
    {
        await using var context = await Context.CreateAsync(_directory);
        var children = new List<InstanceEndpoint> { ChildA, ChildB };
        var vm = context.CreateTargets(() => children);
        await vm.RefreshAsync();

        // 1. 指定“乙”：单选，默认选中当前账户是乙的 A
        vm.SetPlan("plan-specified", context.Alt.Id);
        Assert.False(vm.IsMultiSelect);
        Assert.All(vm.Options, option => Assert.False(option.IsMultiSelect));
        Assert.StartsWith("计划指定了运行账户", vm.HintText);
        Assert.Equal([KeyA], SelectedKeys(vm));
        Assert.Equal("运行于：云原神 · A", vm.SummaryText);

        // 2. 勾选本实例即取消 A，再勾选 B 即取消本实例
        vm.Options[0].IsSelected = true;
        Assert.Equal([Primary], SelectedKeys(vm));
        vm.Options[2].IsSelected = true;
        Assert.Equal([KeyB], SelectedKeys(vm));
        Assert.Equal("运行于：云原神 · B", vm.SummaryText);
        // 用户的选择在实例集合不变的刷新后保留
        await vm.RefreshAsync();
        Assert.Equal([KeyB], SelectedKeys(vm));

        // 3. B 下线：选中项随选项移除，按规则恢复默认（当前账户是乙的 A）
        children.Remove(ChildB);
        await vm.RefreshAsync();
        Assert.Equal([Primary, KeyA], vm.Options.Select(option => option.InstanceKey));
        Assert.Equal([KeyA], SelectedKeys(vm));
        Assert.Equal("运行于：云原神 · A", vm.SummaryText);
    }

    /// <summary>
    /// 跟随当前账户的计划可多选：默认只选本实例，可同时勾选多个实例；只留下的实例下线后恢复默认；全部取消时摘要提示选择。
    /// </summary>
    [Fact]
    public async Task FollowCurrentAccountPlan_IsMultiSelect_AndSummaryListsSelectedInstances()
    {
        await using var context = await Context.CreateAsync(_directory);
        var children = new List<InstanceEndpoint> { ChildA, ChildB };
        var vm = context.CreateTargets(() => children);
        await vm.RefreshAsync();

        // 1. 跟随当前账户：多选，默认只选本实例
        vm.SetPlan("plan-follow", null);
        Assert.True(vm.IsMultiSelect);
        Assert.All(vm.Options, option => Assert.True(option.IsMultiSelect));
        Assert.StartsWith("计划跟随当前账户", vm.HintText);
        Assert.Equal([Primary], SelectedKeys(vm));
        Assert.Equal("运行于：主实例", vm.SummaryText);

        // 2. 再勾选 A 和 B：三个都选中，摘要按选项顺序列出
        vm.Options[1].IsSelected = true;
        vm.Options[2].IsSelected = true;
        Assert.Equal([Primary, KeyA, KeyB], SelectedKeys(vm));
        Assert.Equal("运行于：主实例、云原神 · A、云原神 · B", vm.SummaryText);
        await vm.RefreshAsync();
        Assert.Equal([Primary, KeyA, KeyB], SelectedKeys(vm));

        // 3. 只留 B，B 下线：没有有效选择，恢复默认（本实例）
        vm.Options[0].IsSelected = false;
        vm.Options[1].IsSelected = false;
        Assert.Equal("运行于：云原神 · B", vm.SummaryText);
        children.Remove(ChildB);
        await vm.RefreshAsync();
        Assert.Equal([Primary], SelectedKeys(vm));
        Assert.Equal("运行于：主实例", vm.SummaryText);

        // 4. 全部取消：摘要提示选择
        vm.Options[0].IsSelected = false;
        Assert.Empty(SelectedKeys(vm));
        Assert.Equal("运行于：请选择实例", vm.SummaryText);

        // 5. 换计划恢复默认选择
        vm.SetPlan("plan-other", null);
        Assert.Equal([Primary], SelectedKeys(vm));
    }

    /// <summary>
    /// 提交目标：组件隐藏时只提交到本实例；不是当前显示的计划（例如从计划卡片直接运行）时按该计划的运行账户设置取默认实例。
    /// </summary>
    [Fact]
    public async Task Targets_UseDefaultForOtherPlans_AndLocalWhenHidden()
    {
        await using var context = await Context.CreateAsync(_directory);
        var vm = context.CreateTargets(() => [ChildA]);
        await vm.RefreshAsync();
        vm.SetPlan("plan-shown", null);
        vm.Options[1].IsSelected = true;
        var shown = CreateDocument(context, "plan-shown", null);
        Assert.Equal([Primary, KeyA], vm.GetTargets(shown).Select(target => target.InstanceKey));

        // 1. 其他计划：指定“乙”时为当前账户是乙的 A，跟随当前账户时为本实例
        Assert.Equal([KeyA], vm.GetTargets(CreateDocument(context, "plan-other", context.Alt.Id)).Select(target => target.InstanceKey));
        Assert.Equal([Primary], vm.GetTargets(CreateDocument(context, "plan-other", null)).Select(target => target.InstanceKey));

        // 2. 没有在线子实例（组件隐藏）：只提交到本实例
        var hidden = context.CreateTargets(() => []);
        await hidden.RefreshAsync();
        var target = Assert.Single(hidden.GetTargets(CreateDocument(context, "plan-other", context.Alt.Id)));
        Assert.True(target.IsLocal);
    }

    /// <summary>
    /// 多选提交：选中本实例与子实例 A 时，本实例本地入队一次，A 经下发通道下发一次（带保存后的修订号），结果按实例返回。
    /// </summary>
    [Fact]
    public async Task Submit_LocalAndChild_EnqueuesLocallyAndDispatchesOnce()
    {
        await using var context = await Context.CreateAsync(_directory);
        var document = await context.AddPlanAsync();
        var vm = context.CreateTargets(() => [ChildA]);
        await vm.RefreshAsync();
        vm.SetPlan(document.Id, null);
        vm.Options.Single(option => option.InstanceKey == KeyA).IsSelected = true;
        IReadOnlyList<PuloniaRunTargetOptionViewModel>? confirmed = null;
        vm.ConfirmTargetsAsync = (_, targets) =>
        {
            confirmed = targets;
            return Task.FromResult<PuloniaRunTargetConfirmation?>(PuloniaRunTargetConfirmation.Proceed);
        };

        var results = await vm.SubmitAsync(document);

        // 1. 确认扩展点收到两个选中实例；结果按实例返回，均已提交
        Assert.Equal([Primary, KeyA], confirmed!.Select(target => target.InstanceKey));
        Assert.Equal([("主实例", true), ("云原神 · A", false)], results.Select(result => (result.InstanceName, result.IsLocal)));
        Assert.All(results, result => Assert.True(result.IsSuccess, result.Error));
        // 2. 本实例本地入队一次并执行完成
        var local = await context.Service.WaitForCompletionAsync(results[0].RunId!).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(PuloniaTaskRunStatus.Succeeded, local.Status);
        Assert.Equal(Primary, local.Instance);
        Assert.Equal(results[0].RunId, Assert.Single(await context.Service.ListRunsAsync()).RunId);
        // 3. 子实例经通道下发一次，带磁盘上的修订号；下发客户端开始跟踪
        var submitted = Assert.Single(context.Transport.Submissions);
        Assert.Same(ChildA, submitted.Target);
        Assert.Equal((document.Id, document.Revision, results[1].RunId), (submitted.Request.PlanId, submitted.Request.PlanRevision, submitted.RunId));
        var tracked = Assert.Single(context.Dispatcher.ListRuns());
        Assert.Equal((results[1].RunId, KeyA), (tracked.RunId, tracked.Instance));
    }

    /// <summary>确认扩展点返回 false 时不提交到任何实例；没有选择实例时展开弹出层提示，也不调用确认扩展点。</summary>
    [Fact]
    public async Task Submit_DeclinedOrNothingSelected_DoesNotSubmit()
    {
        await using var context = await Context.CreateAsync(_directory);
        var document = await context.AddPlanAsync();
        var vm = context.CreateTargets(() => [ChildA]);
        await vm.RefreshAsync();
        vm.SetPlan(document.Id, null);
        vm.Options[1].IsSelected = true;
        var confirmCalls = 0;
        vm.ConfirmTargetsAsync = (_, _) =>
        {
            confirmCalls++;
            return Task.FromResult<PuloniaRunTargetConfirmation?>(null);
        };

        // 1. 确认未通过：不入队也不下发
        Assert.Empty(await vm.SubmitAsync(document));
        Assert.Equal(1, confirmCalls);
        Assert.Empty(context.Transport.Submissions);
        Assert.Empty(context.Dispatcher.ListRuns());
        Assert.Empty(await context.Service.ListRunsAsync());

        // 2. 没有选择实例：提示并展开，不调用确认扩展点；重新选择后提示清除
        foreach (var option in vm.Options)
            option.IsSelected = false;
        Assert.Empty(await vm.SubmitAsync(document));
        Assert.Equal(1, confirmCalls);
        Assert.True(vm.IsOpen);
        Assert.Equal("请至少选择一个实例。", vm.Message);
        Assert.True(vm.HasMessage);
        vm.Options[0].IsSelected = true;
        Assert.False(vm.HasMessage);
        Assert.Empty(context.Transport.Submissions);
    }

    /// <summary>
    /// 视图：在 STA 线程实际实例化“运行于”并布局，收起时按钮显示摘要；弹出层中的选项列表（不打开弹出窗口）单独布局，
    /// 多选时每行显示复选框、单选时显示单选按钮，勾选状态与视图模型一致。
    /// </summary>
    [Fact]
    public Task View_InstantiatesAndLaysOut() => RunSta(async () =>
    {
        await using var context = await Context.CreateAsync(_directory);
        var vm = context.CreateTargets(() => [ChildA]);
        await vm.RefreshAsync();
        vm.SetPlan("plan-view", null);

        // 1. 收起状态：按钮显示摘要
        var view = new PuloniaRunTargetsView { DataContext = vm };
        Layout(view, 280, 40);
        Assert.True(view.ActualWidth > 0);
        Assert.Contains(VisualChildren(view).OfType<TextBlock>(), text => text.Text == "运行于：主实例");

        // 2. 弹出层中的选项列表：多选时显示复选框
        var items = Assert.Single(LogicalDescendants(view).OfType<ItemsControl>()
            .Where(control => ReferenceEquals(control.ItemsSource, vm.Options)));
        Layout(items, 460, 200);
        var checkBoxes = VisualChildren(items).OfType<CheckBox>().ToArray();
        Assert.Equal(2, checkBoxes.Length);
        Assert.All(checkBoxes, box => Assert.Equal(Visibility.Visible, box.Visibility));
        Assert.Equal([true, false], checkBoxes.Select(box => box.IsChecked == true));
        Assert.All(VisualChildren(items).OfType<RadioButton>(), radio => Assert.Equal(Visibility.Collapsed, radio.Visibility));

        // 3. 指定账户：改为单选按钮，选中当前账户是乙的 A
        vm.SetPlan("plan-view-specified", context.Alt.Id);
        Layout(items, 460, 200);
        var radios = VisualChildren(items).OfType<RadioButton>().ToArray();
        Assert.Equal(2, radios.Length);
        Assert.All(radios, radio => Assert.Equal(Visibility.Visible, radio.Visibility));
        Assert.Equal([false, true], radios.Select(radio => radio.IsChecked == true));
    });

    /// <summary>仅清理本测试创建的独占目录。</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    /// <summary>按给定尺寸测量与排列，并完成一次布局。</summary>
    /// <param name="element">要布局的元素</param>
    /// <param name="width">可用宽度</param>
    /// <param name="height">可用高度</param>
    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    /// <summary>遍历实际模板生成的控件，验证模板内部绑定而非仅验证外层尺寸。</summary>
    /// <param name="parent">起点</param>
    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in VisualChildren(child)) yield return descendant;
        }
    }

    /// <summary>遍历逻辑树；弹出层未打开时其内容不在可视树中，只能经逻辑树找到。</summary>
    /// <param name="parent">起点</param>
    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in LogicalDescendants(child)) yield return descendant;
        }
    }

    /// <summary>给异步 ViewModel 提供 WPF 调度上下文，完成后结束线程。</summary>
    /// <param name="action">在 STA 线程执行的测试内容</param>
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

    /// <summary>选中实例的实例键，按选项顺序。</summary>
    /// <param name="vm">“运行于”</param>
    private static string[] SelectedKeys(PuloniaRunTargetsViewModel vm)
        => vm.Options.Where(option => option.IsSelected).Select(option => option.InstanceKey).ToArray();

    /// <summary>建立只用于读取计划 ID 与运行账户设置的未保存计划文档。</summary>
    /// <param name="context">测试装置</param>
    /// <param name="planId">计划 ID</param>
    /// <param name="accountId">运行账户设置；为空表示跟随当前账户</param>
    private static PuloniaTaskPlanDocumentViewModel CreateDocument(Context context, string planId, string? accountId)
        => new(new PuloniaTaskPlan { Id = planId, Name = planId, AccountId = accountId, RootTask = new PuloniaTask { Name = "根" } },
            [], context.Session.Clipboard, isNew: true);

    /// <summary>
    /// 测试装置：账户目录有“我的账户”与“乙”，子实例 A 的当前账户为乙；编辑会话使用纯 C# 任务服务与记录型下发通道。
    /// </summary>
    private sealed class Context : IAsyncDisposable
    {
        /// <summary>共享的 Pulonia 存储。</summary>
        public required PuloniaTaskStore Store { get; init; }

        /// <summary>主实例的任务服务，执行本地入队的运行。</summary>
        public required PuloniaTaskService Service { get; init; }

        /// <summary>记录下发请求的替身通道。</summary>
        public required RecordingRunTransport Transport { get; init; }

        /// <summary>主实例的下发客户端。</summary>
        public required PuloniaRunDispatcher Dispatcher { get; init; }

        /// <summary>编辑会话。</summary>
        public required PuloniaPlanEditingSession Session { get; init; }

        /// <summary>有 UID 的账户“乙”，子实例 A 的当前账户。</summary>
        public required PuloniaGenshinAccountProfile Alt { get; init; }

        /// <summary>
        /// 建立装置。
        /// </summary>
        /// <param name="directory">测试目录</param>
        public static async Task<Context> CreateAsync(string directory)
        {
            var store = new PuloniaTaskStore(directory);
            var accounts = new PuloniaAccountService(store, new PuloniaInstanceKeyProvider(Primary));
            // 1. 账户目录：我的账户加“乙”；A 的当前账户为乙，B 首次读取时初始化为第一个账户
            var settings = await accounts.LoadAccountsAsync();
            settings.Accounts.Add(new PuloniaGenshinAccountProfile { Name = "乙", Uid = "100000002" });
            settings = (await accounts.SaveAccountsAsync(settings, new Dictionary<string, (string, string)>())).Settings;
            var alt = settings.Accounts[1];
            await accounts.SetCurrentAccountAsync(KeyA, alt.Id);

            // 2. 主实例的任务服务、下发客户端与编辑会话
            var service = new PuloniaTaskService(store, new PuloniaTaskBuilder(store),
                [new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry())], new PuloniaGameTaskCoordinator(null!),
                new TaskStopService(NullLogger<TaskStopService>.Instance), accountService: accounts);
            var transport = new RecordingRunTransport();
            var dispatcher = new PuloniaRunDispatcher(() => transport);
            var session = new PuloniaPlanEditingSession(store, new PuloniaTaskClipboardService(), service,
                new PuloniaTaskResourceCatalog(), accounts, dispatcher);
            return new Context
            {
                Store = store, Service = service, Transport = transport, Dispatcher = dispatcher, Session = session, Alt = alt
            };
        }

        /// <summary>以指定的在线子实例来源建立“运行于”。</summary>
        /// <param name="listChildren">列出在线子实例；为空表示不是主实例</param>
        public PuloniaRunTargetsViewModel CreateTargets(Func<IReadOnlyList<InstanceEndpoint>>? listChildren)
            => new(Session, listChildren);

        /// <summary>保存一个只有 C# 求和节点的计划，并直接放入会话的普通计划列表。</summary>
        public async Task<PuloniaTaskPlanDocumentViewModel> AddPlanAsync()
        {
            var plan = await Store.SavePlanAsync(new PuloniaTaskPlan
            {
                Name = "运行于",
                RootTask = new PuloniaTask
                {
                    Name = "根",
                    Children = [new PuloniaTask { Name = "计算", TaskType = "csharp",
                        Parameters = new JObject { ["operation"] = "sample.sum", ["values"] = new JArray(1, 2) } }]
                }
            });
            var document = new PuloniaTaskPlanDocumentViewModel(plan, [], Session.Clipboard, isNew: false);
            document.SetDefinitions(Service.Definitions);
            Session.Documents.Add(document);
            return document;
        }

        /// <summary>释放任务服务与存储。</summary>
        public async ValueTask DisposeAsync()
        {
            await Service.DisposeAsync();
            Store.Dispose();
        }
    }

    /// <summary>
    /// 记录下发请求的替身通道：子实例立即接受并返回新运行 ID；本测试不停止、不上报运行。
    /// </summary>
    private sealed class RecordingRunTransport : IPuloniaRunTransport
    {
        /// <summary>保护 <see cref="_submissions"/>。</summary>
        private readonly object _gate = new();

        /// <summary>收到的下发请求与返回的运行 ID，按到达顺序。</summary>
        private readonly List<(InstanceEndpoint Target, PuloniaRunSubmitRequest Request, string RunId)> _submissions = [];

        /// <summary>收到的下发请求快照。</summary>
        public (InstanceEndpoint Target, PuloniaRunSubmitRequest Request, string RunId)[] Submissions
        {
            get
            {
                lock (_gate)
                    return [.. _submissions];
            }
        }

        /// <inheritdoc />
        public event Action<InstanceEndpoint>? ChildInstanceDisconnected { add { } remove { } }

        /// <inheritdoc />
        public event Action? RootConnected { add { } remove { } }

        /// <inheritdoc />
        public Task<PuloniaRunSubmitResponse> SubmitRunAsync(InstanceEndpoint target, PuloniaRunSubmitRequest request, CancellationToken ct)
        {
            var runId = PuloniaId.NewRunId(DateTimeOffset.Now);
            lock (_gate)
                _submissions.Add((target, request, runId));
            return Task.FromResult(new PuloniaRunSubmitResponse { RunId = runId });
        }

        /// <inheritdoc />
        public Task CancelRunAsync(InstanceEndpoint target, PuloniaRunCancelRequest request, CancellationToken ct)
            => throw new InvalidOperationException("本测试不停止下发的运行。");

        /// <inheritdoc />
        public Task ReportRunChangedAsync(PuloniaRunChangedReport report, CancellationToken ct)
            => throw new InvalidOperationException("主实例不上报运行状态。");
    }
}
