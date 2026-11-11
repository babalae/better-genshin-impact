using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// CD 总览中的一个筛选选项。
/// </summary>
/// <param name="Key">选项值；为空表示不筛选。</param>
/// <param name="Label">显示文字。</param>
public sealed record PuloniaCooldownFilterOption(string? Key, string Label);

/// <summary>
/// 路线 CD 总览：按账户、计划、状态与关键词查看完成记录，按材料分组，可清除选中或筛选出的记录。
/// 账户页“路线 CD”页签使用固定账户模式（<see cref="SetFixedAccount"/>），只看所选账户并隐藏账户筛选；
/// 完成记录由编辑会话读取，变化后自动刷新。
/// </summary>
public partial class PuloniaRouteCooldownOverviewViewModel : ObservableObject, IDisposable
{
    /// <summary>只看本次会跳过的记录（CD 中与已完成）。</summary>
    private const string SkippedState = "skipped";

    /// <summary>编辑会话：各账户的完成记录、计划路线范围与清除入口。</summary>
    private readonly PuloniaPlanEditingSession _session;

    /// <summary>参与筛选的全部完成记录（固定账户时只含该账户），筛选不丢弃。</summary>
    private List<PuloniaRouteCooldownEntryViewModel> _all = [];

    /// <summary>外部指定的计划筛选，在计划选项建立后应用。</summary>
    private string? _requestedPlanId;

    /// <summary>正在重建选项，期间的选择变化不触发筛选。</summary>
    private bool _isRebuilding;

    /// <summary>是否处于固定账户模式；固定后不再显示账户筛选。</summary>
    private bool _isAccountFixed;

    /// <summary>固定账户模式下的账户 ID；尚未选中账户时为空，列表为空。</summary>
    private string? _fixedAccountId;

    /// <summary>当前显示的记录；每次筛选替换为新列表，视图按材料分组。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<PuloniaRouteCooldownEntryViewModel> _entries = [];

    /// <summary>账户筛选选项：全部账户与账户目录中的各账户。</summary>
    public ObservableCollection<PuloniaCooldownFilterOption> AccountOptions { get; } = [];

    /// <summary>计划筛选选项：全部计划与会话中的计划。</summary>
    public ObservableCollection<PuloniaCooldownFilterOption> PlanOptions { get; } = [];

    /// <summary>状态筛选选项。</summary>
    public IReadOnlyList<PuloniaCooldownFilterOption> StateOptions { get; } =
        [new(SkippedState, "CD 中与已完成"), new(null, "全部记录")];

    /// <summary>当前账户筛选。</summary>
    [ObservableProperty] private PuloniaCooldownFilterOption? _selectedAccount;

    /// <summary>当前计划筛选。</summary>
    [ObservableProperty] private PuloniaCooldownFilterOption? _selectedPlan;

    /// <summary>当前状态筛选。</summary>
    [ObservableProperty] private PuloniaCooldownFilterOption _selectedState;

    /// <summary>按路线名、目录或材料搜索。</summary>
    [ObservableProperty] private string _searchText = string.Empty;

    /// <summary>计数说明。</summary>
    [ObservableProperty] private string _countText = string.Empty;

    /// <summary>当前列表为空。</summary>
    public bool IsEmpty => Entries.Count == 0;

    /// <summary>是否显示账户筛选；固定账户模式下隐藏。</summary>
    public bool IsAccountFilterVisible => !_isAccountFixed;

    /// <summary>空列表的说明。</summary>
    public string EmptyText => _isAccountFixed && _fixedAccountId is null
        ? "请先选择账户。"
        : _all.Count == 0
            ? "暂无路线 CD 记录。地图追踪路线整份执行成功后，会在这里记录上次成功时间和下次可执行时间。"
            : "没有符合筛选条件的记录。";

    /// <summary>建立总览并订阅账户进度变化。</summary>
    /// <param name="session">编辑会话。</param>
    public PuloniaRouteCooldownOverviewViewModel(PuloniaPlanEditingSession session)
    {
        _session = session;
        _selectedState = StateOptions[0];
        _session.EnsureCooldownSubscription();
        _session.CooldownsChanged += OnCooldownsChanged;
        Rebuild();
    }

