using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 运行预览窗口：按计划当前配置逐项预测本次执行或跳过，可清除选中路线的 CD 记录后直接开始执行。
/// 预测由任务服务的 PreviewAsync 完成，与提交运行使用同一份快照规则与状态判定。
/// </summary>
public partial class PuloniaTaskRunPreviewViewModel : ObservableObject
{
    /// <summary>编辑会话：保存草稿、预测与清除记录。</summary>
    private readonly PuloniaPlanEditingSession _session;

    /// <summary>要预览的计划。</summary>
    private readonly PuloniaTaskPlanDocumentViewModel _document;

    /// <summary>任务类型显示名。</summary>
    private readonly IReadOnlyDictionary<string, string> _typeNames;

    /// <summary>最近一次预测的全部节点。</summary>
    private IReadOnlyList<PuloniaTaskRunPreviewItemViewModel> _allItems = [];

    /// <summary>按计划的运行账户设置选出的账户 ID（统筹设计 5.6，不含临时选择）；会话尚未读取账户目录时为空。</summary>
    private readonly string? _planAccountId;

    /// <summary>按计划设置选出的账户名称，用于临时查看其他账户时的说明。</summary>
    private readonly string? _planAccountName;

    /// <summary>“CD 按账户”下拉框当前选中的账户 ID；预测完成后同步为实际读取的账户。</summary>
    private string? _selectedAccountId;

    /// <summary>预测版本；改选账户后重新预测时，较早的结果不再写入列表。</summary>
    private int _loadGeneration;

    /// <summary>窗口标题。</summary>
    public string Title => $"预览本次执行 · {_document.Name}";

    /// <summary>当前显示的节点。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<PuloniaTaskRunPreviewItemViewModel> _items = [];

    /// <summary>汇总说明。</summary>
    [ObservableProperty] private string _summaryText = "正在按当前配置准备预览…";

    /// <summary>准备失败的原因，例如资源需要先确认更新。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorText;

    /// <summary>正在准备预览。</summary>
    [ObservableProperty] private bool _isLoading;

    /// <summary>只显示会跳过的节点。</summary>
    [ObservableProperty] private bool _showSkippedOnly;

    /// <summary>是否有准备错误。</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    /// <summary>列表为空。</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>用户是否选择了开始执行；窗口关闭后由页面读取。</summary>
    public bool StartRequested { get; private set; }

    /// <summary>
    /// 临时查看路线 CD 的账户 ID；为空时按计划的运行账户设置选择（指定账户或本实例当前账户）。只影响本窗口的显示。
    /// </summary>
    public string? PreviewAccountId { get; set; }

    /// <summary>“CD 按账户”的选项：账户目录中的各账户，顺序与账户列表一致。</summary>
    public IReadOnlyList<PuloniaCooldownFilterOption> AccountOptions { get; }

    /// <summary>是否显示“CD 按账户”：账户目录有多个账户时显示。</summary>
    public bool ShowAccountSelector => AccountOptions.Count > 1;

    /// <summary>
    /// 本窗口按哪个账户预测路线 CD。改选后设置 <see cref="PreviewAccountId"/> 并重新预测；选回按计划设置选出的账户时恢复默认。
    /// 只影响本窗口，不修改任务计划页的显示、计划和实例设置。
    /// </summary>
    public string? SelectedAccountId
    {
        get => _selectedAccountId;
        set
        {
            // 下拉框重建选项时会先写回 null，不能据此改变预测的账户
            if (value is null || value == _selectedAccountId)
                return;
            _selectedAccountId = value;
            PreviewAccountId = value == _planAccountId ? null : value;
            NotifyAccountChanged();
            // 经命令重新预测：预测中再次改选时以最后一次为准（见 LoadAsync 的版本判断）
            LoadCommand.Execute(null);
        }
    }

    /// <summary>临时查看其他账户时的说明；按计划设置预测时为 null。</summary>
    public string? AccountNotice => PreviewAccountId is not null && _planAccountName is not null
        ? $"仅改变本窗口显示，开始执行后仍按“{_planAccountName}”判定"
        : null;

    /// <summary>是否显示临时查看的说明。</summary>
    public bool HasAccountNotice => AccountNotice is not null;

