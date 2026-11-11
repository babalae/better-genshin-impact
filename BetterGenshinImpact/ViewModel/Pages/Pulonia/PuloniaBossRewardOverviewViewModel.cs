using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BetterGenshinImpact.GameTask.AutoBoss;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// 账户页“首领领奖”：只读列出所选账户在各计划（普通计划与一条龙配置）的自动首领讨伐节点中，当前重置批次内的累计成功领奖次数。
/// 次数存放在账户的 boss-rewards.json，键是（计划 ID、节点 ID、重置批次）的哈希，只能按会话中已知的节点逐个查询；
/// 重置批次取节点当前有效参数，与运行时和节点设置中的读取方式一致。页签显示时读取，本进程写入该账户的进度后刷新。
/// </summary>
public partial class PuloniaBossRewardOverviewViewModel : ObservableObject
{
    /// <summary>自动首领讨伐节点的任务类型。</summary>
    private static readonly string BossTaskType = PuloniaSoloTaskExecutor.ToTaskType(AutoBossTask.TaskId);

    /// <summary>编辑会话：计划文档与账户服务。</summary>
    private readonly PuloniaPlanEditingSession _session;

    /// <summary>页面所在的 UI 线程；账户进度事件来自写入线程。</summary>
    private readonly Dispatcher _dispatcher;

    /// <summary>当前查看的账户 ID；尚未选中账户时为空。</summary>
    private string? _accountId;

    /// <summary>页签是否正在显示；只在显示时响应账户进度变化。</summary>
    private bool _isShown;

    /// <summary>读取版本；新的读取开始后，较早读取的结果不再写入列表。</summary>
    private int _generation;

    /// <summary>当前账户有领奖记录的节点，按计划顺序与节点顺序排列。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(ShowEmptyText))]
    private IReadOnlyList<PuloniaBossRewardEntryViewModel> _entries = [];

    /// <summary>列表上方的计数说明；没有首领讨伐任务时为空。</summary>
    [ObservableProperty] private string _summaryText = string.Empty;

    /// <summary>列表为空时的说明。</summary>
    [ObservableProperty] private string _emptyText = "请先选择账户。";

    /// <summary>正在读取。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyText))]
    private bool _isLoading;

    /// <summary>列表为空。</summary>
    public bool IsEmpty => Entries.Count == 0;

    /// <summary>是否显示空列表说明：读取期间只显示进度，不显示上一次的说明。</summary>
    public bool ShowEmptyText => IsEmpty && !IsLoading;

    /// <summary>建立首领领奖列表并订阅账户进度变化；构造时不读取文件。</summary>
    /// <param name="session">编辑会话。</param>
    public PuloniaBossRewardOverviewViewModel(PuloniaPlanEditingSession session)
    {
        _session = session;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _session.AccountService.AccountProgressChanged += OnAccountProgressChanged;
    }

    /// <summary>切换查看的账户；页签正在显示时立即重新读取，否则清空列表，等页签显示时读取。</summary>
    /// <param name="accountId">账户 ID；为空表示尚未选中账户。</param>
    public void SetAccount(string? accountId)
    {
        if (_accountId == accountId) return;
        _accountId = accountId;
        Entries = [];
        SummaryText = string.Empty;
        if (_isShown) _ = LoadAsync();
    }

    /// <summary>页签显示时读取（由视图 Loaded 通过 Behaviors 调用）。</summary>
    [RelayCommand]
    private Task ShowAsync()
    {
        _isShown = true;
        return LoadAsync();
    }

    /// <summary>页签隐藏后不再响应账户进度变化（由视图 Unloaded 通过 Behaviors 调用）。</summary>
    [RelayCommand]
    private void Hide() => _isShown = false;

