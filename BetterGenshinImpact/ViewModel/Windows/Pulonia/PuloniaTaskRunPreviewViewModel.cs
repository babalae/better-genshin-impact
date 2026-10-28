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

    /// <summary>请求关闭窗口。</summary>
    public event EventHandler? CloseRequested;

    /// <summary>建立预览。</summary>
    /// <param name="session">编辑会话。</param>
    /// <param name="document">要预览的计划。</param>
    public PuloniaTaskRunPreviewViewModel(PuloniaPlanEditingSession session, PuloniaTaskPlanDocumentViewModel document)
    {
        _session = session;
        _document = document;
        _typeNames = session.TaskService.Definitions.ToDictionary(item => item.TaskType, item => item.DisplayName, StringComparer.Ordinal);
    }

    /// <summary>保存未保存的修改后按已保存配置预测；失败时显示原因，不弹窗。</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorText = null;
        try
        {
            // 1. 预测读取已保存的计划，先完成自动保存
            if (_document.IsDirty && !await _session.SaveDocumentAsync(_document))
            {
                ErrorText = "计划保存失败，无法按当前配置预览。";
                return;
            }
            // 2. 与提交相同地准备快照并逐项判定
            var preview = await _session.TaskService.PreviewAsync(new PuloniaTaskRequest { PlanId = _document.Id, Source = "ui" });
            var now = DateTimeOffset.UtcNow;
            _allItems = preview.Items.Select((item, index) => new PuloniaTaskRunPreviewItemViewModel(item, index + 1,
                _typeNames.GetValueOrDefault(item.TaskType) ?? item.TaskType, preview.ScopeKey, now)).ToArray();
            SummaryText = $"预计执行 {preview.ExecuteCount} 项 · 因路线 CD 跳过 {preview.CooldownSkipCount} 项"
                          + (preview.UnmatchedCount > 0 ? $" · {preview.UnmatchedCount} 项路线未识别刷新规则（会执行）" : string.Empty)
                          + $"（{PuloniaCooldownText.Scope(preview.ScopeKey, _session.AccountNamesByUid)}）";
            ApplyFilter();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 准备失败（资源待确认更新、引用缺失、计划无效等）只在窗口内显示原因，不中断页面
            _allItems = [];
            ApplyFilter();
            SummaryText = "无法预览。";
            ErrorText = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>清除选中节点的路线 CD 记录后重新预测。</summary>
    /// <param name="selected">列表的选中项。</param>
    [RelayCommand]
    private async Task ClearSelectedAsync(IList? selected)
    {
        var keys = selected?.OfType<PuloniaTaskRunPreviewItemViewModel>().Select(item => item.CooldownKey)
            .OfType<PuloniaCollectionCooldownKey>().Distinct().ToArray() ?? [];
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

    /// <summary>可清除的路线 CD 记录；没有记录时为空。</summary>
    public PuloniaCollectionCooldownKey? CooldownKey { get; }

    /// <summary>建立一行。</summary>
    public PuloniaTaskRunPreviewItemViewModel(PuloniaTaskRunPreviewItem item, int index, string typeText, string scopeKey,
        DateTimeOffset now)
    {
        Index = index;
        Name = item.Name;
        TypeText = typeText;
        IsDisabled = item.Outcome == PuloniaTaskPreviewOutcome.Disabled;
        IsSkipped = item.Outcome is PuloniaTaskPreviewOutcome.CoolingDown or PuloniaTaskPreviewOutcome.Completed
            or PuloniaTaskPreviewOutcome.RepeatedInRun;
        IsCaution = item.Outcome == PuloniaTaskPreviewOutcome.Execute
                    && item.Cooldown?.State == PuloniaCollectionCooldownState.Unmatched;
        if (item.Policy is { } policy && item.Cooldown?.Entry is not null)
            CooldownKey = new PuloniaCollectionCooldownKey(scopeKey, PuloniaCollectionCooldown.GetEffectKey(policy));
        var rule = item.Policy is { } p ? $"{p.MaterialName ?? "未识别"} · {p.Description}" : string.Empty;
        (OutcomeText, DetailText) = item.Outcome switch
        {
            PuloniaTaskPreviewOutcome.Disabled => ("已关闭", "节点自身或上级分组已关闭。"),
            PuloniaTaskPreviewOutcome.CoolingDown when item.Cooldown?.NextEligibleAt is { } next =>
                ("跳过 · CD 中", $"下次可执行：{PuloniaCooldownText.Next(next, now)} · {rule}"),
            PuloniaTaskPreviewOutcome.Completed => ("跳过 · 已完成", "一次性路线已成功执行，不再自动执行。"),
            PuloniaTaskPreviewOutcome.RepeatedInRun =>
                ("跳过 · 本次重复", $"与“{item.RepeatedTaskName}”是同一路线文件，前一项成功后跳过。"),
            _ when item.Policy is null => ("执行", string.Empty),
            _ when IsCaution => ("执行", "未识别刷新规则：成功后不会跳过，可在共享设置中选择自定义周期。"),
            _ when item.Cooldown?.LastSuccessAt is { } last => ("执行", $"上次成功 {PuloniaCooldownText.Time(last)}，已可执行 · {rule}"),
            _ => ("执行", rule)
        };
    }
}
