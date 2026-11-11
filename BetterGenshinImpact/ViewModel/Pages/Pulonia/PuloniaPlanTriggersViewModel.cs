using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.View.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 一份计划的定时与快捷键触发配置：列表、独立草稿、启用开关和运行反馈。
/// 任务计划页的“触发方式”页签与一条龙的触发设置窗口共用此视图模型，保存经编辑会话写入同一文档。
/// </summary>
public partial class PuloniaPlanTriggersViewModel : ObservableObject, IDisposable
{
    /// <summary>负责保存计划文档的编辑会话。</summary>
    private readonly PuloniaPlanEditingSession _session;
    /// <summary>提供运行时状态的唯一调度宿主；测试中可为空。</summary>
    private readonly PuloniaTaskTriggerHost? _triggerHost;
    /// <summary>可选为执行实例的实例、在线判断与其他实例的触发游标（统筹设计 9.5）。</summary>
    private readonly PuloniaTriggerInstanceCatalog _instances;
    /// <summary>其他实例状态的刷新间隔：在线判断要短暂打开在线标记文件，不随每秒的调度通知读取。</summary>
    private static readonly TimeSpan OtherInstancesRefreshInterval = TimeSpan.FromSeconds(10);
    /// <summary>最近一次读取的其他实例状态：实例键 → 是否在线与游标。</summary>
    private IReadOnlyDictionary<string, (bool Online, IReadOnlyList<PuloniaTaskTriggerState> Cursors)> _otherInstances =
        new Dictionary<string, (bool, IReadOnlyList<PuloniaTaskTriggerState>)>();
    /// <summary>上次读取其他实例状态的时间；为空表示尚未读取。</summary>
    private DateTimeOffset? _otherInstancesReadAt;
    /// <summary>是否正在读取其他实例状态。</summary>
    private bool _isReadingOtherInstances;
    /// <summary>列表最后显示的配置版本，树编辑不覆盖尚未保存的触发器草稿。</summary>
    private string _triggerListRevision = "";
    /// <summary>卡片开关保存期间仅刷新列表，保留编辑器内其他尚未保存的字段。</summary>
    private bool _preserveTriggerDraft;
    /// <summary>当前编辑的计划；切换后重建列表并放弃旧计划的草稿。</summary>
    [ObservableProperty] private PuloniaTaskPlanDocumentViewModel? _document;
    /// <summary>当前计划的触发器列表。</summary>
    public ObservableCollection<PuloniaTaskTriggerItemViewModel> Triggers { get; } = [];
    /// <summary>选中的已保存触发器，新增草稿没有对应列表项。</summary>
    [ObservableProperty] private PuloniaTaskTriggerItemViewModel? _selectedTrigger;
    /// <summary>当前可修改的独立草稿。</summary>
    [ObservableProperty] private PuloniaTaskTriggerEditorViewModel? _triggerEditor;
    /// <summary>程序内调度、热键注册和错误反馈。</summary>
    [ObservableProperty] private string _triggerStatus = "定时与快捷键只在 BGI 运行时生效。";
    /// <summary>最近一次保存、启用或删除的结果。</summary>
    [ObservableProperty] private string _message = "";
    /// <summary>正在保存触发配置，期间禁用触发器界面。</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>建立触发配置视图模型，订阅文档变化和调度宿主反馈。</summary>
    /// <param name="session">编辑会话。</param>
    /// <param name="triggerHost">调度宿主；为空时只显示配置。</param>
    /// <param name="instances">实例目录；为空时只列出主实例、桌面分身与 Pulonia 实例目录中的实例，不显示其他实例的游标。</param>
    public PuloniaPlanTriggersViewModel(PuloniaPlanEditingSession session, PuloniaTaskTriggerHost? triggerHost,
        PuloniaTriggerInstanceCatalog? instances = null)
    {
        _session = session;
        _triggerHost = triggerHost;
        _instances = instances ?? new PuloniaTriggerInstanceCatalog(session.Store, session.AccountService);
        _session.DocumentChanged += OnDocumentContentOrStateChanged;
        if (_triggerHost is not null) _triggerHost.Changed += OnTriggerHostChanged;
    }

    /// <summary>切换计划后重建列表。</summary>
    partial void OnDocumentChanged(PuloniaTaskPlanDocumentViewModel? value)
    {
        Message = "";
        Refresh(force: true);
    }