    /// <summary>解除订阅；账户页的实例随程序存在，单独创建的临时实例用完后调用。</summary>
    public void Dispose() => _session.CooldownsChanged -= OnCooldownsChanged;

    /// <summary>
    /// 页面或页签显示时重新读取完成记录（按修改时间比较）；账户页可能先于任务计划页打开，先完成会话的首次加载以列出计划选项。
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await _session.InitializeAsync();
        await _session.ReloadCooldownsAsync();
    }

    /// <summary>
    /// 固定为一个账户：只列出该账户的完成记录，隐藏账户筛选，计划、状态与关键词筛选照常；为空表示尚未选中账户，列表为空。
    /// 账户页切换所选账户时调用。
    /// </summary>
    /// <param name="accountId">要固定的账户 ID。</param>
    public void SetFixedAccount(string? accountId)
    {
        if (_isAccountFixed && _fixedAccountId == accountId) return;
        var wasFixed = _isAccountFixed;
        _isAccountFixed = true;
        _fixedAccountId = accountId;
        if (!wasFixed) OnPropertyChanged(nameof(IsAccountFilterVisible));
        Rebuild();
    }

    /// <summary>按计划筛选；为空表示全部计划。计划选项尚未建立时，在下次重建时应用。</summary>
    /// <param name="planId">计划 ID。</param>
    public void SetPlanFilter(string? planId)
    {
        _requestedPlanId = planId;
        if (PlanOptions.FirstOrDefault(option => option.Key == planId) is not { } option)
            return;
        _requestedPlanId = null;
        SelectedPlan = option;
    }

    /// <summary>清除列表中选中的记录。</summary>
    /// <param name="selected">列表的选中项。</param>
    [RelayCommand]
    private async Task ClearSelectedAsync(IList? selected)
    {
        var keys = selected?.OfType<PuloniaRouteCooldownEntryViewModel>().Select(entry => entry.Key).Distinct().ToArray() ?? [];
        if (keys.Length == 0)
        {
            _session.ReportStatus("请先在路线 CD 列表中选择要清除的记录。");
            return;
        }
        await _session.ClearCooldownRecordsAsync(keys, "选中路线");
    }

    /// <summary>清除当前筛选出的全部记录；可先按计划、账户或材料筛选，实现按计划、账户或材料清除。</summary>
    [RelayCommand]
    private async Task ClearFilteredAsync()
    {
        var keys = Entries.Select(entry => entry.Key).Distinct().ToArray();
        if (keys.Length == 0) return;
        await _session.ClearCooldownRecordsAsync(keys, "当前列表中全部路线");
    }

    /// <summary>完成记录重新读取后重建列表。</summary>
    private void OnCooldownsChanged(object? sender, EventArgs e) => Rebuild();

    /// <summary>从会话读到的各账户完成记录建立全部记录与筛选选项，保留原有选择。</summary>
    private void Rebuild()
    {
        var now = DateTimeOffset.UtcNow;
        var accounts = _session.CooldownAccounts;
        // 固定账户模式只取该账户的记录，计数与空列表说明都按这个账户计算
        var sources = _isAccountFixed ? accounts.Where(item => item.Account.Id == _fixedAccountId) : accounts;
        // 每个账户按自己的服务器时区推算记录时规则下的状态
        _all = sources.SelectMany(item => item.Completions.Completions.Select(completion =>
                new PuloniaRouteCooldownEntryViewModel(completion, item.Account.Id, item.Account.Name,
                    PuloniaServerTime.GetUtcOffsetMinutes(item.Account.ServerTimeZone), now)))
            .ToList();
        _isRebuilding = true;
        try
        {
            // 1. 账户：全部账户 + 账户目录中的各账户（顺序与账户列表一致）
            var accountKey = SelectedAccount?.Key;
            AccountOptions.Clear();
            AccountOptions.Add(new PuloniaCooldownFilterOption(null, "全部账户"));
            foreach (var (account, _) in accounts)
                AccountOptions.Add(new PuloniaCooldownFilterOption(account.Id, account.Name));
            SelectedAccount = AccountOptions.FirstOrDefault(option => option.Key == accountKey) ?? AccountOptions[0];

            // 2. 计划：全部计划 + 普通计划 + 一条龙配置
            var planKey = _requestedPlanId ?? SelectedPlan?.Key;
            PlanOptions.Clear();
            PlanOptions.Add(new PuloniaCooldownFilterOption(null, "全部计划"));
            foreach (var document in _session.Documents)
                PlanOptions.Add(new PuloniaCooldownFilterOption(document.Id, document.Name));
            foreach (var document in _session.OneDragonDocuments)
                PlanOptions.Add(new PuloniaCooldownFilterOption(document.Id, "一条龙：" + document.Name));
            var plan = PlanOptions.FirstOrDefault(option => option.Key == planKey);
            if (plan is not null) _requestedPlanId = null;
            SelectedPlan = plan ?? PlanOptions[0];
        }
        finally
        {
            _isRebuilding = false;
        }
        ApplyFilter();
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>按当前筛选生成列表：按材料分组，组内 CD 中在前并按下次可执行时间排序。</summary>
    private void ApplyFilter()
    {
        var planFiles = SelectedPlan?.Key is { } planId
            && _session.AllDocuments.FirstOrDefault(document => document.Id == planId) is { } plan
            ? _session.GetCooldownFileSet(plan) : null;
        var keyword = SearchText.Trim();
        // 固定账户时 _all 已只含该账户，不再使用账户筛选
        var filtered = _all.Where(entry =>
                (_isAccountFixed || SelectedAccount?.Key is not { } accountId || entry.Key.AccountId == accountId)
                && (planFiles is null || planFiles.Contains(entry.Completion.Route))
                && (SelectedState.Key != SkippedState || entry.IsSkipped)
                && (keyword.Length == 0 || entry.SearchText.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        // 组头按当前显示的记录统计，筛选后数量与列表一致
        foreach (var group in filtered.GroupBy(entry => entry.Material, StringComparer.Ordinal))
        {
            var items = group.ToArray();
            var header = new PuloniaRouteCooldownGroupViewModel(group.Key, items);
            foreach (var entry in items) entry.Group = header;
        }
        Entries = filtered
            .OrderBy(entry => entry.Material == "未识别" ? 1 : 0).ThenBy(entry => entry.Material, StringComparer.Ordinal)
            .ThenBy(entry => entry.State switch
            {
                PuloniaRouteCooldownState.CoolingDown => 0,
                PuloniaRouteCooldownState.Completed => 1,
                _ => 2
            })
            .ThenBy(entry => entry.NextEligibleAt ?? DateTimeOffset.MaxValue)
            .ThenByDescending(entry => entry.Completion.CompletedAt)
            .ToArray();
        var skipped = filtered.Count(entry => entry.IsSkipped);
        CountText = $"显示 {Entries.Count} / {_all.Count} 条记录，其中 {skipped} 条本次会跳过。剩余时间按上次成功时的规则和账户的服务器时区计算；"
                    + "计划中的节点按当前规则判定，以任务树标记为准。";
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>账户筛选变化。</summary>
    partial void OnSelectedAccountChanged(PuloniaCooldownFilterOption? value)
    {
        if (!_isRebuilding) ApplyFilter();
    }

    /// <summary>计划筛选变化。</summary>
    partial void OnSelectedPlanChanged(PuloniaCooldownFilterOption? value)
    {
        if (!_isRebuilding) ApplyFilter();
    }

    /// <summary>状态筛选变化。</summary>
    partial void OnSelectedStateChanged(PuloniaCooldownFilterOption value)
    {
        if (!_isRebuilding) ApplyFilter();
    }

    /// <summary>搜索文字变化。</summary>
    partial void OnSearchTextChanged(string value)
    {
        if (!_isRebuilding) ApplyFilter();
    }
}
