using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Remote;
using BetterGenshinImpact.Service.Instance;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 任务计划页与新一条龙页共用的“运行于”（统筹设计 9.4）：只在主实例且有在线子实例时显示，选项为主实例加各在线子实例，
/// 每项显示“实例名 — 当前账户”，账户目录有多个账户时可就地修改该实例的当前账户。
/// 计划指定账户时只能单选，默认选中当前账户就是该账户的实例，没有则主实例；跟随当前账户时可多选，默认只选主实例。
/// 提交时每个选中实例各提交一次：主实例本地入队，子实例经一步下发（共用一次保存后的修订号）。子实例不显示本组件，运行提交到本实例。
/// </summary>
public sealed partial class PuloniaRunTargetsViewModel : ObservableObject
{
    /// <summary>没有选中任何实例时的提示。</summary>
    private const string NoSelectionMessage = "请至少选择一个实例。";

    /// <summary>编辑会话：账户服务与运行提交。</summary>
    private readonly PuloniaPlanEditingSession _session;

    /// <summary>主实例的多实例服务，用于订阅子实例上线下线；子实例或测试中为空。</summary>
    private readonly InstanceService? _instances;

    /// <summary>列出在线子实例；只有主实例能下发，子实例与测试直接构造时为空。</summary>
    private readonly Func<IReadOnlyList<InstanceEndpoint>>? _listChildren;

    /// <summary>合并来自后台线程的刷新请求（子实例上下线、账户目录或当前账户变化）。</summary>
    private readonly PuloniaCoalescedRefresh _refresh;

    /// <summary>最近一次读取的账户目录。</summary>
    private IReadOnlyList<PuloniaGenshinAccountProfile> _accounts = [];

    /// <summary>当前显示的计划 ID；换计划后恢复默认选择。</summary>
    private string? _planId;

    /// <summary>当前计划的运行账户设置：为空表示跟随当前账户（可多选），否则为指定账户（只能单选）。</summary>
    private string? _planAccountId;

    /// <summary>用户是否改过选择；没改过时刷新后按默认规则重新选择（例如实例的当前账户变了）。</summary>
    private bool _hasUserSelection;

    /// <summary>正在由本类批量设置选择，选项的变化回调不计为用户操作。</summary>
    private bool _isApplyingSelection;

    /// <summary>刷新版本号；较早发起、较晚完成的读取结果被丢弃。</summary>
    private int _refreshVersion;

    /// <summary>页面是否可见并已订阅变化。</summary>
    private bool _isActive;

    /// <summary>选项：本实例在首位，其后是在线子实例。</summary>
    public ObservableCollection<PuloniaRunTargetOptionViewModel> Options { get; } = [];

    /// <summary>弹出层是否展开。</summary>
    [ObservableProperty] private bool _isOpen;

    /// <summary>是否显示“运行于”：主实例且有在线子实例。</summary>
    [ObservableProperty] private bool _isChoiceVisible;

    /// <summary>读取或修改账户失败、没有选择实例等提示；没有时为空。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    /// <summary>是否有需要显示的提示。</summary>
    public bool HasMessage => Message.Length > 0;

    /// <summary>是否允许多选：计划跟随当前账户时可选多个实例，指定账户时只能选一个。</summary>
    public bool IsMultiSelect => _planAccountId is null;

    /// <summary>弹出层顶部的说明，随计划的运行账户设置变化。</summary>
    public string HintText => IsMultiSelect
        ? "计划跟随当前账户：可以选择多个实例，每个实例各运行一次，为该实例的当前账户执行。"
        : "计划指定了运行账户：同一账户不能同时在多个实例运行，只能选一个实例。";

    /// <summary>收起时按钮上的摘要，例如“运行于：主实例、云原神 · A”。</summary>
    public string SummaryText => Options.Where(option => option.IsSelected).Select(option => option.InstanceName).ToArray()
        is { Length: > 0 } names
        ? "运行于：" + string.Join("、", names)
        : "运行于：请选择实例";

    /// <summary>
    /// 提交前的确认：参数为要运行的计划与选中的实例，返回 null 时不提交。
    /// 默认按统筹设计 4.5、4.6 检查（<see cref="PuloniaRunTargetCheck"/>）：账户重复、账户已被占用、当前账户未确定、云原神不能切换、
    /// 切换会把其他在线实例挤下线；用户选择返回修改时展开本组件。测试可替换为不弹框的实现。
    /// </summary>
    public Func<PuloniaTaskPlanDocumentViewModel, IReadOnlyList<PuloniaRunTargetOptionViewModel>, Task<PuloniaRunTargetConfirmation?>>
        ConfirmTargetsAsync { get; set; }