    /// <summary>请求关闭窗口。</summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// 建立预览；“CD 按账户”默认是任务计划页当前显示路线 CD 的账户（含页面的临时选择）。
    /// </summary>
    /// <param name="session">编辑会话。</param>
    /// <param name="document">要预览的计划。</param>
    public PuloniaTaskRunPreviewViewModel(PuloniaPlanEditingSession session, PuloniaTaskPlanDocumentViewModel document)
    {
        _session = session;
        _document = document;
        _typeNames = session.TaskService.Definitions.ToDictionary(item => item.TaskType, item => item.DisplayName, StringComparer.Ordinal);
        // 账户选项取会话最近读取的账户目录；页面临时查看其他账户时，本窗口也从该账户开始
        AccountOptions = session.CooldownAccountProfiles.Select(account => new PuloniaCooldownFilterOption(account.Id, account.Name))
            .ToArray();
        var planAccount = session.GetDefaultCooldownAccount(document);
        _planAccountId = planAccount?.Id;
        _planAccountName = planAccount?.Name;
        _selectedAccountId = session.GetCooldownAccount(document)?.Id;
        PreviewAccountId = _selectedAccountId == _planAccountId ? null : _selectedAccountId;
    }

    /// <summary>保存未保存的修改后按已保存配置预测；失败时显示原因，不弹窗。</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        var generation = ++_loadGeneration;
        IsLoading = true;
        ErrorText = null;
        try
        {
            // 1. 预测读取已保存的计划，先完成自动保存
            if (_document.IsDirty && !await _session.SaveDocumentAsync(_document))
            {
                if (generation != _loadGeneration) return;
                ErrorText = "计划保存失败，无法按当前配置预览。";
                return;
            }
            // 2. 与提交相同地准备快照并逐项判定
            var preview = await _session.TaskService.PreviewAsync(new PuloniaTaskRequest { PlanId = _document.Id, Source = "ui" },
                PreviewAccountId);
            // 预测期间改选了账户：以后开始的预测为准
            if (generation != _loadGeneration) return;
            // 下拉框显示实际读取的账户（会话尚未读取账户目录时，初始选中值可能为空）
            if (_selectedAccountId != preview.AccountId)
            {
                _selectedAccountId = preview.AccountId;
                NotifyAccountChanged();
            }
            var now = DateTimeOffset.UtcNow;
            _allItems = preview.Items.Select((item, index) => new PuloniaTaskRunPreviewItemViewModel(item, index + 1,
                _typeNames.GetValueOrDefault(item.TaskType) ?? item.TaskType, preview.AccountId, now)).ToArray();
            SummaryText = $"预计执行 {preview.ExecuteCount} 项 · 因路线 CD 跳过 {preview.CooldownSkipCount} 项"
                          + (preview.UnmatchedCount > 0 ? $" · {preview.UnmatchedCount} 项路线未识别刷新规则（会执行）" : string.Empty)
                          + $"（{PuloniaCooldownText.Account(preview.AccountName)}）"
                          + (preview.AccountNotice is { } notice ? "\n" + notice : string.Empty);
            ApplyFilter();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (generation != _loadGeneration) return;
            // 准备失败（资源待确认更新、引用缺失、计划无效、所选账户已删除等）只在窗口内显示原因，不中断页面
            _allItems = [];
            ApplyFilter();
            SummaryText = "无法预览。";
            ErrorText = ex.Message;
        }
        finally
        {
            if (generation == _loadGeneration) IsLoading = false;
        }
    }

    /// <summary>“CD 按账户”的选中值或临时查看状态变化后通知界面。</summary>
    private void NotifyAccountChanged()
    {
        OnPropertyChanged(nameof(SelectedAccountId));
        OnPropertyChanged(nameof(AccountNotice));
        OnPropertyChanged(nameof(HasAccountNotice));
    }

    /// <summary>清除选中节点的路线 CD 记录后重新预测。</summary>
    /// <param name="selected">列表的选中项。</param>
    [RelayCommand]
    private async Task ClearSelectedAsync(IList? selected)
    {
        var keys = selected?.OfType<PuloniaTaskRunPreviewItemViewModel>().Select(item => item.CooldownKey)
            .OfType<PuloniaCooldownRecordKey>().Distinct().ToArray() ?? [];
        if (keys.Length == 0)
        {
            ErrorText = "选中的项没有路线 CD 记录。";
            return;
        }
        if (await _session.ClearCooldownRecordsAsync(keys, "选中路线") > 0)
            await LoadAsync();
    }

    /// <summary>关闭预览并开始执行此计划。</summary>
    [RelayCommand]
    private void Start()
    {
        StartRequested = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>筛选变化后更新列表。</summary>
    partial void OnShowSkippedOnlyChanged(bool value) => ApplyFilter();

    /// <summary>按“只看跳过”筛选。</summary>
    private void ApplyFilter()
        => Items = ShowSkippedOnly ? _allItems.Where(item => item.IsSkipped).ToArray() : _allItems;
}