    /// <summary>当前计划被编辑、撤销或保存后刷新列表；只在触发配置变化时重建。</summary>
    private void OnDocumentContentOrStateChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, Document))
            Refresh();
    }

    /// <summary>切换计划或撤销触发配置后刷新列表，保留仍存在的选择。</summary>
    /// <param name="force">为 true 时无条件重建并放弃草稿。</param>
    private void Refresh(bool force = false)
    {
        var document = Document;
        var revision = document is null ? "" : document.Id + "/"
            + string.Join("|", document.Plan.Triggers.Select(trigger
                => trigger.Id + ":" + trigger.ActivatedAtUtc.UtcTicks));
        if (!force && revision == _triggerListRevision)
        {
            UpdateTriggerStatus();
            return;
        }
        _triggerListRevision = revision;
        var id = force ? null : SelectedTrigger?.Trigger.Id;
        var draft = !force && _preserveTriggerDraft ? TriggerEditor : null;
        SelectedTrigger = null;
        TriggerEditor = null;
        Triggers.Clear();
        if (document is not null)
            foreach (var trigger in document.Plan.Triggers) Triggers.Add(new PuloniaTaskTriggerItemViewModel(trigger));
        SelectedTrigger = Triggers.FirstOrDefault(item => item.Trigger.Id == id)
            ?? (draft is not null ? null : Triggers.FirstOrDefault());
        if (draft is not null)
        {
            if (SelectedTrigger is not null) draft.SynchronizeEnabled(SelectedTrigger.Trigger);
            TriggerEditor = draft;
        }
        UpdateTriggerStatus();
    }

    /// <summary>列表选择建立草稿，不直接回写自动保存文档。</summary>
    partial void OnSelectedTriggerChanged(PuloniaTaskTriggerItemViewModel? value)
        => TriggerEditor = value is not null && Document is not null ? CreateEditor(value.Trigger, Document.Plan) : null;

    /// <summary>建立触发器草稿，执行实例选项列出全部已知实例（含已保存的执行实例）。</summary>
    /// <param name="trigger">要编辑的触发器</param>
    /// <param name="plan">触发器所属计划</param>
    private PuloniaTaskTriggerEditorViewModel CreateEditor(PuloniaTaskTrigger trigger, PuloniaTaskPlan plan)
        => new(trigger, plan, _instances.ListInstances(trigger.Instances));

    /// <summary>在 UI 线程合并最新的平台状态和当前计划的触发反馈。</summary>
    private void UpdateTriggerStatus()
    {
        TriggerStatus = _triggerHost?.Status ?? "调度服务不可用，定时与快捷键暂不会执行。";
        var local = _instances.CurrentInstanceKey;
        foreach (var item in Triggers)
        {
            var trigger = item.Trigger;
            // 本实例负责的部分：本实例负责的定时，或主实例上的热键
            var handledHere = trigger.IsScheduledOn(local)
                              || trigger.Kind == PuloniaTaskTriggerKind.Hotkey && local == PuloniaInstanceKey.Primary;
            item.Update(_triggerHost?.Statuses.FirstOrDefault(status => status.PlanId == Document?.Id && status.TriggerId == trigger.Id),
                handledHere, DescribeOtherInstances(trigger, local));
            if (Document?.LastSaveError is { } error)
                item.RuntimeText = "配置尚未保存，仍按上次保存的设置执行：" + error;
        }
        // 其他实例的在线状态与游标按较长间隔读取
        if (_otherInstancesReadAt is not { } readAt || DateTimeOffset.UtcNow - readAt >= OtherInstancesRefreshInterval)
            _ = RefreshOtherInstancesAsync();
    }

    /// <summary>
    /// 其他执行实例的状态行（统筹设计 9.5）：例如“云原神 · 小号a：在线，已处理到 04:30”“云原神 · 小号b：未运行”；
    /// 游标在提交、合并和遗漏时都会前移，是否真的提交了运行要看执行记录。
    /// </summary>
    /// <param name="trigger">定时触发器</param>
    /// <param name="local">本实例的实例键</param>
    private IReadOnlyList<string> DescribeOtherInstances(PuloniaTaskTrigger trigger, string local)
    {
        if (trigger.Kind != PuloniaTaskTriggerKind.Schedule || Document is not { } document)
            return [];
        var keys = trigger.Instances.Count == 0 ? [PuloniaInstanceKey.Primary] : trigger.Instances;
        var lines = new List<string>();
        foreach (var key in keys.Where(key => key != local))
        {
            var name = PuloniaInstanceKey.Describe(key);
            if (!_instances.Exists(key))
            {
                lines.Add(name + "：执行实例不存在，没有实例会检查这个定时。");
                continue;
            }
            if (!_otherInstances.TryGetValue(key, out var info))
            {
                lines.Add(name + "：正在读取状态…");
                continue;
            }
            var cursor = info.Cursors.FirstOrDefault(item => item.PlanId == document.Id && item.TriggerId == trigger.Id
                                                             && item.TriggerActivatedAtUtc == trigger.ActivatedAtUtc);
            var processed = cursor?.LastOccurrenceUtc is { } last
                ? "已处理到 " + last.ToOffset(PuloniaTaskSchedule.GetUtcOffset(trigger.ServerTimeZone)).ToString("MM-dd HH:mm")
                : "尚未处理过";
            lines.Add(info.Online ? $"{name}：在线，{processed}" : $"{name}：未运行（{processed}，启动后按补触发规则处理）");
        }
        return lines;
    }

    /// <summary>读取其他实例的在线状态与触发游标，完成后在 UI 线程刷新列表；读取期间不重复发起。</summary>
    private async Task RefreshOtherInstancesAsync()
    {
        if (_isReadingOtherInstances)
            return;
        _isReadingOtherInstances = true;
        _otherInstancesReadAt = DateTimeOffset.UtcNow;
        try
        {
            var keys = Triggers.SelectMany(item => item.Trigger.Instances.Count == 0
                    ? [PuloniaInstanceKey.Primary]
                    : item.Trigger.Instances)
                .Where(key => key != _instances.CurrentInstanceKey).Distinct(StringComparer.Ordinal).ToArray();
            if (keys.Length == 0)
                return;
            var cursors = await _instances.ListOtherCursorsAsync();
            var infos = await Task.Run(() => keys.ToDictionary(key => key,
                key => (_instances.IsOnline(key), cursors.GetValueOrDefault(key) ?? []), StringComparer.Ordinal));
            _otherInstances = infos;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            _isReadingOtherInstances = false;
        }
        UpdateTriggerStatus();
    }

    /// <summary>后台通知只安排 UI 工作，不从计时器线程修改可观察集合。</summary>
    private void OnTriggerHostChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.HasShutdownStarted)
            dispatcher.BeginInvoke(new Action(UpdateTriggerStatus));
    }

    /// <summary>
    /// 新增默认关闭的每日 4 点定时草稿；计划指定了账户时，Cron 的服务器时区默认取该账户的服务器（统筹设计 6.4.2），否则为 UTC+8。
    /// </summary>
    [RelayCommand]
    private async Task AddScheduleTriggerAsync()
    {
        if (Document is not { } document) return;
        var trigger = new PuloniaTaskTrigger();
        if (document.Plan.AccountId is { } accountId)
        {
            try
            {
                if ((await _instances.Accounts.LoadAccountsAsync()).Accounts.FirstOrDefault(account => account.Id == accountId) is { } account)
                    trigger.ServerTimeZone = account.ServerTimeZone;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // 账户目录读不出时沿用默认时区，用户可以在编辑区修改
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }
        if (!ReferenceEquals(Document, document)) return;
        SelectedTrigger = null;
        TriggerEditor = CreateEditor(trigger, document.Plan);
    }

    /// <summary>新增默认关闭的快捷键草稿，默认采用软件推荐的键鼠监听模式和 F8；可在编辑区切换为全局热键。</summary>
    [RelayCommand]
    private void AddHotkeyTrigger()
    {
        if (Document is null) return;
        SelectedTrigger = null;
        TriggerEditor = CreateEditor(new PuloniaTaskTrigger
        { Kind = PuloniaTaskTriggerKind.Hotkey, HotkeyType = HotKeyTypeEnum.KeyboardMonitor, Hotkey = "F8" }, Document.Plan);
    }

    /// <summary>卡片开关独立修改启用状态并立即保存，不提交右侧尚未保存的配置字段。</summary>
    /// <param name="item">被切换的触发器卡片。</param>
    [RelayCommand]
    private async Task ToggleTriggerEnabledAsync(PuloniaTaskTriggerItemViewModel? item)
    {
        var document = Document;
        if (document is null || item is null || IsBusy || _session.IsBusy || !Triggers.Contains(item))
        {
            item?.RefreshEnabledBinding();
            return;
        }
        IsBusy = true;
        try
        {
            var saved = document.Plan.Triggers.FirstOrDefault(trigger => trigger.Id == item.Trigger.Id);
            if (saved is null) return;
            var trigger = saved.Clone();
            trigger.Enabled = !saved.Enabled;
            trigger.ActivatedAtUtc = DateTimeOffset.UtcNow;
            if (trigger.Enabled && trigger.BusyPolicy == PuloniaTaskBusyPolicy.StopCurrent)
            {
                var answer = await ThemedMessageBox.ShowAsync("该触发器会停止当前任务，并在清理完成后执行新请求。确认启用？",
                    "停止当前任务策略", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) return;
            }
            // 使用已保存字段构建候选；即使当前编辑草稿无效，也不会被开关操作带入计划。
            var candidate = PuloniaTaskJson.ClonePlan(document.Plan);
            candidate.Triggers[candidate.Triggers.FindIndex(value => value.Id == trigger.Id)] = trigger;
            PuloniaTaskValidator.ValidatePlan(candidate);
            _preserveTriggerDraft = true;
            document.ApplyMutation(() =>
                document.Plan.Triggers[document.Plan.Triggers.FindIndex(value => value.Id == trigger.Id)] = trigger,
                document.SelectedNode);
            if (await _session.SaveDocumentAsync(document))
                Message = $"触发器“{trigger.DisplayText}”已{(trigger.Enabled ? "启用" : "停用")}。右侧未保存的修改仍保留。";
            Refresh();
        }
        catch (Exception ex) { await ThemedMessageBox.ErrorAsync(ex.Message, "无法切换触发器状态"); }
        finally
        {
            _preserveTriggerDraft = false;
            item.RefreshEnabledBinding();
            IsBusy = false;
        }
    }

    /// <summary>校验草稿、纳入撤销并立即保存；只有保存成功后调度宿主才读取新配置。</summary>
    [RelayCommand]
    private async Task SaveTriggerAsync()
    {
        var document = Document;
        var editor = TriggerEditor;
        if (document is null || editor is null || IsBusy || _session.IsBusy || !editor.IsValid) return;
        try
        {
            var trigger = editor.CreateTrigger();
            if (trigger.BusyPolicy == PuloniaTaskBusyPolicy.StopCurrent && trigger.Enabled)
            {
                var answer = await ThemedMessageBox.ShowAsync("该触发器到点会停止当前任务，并在清理完成后执行新请求。确认启用此策略？",
                    "停止当前任务策略", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) return;
            }
            // 执行实例的账户检查（统筹设计 4.5、9.5）：有问题时说明，用户可以坚持保存
            if (await FindInstanceAccountProblemsAsync(document.Plan, trigger) is { Count: > 0 } problems)
            {
                var answer = await ThemedMessageBox.ShowAsync("保存前发现以下情况，到点时相关的运行可能进入待处理：\n\n"
                        + string.Join("\n", problems.Select(problem => "• " + problem))
                        + "\n\n仍然保存吗？可以先在标题栏或账户页修改这些实例的当前账户。",
                    "检查执行实例", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) return;
            }
            IsBusy = true;
            // 先校验候选整个计划，避免目标被树编辑删除后将无效草稿装入文档。
            var candidate = PuloniaTaskJson.ClonePlan(document.Plan);
            candidate.Triggers.RemoveAll(item => item.Id == trigger.Id);
            candidate.Triggers.Add(trigger);
            PuloniaTaskValidator.ValidatePlan(candidate);
            document.ApplyMutation(() =>
            {
                document.Plan.Triggers.RemoveAll(item => item.Id == trigger.Id);
                document.Plan.Triggers.Add(trigger);
            }, document.SelectedNode);
            if (await _session.SaveDocumentAsync(document))
            {
                Refresh();
                SelectedTrigger = Triggers.FirstOrDefault(item => item.Trigger.Id == trigger.Id);
                Message = trigger.Enabled ? "触发器已保存，稍后开始生效。" : "触发器已保存；使用列表卡片上的开关启用。";
            }
        }
        catch (Exception ex) { await ThemedMessageBox.ErrorAsync(ex.Message, "触发配置无效"); }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 保存定时触发器前检查执行实例的当前账户：选了多个执行实例而它们的当前账户相同；计划指定账户、执行实例是云原神，
    /// 而该实例的当前账户不是这个账户（云原神实例不能切换账户）。读不出时不阻止保存。
    /// </summary>
    /// <param name="plan">触发器所属计划</param>
    /// <param name="trigger">要保存的触发器</param>
    /// <returns>问题说明；没有时为空</returns>
    private async Task<IReadOnlyList<string>> FindInstanceAccountProblemsAsync(PuloniaTaskPlan plan, PuloniaTaskTrigger trigger)
    {
        if (trigger.Kind != PuloniaTaskTriggerKind.Schedule)
            return [];
        var keys = trigger.Instances.Count == 0 ? [PuloniaInstanceKey.Primary] : trigger.Instances;
        try
        {
            var accounts = (await _instances.Accounts.LoadAccountsAsync()).Accounts;
            string Name(string? id) => accounts.FirstOrDefault(account => account.Id == id)?.Name ?? "未确定";
            var currents = new List<(string Key, string? AccountId)>();
            foreach (var key in keys)
                currents.Add((key, await _instances.Accounts.GetCurrentAccountIdAsync(key)));
            var problems = new List<string>();
            // 1. 跟随当前账户、选了多个执行实例：当前账户相同的实例到点时不能同时运行
            if (plan.AccountId is null)
            {
                foreach (var group in currents.Where(item => item.AccountId is not null)
                             .GroupBy(item => item.AccountId!, StringComparer.Ordinal).Where(group => group.Count() > 1))
                    problems.Add($"{string.Join("、", group.Select(item => PuloniaInstanceKey.Describe(item.Key)))}的当前账户都是“{Name(group.Key)}”，"
                                 + "同一账户不能同时在两个实例运行，后开始的会进入待处理。请为每个实例设置不同的当前账户。");
                foreach (var item in currents.Where(item => item.AccountId is null))
                    problems.Add($"{PuloniaInstanceKey.Describe(item.Key)}的当前账户未确定，到点时会进入待处理。");
            }
            // 2. 指定账户、执行实例是云原神：云原神实例不能切换账户，到点时需要该实例登录着这个账户
            else
            {
                foreach (var item in currents.Where(item => item.Key.StartsWith(PuloniaInstanceKey.WebViewPrefix, StringComparison.Ordinal)
                                                            && item.AccountId != plan.AccountId))
                    problems.Add($"云原神实例不能切换账户，到点时需要{PuloniaInstanceKey.Describe(item.Key)}登录着“{Name(plan.AccountId)}”"
                                 + $"（它现在的当前账户是“{Name(item.AccountId)}”）。");
            }
            return problems;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return [];
        }
    }

    /// <summary>删除需明确确认；右键菜单传入被点击项，底部按钮缺省使用当前选中触发器。</summary>
    /// <param name="item">要删除的触发器卡片；为空时使用当前选中项。</param>
    [RelayCommand]
    private async Task DeleteTriggerAsync(PuloniaTaskTriggerItemViewModel? item = null)
    {
        var document = Document;
        // 传入项必须仍在当前列表中，避免列表刷新后的过期引用误删其他触发器。
        var target = item is null ? SelectedTrigger : Triggers.Contains(item) ? item : null;
        var trigger = target?.Trigger;
        if (document is null || trigger is null || IsBusy || _session.IsBusy) return;
        var answer = await ThemedMessageBox.ShowAsync($"删除触发器“{trigger.DisplayText}”？已保存的执行记录不会删除。",
            "删除触发器", MessageBoxButton.YesNo, ThemedMessageBox.MessageBoxIcon.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        document.ApplyMutation(() => document.Plan.Triggers.RemoveAll(saved => saved.Id == trigger.Id), document.SelectedNode);
        if (await _session.SaveDocumentAsync(document))
            Message = $"已删除触发器“{trigger.DisplayText}”。";
        Refresh();
    }

    /// <summary>放弃当前触发器草稿，恢复已保存配置或关闭新增表单。</summary>
    [RelayCommand]
    private void CancelTriggerEdit()
        => TriggerEditor = SelectedTrigger is not null && Document is not null
            ? CreateEditor(SelectedTrigger.Trigger, Document.Plan) : null;

    /// <summary>窗口关闭或页面释放时解除会话与调度宿主的事件订阅。</summary>
    public void Dispose()
    {
        _session.DocumentChanged -= OnDocumentContentOrStateChanged;
        if (_triggerHost is not null) _triggerHost.Changed -= OnTriggerHostChanged;
    }
}