    /// <summary>
    /// 由页面建立：主实例时列出在线子实例并订阅上下线，子实例时只提交到本实例。
    /// </summary>
    /// <param name="session">编辑会话</param>
    /// <param name="instances">多实例服务；为空时只提交到本实例</param>
    public PuloniaRunTargetsViewModel(PuloniaPlanEditingSession session, InstanceService? instances)
        : this(session, CreateChildSource(instances))
    {
        _instances = instances is { Context.IsRoot: true } ? instances : null;
    }

    /// <summary>
    /// 以指定的在线子实例来源建立；测试用它提供子实例端点。
    /// </summary>
    /// <param name="session">编辑会话</param>
    /// <param name="listChildren">列出在线子实例；为空表示不是主实例，只提交到本实例</param>
    internal PuloniaRunTargetsViewModel(PuloniaPlanEditingSession session, Func<IReadOnlyList<InstanceEndpoint>>? listChildren)
    {
        _session = session;
        _listChildren = listChildren;
        _refresh = new PuloniaCoalescedRefresh(RefreshAsync);
        // 提交前检查：有问题且用户选择返回时，组件可见就展开，在这里就地修改各实例的当前账户
        var check = new PuloniaRunTargetCheck(session.AccountService, session.TaskService.Definitions);
        ConfirmTargetsAsync = (document, targets) => check.ConfirmAsync(document, targets, () =>
        {
            if (IsChoiceVisible)
                IsOpen = true;
        });
        // 首次读取前只有本实例一项并选中：组件隐藏时运行提交到本实例
        Options.Add(CreateOption(LocalKey, PuloniaInstanceKey.Describe(LocalKey), null));
        ApplyDefaultSelection();
    }

    /// <summary>本实例的实例键。</summary>
    private string LocalKey => _session.AccountService.CurrentInstanceKey;

    /// <summary>主实例列出在线子实例；子实例或没有多实例服务时为空。</summary>
    /// <param name="instances">多实例服务</param>
    private static Func<IReadOnlyList<InstanceEndpoint>>? CreateChildSource(InstanceService? instances)
        => instances is { Context.IsRoot: true } ? () => instances.GetConnectedChildInstances() : null;

    /// <summary>
    /// 页面进入时调用：订阅子实例上下线、账户目录与当前账户的变化，并重读一次（取得其他实例写入的当前账户）。
    /// </summary>
    public Task ActivateAsync()
    {
        if (!_isActive)
        {
            _isActive = true;
            _session.AccountService.AccountsChanged += OnAccountsChanged;
            _session.AccountService.CurrentAccountChanged += OnCurrentAccountChanged;
            if (_instances is not null)
                _instances.ConnectedInstancesChanged += OnConnectedInstancesChanged;
        }
        return RefreshAsync();
    }

    /// <summary>页面离开时调用：解除订阅并收起弹出层，保留当前选择。</summary>
    public void Deactivate()
    {
        IsOpen = false;
        if (!_isActive)
            return;
        _isActive = false;
        _session.AccountService.AccountsChanged -= OnAccountsChanged;
        _session.AccountService.CurrentAccountChanged -= OnCurrentAccountChanged;
        if (_instances is not null)
            _instances.ConnectedInstancesChanged -= OnConnectedInstancesChanged;
    }

    /// <summary>
    /// 页面显示的计划或其运行账户设置变化时调用：切换单选或多选，并按规则恢复默认选择。
    /// </summary>
    /// <param name="planId">当前计划 ID；没有计划时为空</param>
    /// <param name="planAccountId">计划的运行账户设置；为空表示跟随当前账户</param>
    public void SetPlan(string? planId, string? planAccountId)
    {
        if (planId == _planId && planAccountId == _planAccountId)
            return;
        _planId = planId;
        _planAccountId = planAccountId;
        foreach (var option in Options)
            option.IsMultiSelect = IsMultiSelect;
        ApplyDefaultSelection();
        OnPropertyChanged(nameof(IsMultiSelect));
        OnPropertyChanged(nameof(HintText));
        if (Message == NoSelectionMessage)
            Message = string.Empty;
        OnPropertyChanged(nameof(SummaryText));
    }