    /// <summary>手动重新读取（例如其他实例刚写入了次数）。</summary>
    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    /// <summary>
    /// 读取所选账户在各自动首领讨伐节点当前批次内的次数，只列出次数大于 0 的节点；读取失败时在列表处说明原因。
    /// </summary>
    internal async Task LoadAsync()
    {
        var generation = ++_generation;
        if (_accountId is not { } accountId)
        {
            Entries = [];
            SummaryText = string.Empty;
            EmptyText = "请先选择账户。";
            return;
        }
        IsLoading = true;
        try
        {
            // 1. 账户页可能先于任务计划页打开：先完成会话的首次加载，才能列出计划中的节点
            await _session.InitializeAsync();
            if (!_session.IsInitialized)
            {
                if (generation != _generation) return;
                Entries = [];
                SummaryText = string.Empty;
                EmptyText = "任务计划加载失败，无法列出首领讨伐任务。";
                return;
            }
            // 2. 收集各计划自身的自动首领讨伐节点；引用计划中的节点按它所在的计划记录，不在引用处重复列出
            var nodes = CollectBossNodes();
            // 3. 逐个节点读取当前重置批次内的次数
            var entries = new List<PuloniaBossRewardEntryViewModel>();
            foreach (var node in nodes)
            {
                var count = await _session.AccountService.GetBossRewardCountAsync(accountId, node.PlanId, node.TaskId, node.ResetId);
                if (count > 0)
                    entries.Add(new PuloniaBossRewardEntryViewModel(node.PlanName, node.NodeName, count, node.Limit));
            }
            // 读取期间切换了账户或开始了新的读取，旧结果不再显示
            if (generation != _generation) return;
            Entries = entries;
            SummaryText = nodes.Count == 0
                ? string.Empty
                : $"共 {nodes.Count} 个自动首领讨伐任务，其中 {entries.Count} 个在当前重置批次内有领奖记录。"
                  + "次数按任务分别统计，在任务设置中“清空累计”后重新从 0 开始。";
            EmptyText = nodes.Count == 0
                ? "任务计划和一条龙配置中没有自动首领讨伐任务。"
                : "这个账户在当前重置批次内还没有首领领奖记录。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException
                                       or PuloniaTaskValidationException or InvalidOperationException)
        {
            if (generation != _generation) return;
            Entries = [];
            SummaryText = string.Empty;
            EmptyText = "读取首领领奖次数失败：" + ex.Message;
        }
        finally
        {
            if (generation == _generation) IsLoading = false;
        }
    }

    /// <summary>
    /// 按普通计划、一条龙配置的顺序列出会话中的自动首领讨伐节点，以及运行时使用的计划 ID、节点 ID、重置批次与累计上限。
    /// </summary>
    private List<BossNode> CollectBossNodes()
    {
        var nodes = new List<BossNode>();
        foreach (var (document, prefix) in _session.Documents.Select(document => (document, string.Empty))
                     .Concat(_session.OneDragonDocuments.Select(document => (document, "一条龙："))))
        {
            foreach (var node in document.EnumerateNodes().Where(node => node.IsLeaf && node.TaskType == BossTaskType))
            {
                // 与执行器相同：重置批次与累计上限取节点当前有效参数，缺省为空批次与不限制
                var values = document.GetOneDragonParameters(node);
                var resetId = values.Value<string>(PuloniaSoloTaskExecutor.BossRewardCountResetParameter) ?? string.Empty;
                var limit = values.Value<int?>(PuloniaSoloTaskExecutor.BossTotalRunCountLimitParameter) ?? 0;
                nodes.Add(new BossNode(document.Id, node.Id, resetId, prefix + document.Name, DescribeNode(node), limit));
            }
        }
        return nodes;
    }

    /// <summary>节点在计划中的位置：上级分组与节点名，例如“周常 / 讨伐雷音权现”。</summary>
    /// <param name="node">计划节点。</param>
    private static string DescribeNode(PuloniaTaskNodeViewModel node)
    {
        var names = new List<string> { node.Name };
        for (var parent = node.Parent; parent is { IsRoot: false }; parent = parent.Parent)
            names.Insert(0, parent.Name);
        return string.Join(" / ", names);
    }

    /// <summary>本进程写入或清除了账户进度后，页签正在显示且是当前账户时重新读取。</summary>
    private void OnAccountProgressChanged(object? sender, PuloniaAccountProgressChangedEventArgs e)
    {
        if (_dispatcher.HasShutdownStarted) return;
        _dispatcher.InvokeAsync(() =>
        {
            if (_isShown && e.AccountId == _accountId)
                _ = LoadAsync();
        });
    }

    /// <summary>一个自动首领讨伐节点的查询身份与显示信息。</summary>
    /// <param name="PlanId">节点所在计划的 ID。</param>
    /// <param name="TaskId">节点 ID。</param>
    /// <param name="ResetId">节点当前的重置批次。</param>
    /// <param name="PlanName">计划显示名，一条龙配置带“一条龙：”前缀。</param>
    /// <param name="NodeName">节点在计划中的位置与名称。</param>
    /// <param name="Limit">累计上限；0 表示不限制。</param>
    private sealed record BossNode(string PlanId, string TaskId, string ResetId, string PlanName, string NodeName, int Limit);
}

/// <summary>
/// “首领领奖”列表中的一行：计划名、节点名与当前重置批次内的累计成功领奖次数。
/// </summary>
/// <param name="PlanName">计划显示名，一条龙配置带“一条龙：”前缀。</param>
/// <param name="NodeName">节点在计划中的位置与名称。</param>
/// <param name="Count">累计成功领奖次数。</param>
/// <param name="Limit">节点设置的累计上限；0 表示不限制。</param>
public sealed record PuloniaBossRewardEntryViewModel(string PlanName, string NodeName, int Count, int Limit)
{
    /// <summary>次数说明，设置了累计上限时一并显示，例如“3 次（上限 10）”。</summary>
    public string CountText => Limit > 0 ? $"{Count} 次（上限 {Limit}）" : $"{Count} 次";
}