/// <summary>
/// 运行预览中的一行：序号、名称、类型、预测结果与原因。
/// </summary>
public sealed class PuloniaTaskRunPreviewItemViewModel
{
    /// <summary>执行顺序中的序号。</summary>
    public int Index { get; }

    /// <summary>节点名称。</summary>
    public string Name { get; }

    /// <summary>任务类型显示名。</summary>
    public string TypeText { get; }

    /// <summary>预测结果文字。</summary>
    public string OutcomeText { get; }

    /// <summary>原因与规则说明。</summary>
    public string DetailText { get; }

    /// <summary>是否因路线 CD 跳过。</summary>
    public bool IsSkipped { get; }

    /// <summary>是否需要提示（未识别刷新规则）。</summary>
    public bool IsCaution { get; }

    /// <summary>是否已关闭。</summary>
    public bool IsDisabled { get; }

    /// <summary>可清除的完成记录（预览账户与路线）；没有记录时为空。</summary>
    public PuloniaCooldownRecordKey? CooldownKey { get; }

    /// <summary>建立一行。</summary>
    /// <param name="item">预测项。</param>
    /// <param name="index">执行顺序中的序号。</param>
    /// <param name="typeText">任务类型显示名。</param>
    /// <param name="accountId">预览读取完成记录的账户 ID。</param>
    /// <param name="now">预测时刻。</param>
    public PuloniaTaskRunPreviewItemViewModel(PuloniaTaskRunPreviewItem item, int index, string typeText, string accountId,
        DateTimeOffset now)
    {
        Index = index;
        Name = item.Name;
        TypeText = typeText;
        IsDisabled = item.Outcome == PuloniaTaskPreviewOutcome.Disabled;
        IsSkipped = item.Outcome is PuloniaTaskPreviewOutcome.CoolingDown or PuloniaTaskPreviewOutcome.Completed
            or PuloniaTaskPreviewOutcome.RepeatedInRun;
        IsCaution = item.Outcome == PuloniaTaskPreviewOutcome.Execute
                    && item.Cooldown?.State == PuloniaRouteCooldownState.Unmatched;
        if (item.Rule is { } refreshRule && item.Cooldown?.Completion is not null)
            CooldownKey = new PuloniaCooldownRecordKey(accountId, refreshRule.FileIdentity);
        var rule = item.Rule is { } p ? $"{p.MaterialName ?? "未识别"} · {p.Description}" : string.Empty;
        (OutcomeText, DetailText) = item.Outcome switch
        {
            PuloniaTaskPreviewOutcome.Disabled => ("已关闭", "节点自身或上级分组已关闭。"),
            PuloniaTaskPreviewOutcome.CoolingDown when item.Cooldown?.NextEligibleAt is { } next =>
                ("跳过 · CD 中", $"下次可执行：{PuloniaCooldownText.Next(next, now)} · {rule}"),
            PuloniaTaskPreviewOutcome.Completed => ("跳过 · 已完成", "一次性路线已成功执行，不再自动执行。"),
            PuloniaTaskPreviewOutcome.RepeatedInRun =>
                ("跳过 · 本次重复", $"与“{item.RepeatedTaskName}”是同一路线文件，前一项成功后跳过。"),
            _ when item.Rule is null => ("执行", string.Empty),
            _ when IsCaution => ("执行", "未识别刷新规则：成功后不会跳过，可在共享设置中选择自定义周期。"),
            _ when item.Cooldown?.LastSuccessAt is { } last => ("执行", $"上次成功 {PuloniaCooldownText.Time(last)}，已可执行 · {rule}"),
            _ => ("执行", rule)
        };
    }
}