    /// <summary>
    /// 重读在线子实例、账户目录与各实例的当前账户，重建或更新选项；下线的选中项随选项移除，选择无效或用户没改过时恢复默认。
    /// </summary>
    public async Task RefreshAsync()
    {
        var version = ++_refreshVersion;

        // 1. 实例：本实例加在线子实例；只有主实例能列出并下发到子实例，无法识别实例键的子实例不能运行计划
        var rows = new List<(string Key, string Name, InstanceEndpoint? Endpoint)>
            { (LocalKey, PuloniaInstanceKey.Describe(LocalKey), null) };
        foreach (var endpoint in _listChildren?.Invoke() ?? [])
        {
            if (endpoint.InstanceType == BetterGiInstanceType.Primary
                || PuloniaRunDispatcher.TryGetInstanceKey(endpoint) is not { } key
                || rows.Any(row => row.Key == key))
                continue;
            rows.Add((key, PuloniaRunDispatcher.Describe(endpoint), endpoint));
        }

        // 2. 账户目录与各实例当前账户：每次都从共享设置文件重读，得到其他实例写入的最新值；读不出的项显示“未确定”并说明
        var problems = new List<string>();
        var accounts = _accounts;
        try
        {
            accounts = (await _session.AccountService.LoadAccountsAsync()).Accounts;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            problems.Add("账户目录读取失败：" + ex.Message);
        }
        var currents = new string?[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            try
            {
                currents[index] = await _session.AccountService.GetCurrentAccountIdAsync(rows[index].Key);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                problems.Add($"“{rows[index].Name}”的当前账户读取失败：{ex.Message}");
            }
        }
        if (version != _refreshVersion)
            return;

        // 3. 实例集合变化时重建选项（按实例键保留选择），否则就地更新账户，避免弹出层中的下拉框闪动
        _accounts = accounts;
        if (!Options.Select(option => option.Identity)
                .SequenceEqual(rows.Select(row => PuloniaRunTargetOptionViewModel.CreateIdentity(row.Key, row.Endpoint))))
        {
            var selectedKeys = Options.Where(option => option.IsSelected).Select(option => option.InstanceKey)
                .ToHashSet(StringComparer.Ordinal);
            _isApplyingSelection = true;
            try
            {
                Options.Clear();
                foreach (var row in rows)
                {
                    var option = CreateOption(row.Key, row.Name, row.Endpoint);
                    option.IsSelected = selectedKeys.Contains(row.Key);
                    Options.Add(option);
                }
            }
            finally
            {
                _isApplyingSelection = false;
            }
        }
        for (var index = 0; index < rows.Count; index++)
            Options[index].UpdateAccount(currents[index], accounts);

        // 4. 显示与选择：只有本实例时隐藏；选择无效（例如选中的实例已下线）或用户没改过时按默认规则选择
        IsChoiceVisible = _listChildren is not null && Options.Count > 1;
        EnsureSelection();
        Message = string.Join("\n", problems);
        OnPropertyChanged(nameof(SummaryText));
    }

    /// <summary>
    /// 按选择逐个实例提交一次运行：先做提交前确认，再交给编辑会话保存一次计划、检查资源并逐个提交，结果按实例汇总。
    /// 组件隐藏时只提交到本实例；没有选择实例时展开弹出层提示，不提交。
    /// </summary>
    /// <param name="document">要运行的计划</param>
    /// <returns>每个实例的提交结果；未提交时为空</returns>
    public async Task<IReadOnlyList<PuloniaRunSubmitResult>> SubmitAsync(PuloniaTaskPlanDocumentViewModel document)
    {
        var targets = GetTargets(document);
        if (targets.Count == 0)
        {
            Message = NoSelectionMessage;
            IsOpen = true;
            return [];
        }
        if (await ConfirmTargetsAsync(document, targets) is not { } confirmation)
            return [];
        return await _session.RunPlanOnTargetsAsync(document, targets.Select(target => target.Endpoint).ToArray(),
            confirmation.AllowDisplaceOnlineInstance);
    }

    /// <summary>
    /// 本次提交的目标实例：组件隐藏时为本实例；不是当前显示的计划（例如从计划卡片直接运行）时按该计划的运行账户设置取默认实例；
    /// 否则为选中的实例。
    /// </summary>
    /// <param name="document">要运行的计划</param>
    internal IReadOnlyList<PuloniaRunTargetOptionViewModel> GetTargets(PuloniaTaskPlanDocumentViewModel document)
    {
        var local = Options.First(option => option.IsLocal);
        if (!IsChoiceVisible)
            return [local];
        if (document.Id != _planId)
        {
            var key = GetDefaultTargetKey(document.Plan.AccountId, Snapshot(), LocalKey);
            return Options.Where(option => option.InstanceKey == key).ToArray();
        }
        return Options.Where(option => option.IsSelected).ToArray();
    }

    /// <summary>
    /// 默认选中的实例：计划指定账户时为当前账户就是该账户的第一个实例（本实例在首位），没有则本实例；跟随当前账户时为本实例。
    /// </summary>
    /// <param name="planAccountId">计划的运行账户设置；为空表示跟随当前账户</param>
    /// <param name="instances">各实例的实例键与当前账户，按选项顺序</param>
    /// <param name="localKey">本实例的实例键</param>
    /// <returns>默认实例的实例键</returns>
    internal static string GetDefaultTargetKey(string? planAccountId,
        IReadOnlyList<(string InstanceKey, string? CurrentAccountId)> instances, string localKey)
        => planAccountId is not null
           && instances.FirstOrDefault(instance => instance.CurrentAccountId == planAccountId) is { InstanceKey: { } key }
            ? key
            : localKey;

    /// <summary>展开弹出层，并重读其他实例写入的当前账户。</summary>
    [RelayCommand]
    private async Task OpenAsync()
    {
        IsOpen = true;
        await RefreshAsync();
    }

    /// <summary>建立选项，选择变化与就地修改账户都回到本类处理。</summary>
    /// <param name="instanceKey">实例键</param>
    /// <param name="instanceName">实例显示名</param>
    /// <param name="endpoint">在线子实例端点；本实例为 null</param>
    private PuloniaRunTargetOptionViewModel CreateOption(string instanceKey, string instanceName, InstanceEndpoint? endpoint)
        => new(instanceKey, instanceName, endpoint, OnOptionSelectionChanged, ChangeAccountAsync) { IsMultiSelect = IsMultiSelect };

    /// <summary>各选项的实例键与当前账户。</summary>
    private (string InstanceKey, string? CurrentAccountId)[] Snapshot()
        => Options.Select(option => (option.InstanceKey, option.CurrentAccountId)).ToArray();

    /// <summary>用户改过且仍有效的选择保留；否则按默认规则选择。</summary>
    private void EnsureSelection()
    {
        var selectedCount = Options.Count(option => option.IsSelected);
        if (_hasUserSelection && (IsMultiSelect ? selectedCount > 0 : selectedCount == 1))
            return;
        ApplyDefaultSelection();
    }

    /// <summary>按计划的运行账户设置选中默认实例，并视为用户没有改过选择。</summary>
    private void ApplyDefaultSelection()
    {
        var key = GetDefaultTargetKey(_planAccountId, Snapshot(), LocalKey);
        SetSelection(option => option.InstanceKey == key);
        _hasUserSelection = false;
    }

    /// <summary>批量设置选择；期间的选项回调不计为用户操作。</summary>
    /// <param name="isSelected">每个选项是否选中</param>
    private void SetSelection(Func<PuloniaRunTargetOptionViewModel, bool> isSelected)
    {
        _isApplyingSelection = true;
        try
        {
            foreach (var option in Options)
                option.IsSelected = isSelected(option);
        }
        finally
        {
            _isApplyingSelection = false;
        }
    }

    /// <summary>用户勾选或取消一个实例：指定账户的计划只能选一个，选中一个即取消其他。</summary>
    /// <param name="option">变化的选项</param>
    private void OnOptionSelectionChanged(PuloniaRunTargetOptionViewModel option)
    {
        if (_isApplyingSelection)
            return;
        _hasUserSelection = true;
        if (!IsMultiSelect && option.IsSelected)
            SetSelection(other => ReferenceEquals(other, option));
        if (Message == NoSelectionMessage)
            Message = string.Empty;
        OnPropertyChanged(nameof(SummaryText));
    }

    /// <summary>
    /// 就地修改一个实例的当前账户并立即保存（直接写共享的实例设置，不经过 IPC）；正在运行的任务不受影响，下一次运行开始时生效。
    /// </summary>
    /// <param name="option">实例选项</param>
    /// <param name="accountId">新的当前账户 ID</param>
    private async Task ChangeAccountAsync(PuloniaRunTargetOptionViewModel option, string accountId)
    {
        string? error = null;
        try
        {
            await _session.AccountService.SetCurrentAccountAsync(option.InstanceKey, accountId);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            error = $"未能修改“{option.InstanceName}”的当前账户：{ex.Message}";
        }
        await RefreshAsync();
        if (error is not null)
            Message = error;
    }

    /// <summary>账户目录在本进程保存后刷新。</summary>
    private void OnAccountsChanged(object? sender, EventArgs e) => _refresh.Request();

    /// <summary>任一实例的当前账户在本进程被修改后刷新。</summary>
    private void OnCurrentAccountChanged(object? sender, PuloniaCurrentAccountChangedEventArgs e) => _refresh.Request();

    /// <summary>子实例上线或下线后刷新；事件来自管道后台线程，刷新合并到 UI 线程。</summary>
    private void OnConnectedInstancesChanged(object? sender, EventArgs e) => _refresh.Request();
}
