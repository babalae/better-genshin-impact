using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Service.Instance;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>
/// “运行于”中的一个实例：本实例（主实例本地提交）或在线子实例（经一步下发整次交给它执行），
/// 显示“实例名 — 当前账户”；账户目录有多个账户时可就地修改该实例的当前账户（立即保存）。
/// </summary>
public sealed partial class PuloniaRunTargetOptionViewModel : ObservableObject
{
    /// <summary>当前账户“未确定”时下拉框中的占位项键。</summary>
    private const string UndeterminedKey = "";

    /// <summary>选择变化后通知所属的“运行于”，由它处理单选与摘要。</summary>
    private readonly Action<PuloniaRunTargetOptionViewModel> _selectionChanged;

    /// <summary>就地修改当前账户：参数为本选项与新的账户 ID，由所属的“运行于”保存并刷新。</summary>
    private readonly Func<PuloniaRunTargetOptionViewModel, string, Task> _changeAccount;

    /// <summary>实例键；刷新列表时据此保留选择。</summary>
    public string InstanceKey { get; }

    /// <summary>实例显示名，例如“主实例”“云原神 · 小号A”。</summary>
    public string InstanceName { get; }

    /// <summary>在线子实例的端点；本实例本地提交时为 null。</summary>
    public InstanceEndpoint? Endpoint { get; }

    /// <summary>是否在本实例本地提交。</summary>
    public bool IsLocal => Endpoint is null;

    /// <summary>选项身份：实例键加进程 ID。子实例重启后端点变化，列表需要重建，但仍按实例键保留选择。</summary>
    public string Identity => CreateIdentity(InstanceKey, Endpoint);

    /// <summary>是否选中；多选时用复选框，单选时用单选按钮。</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>是否允许多选（计划跟随当前账户）；计划指定账户时只能选一个实例。</summary>
    [ObservableProperty] private bool _isMultiSelect = true;

    /// <summary>实例当前账户 ID；“未确定”或尚未读取时为 null。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedAccountId))]
    private string? _currentAccountId;

    /// <summary>实例当前账户名称；“未确定”时为“未确定”。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    private string _accountName = "未确定";

    /// <summary>账户下拉框的选项：键为账户 ID，当前账户未确定时首项为“未确定”占位。</summary>
    [ObservableProperty] private IReadOnlyList<KeyValuePair<string, string>> _accountChoices = [];

    /// <summary>是否显示账户下拉框：账户目录有多个账户，或当前账户未确定需要选择时。</summary>
    [ObservableProperty] private bool _canChangeAccount;

    /// <summary>“实例名 — 当前账户名”，例如“云原神 · 小号A — 乙”。</summary>
    public string Display => $"{InstanceName} — {AccountName}";

    /// <summary>
    /// 账户下拉框的选中值：当前账户 ID，未确定时为占位键。选择其他账户即把它设为该实例的当前账户并立即保存；
    /// 下拉框重建选项时会写回 null 或占位键，这些写回被忽略。
    /// </summary>
    public string SelectedAccountId
    {
        get => CurrentAccountId ?? UndeterminedKey;
        set
        {
            if (string.IsNullOrEmpty(value) || value == CurrentAccountId)
                return;
            _ = _changeAccount(this, value);
        }
    }

    /// <summary>
    /// 建立选项。
    /// </summary>
    /// <param name="instanceKey">实例键</param>
    /// <param name="instanceName">实例显示名</param>
    /// <param name="endpoint">在线子实例端点；本实例为 null</param>
    /// <param name="selectionChanged">选择变化的回调</param>
    /// <param name="changeAccount">就地修改当前账户的回调</param>
    public PuloniaRunTargetOptionViewModel(string instanceKey, string instanceName, InstanceEndpoint? endpoint,
        Action<PuloniaRunTargetOptionViewModel> selectionChanged, Func<PuloniaRunTargetOptionViewModel, string, Task> changeAccount)
    {
        InstanceKey = instanceKey;
        InstanceName = instanceName;
        Endpoint = endpoint;
        _selectionChanged = selectionChanged;
        _changeAccount = changeAccount;
    }

    /// <summary>
    /// 按最新读取的账户目录与该实例当前账户更新显示与下拉选项；先换选项再通知选中值，下拉框才能选回当前账户。
    /// </summary>
    /// <param name="currentAccountId">实例当前账户 ID；“未确定”时为 null</param>
    /// <param name="accounts">账户目录</param>
    internal void UpdateAccount(string? currentAccountId, IReadOnlyList<PuloniaGenshinAccountProfile> accounts)
    {
        var current = accounts.FirstOrDefault(account => account.Id == currentAccountId);
        var choices = new List<KeyValuePair<string, string>>();
        // 当前账户未确定（或已不在目录中）时以占位项显示，选择真实账户后消失
        if (current is null)
            choices.Add(new KeyValuePair<string, string>(UndeterminedKey, "未确定"));
        choices.AddRange(accounts.Select(account =>
            new KeyValuePair<string, string>(account.Id, PuloniaAccountChoiceViewModel.Describe(account))));
        AccountChoices = choices;
        CanChangeAccount = accounts.Count > 1 || (current is null && accounts.Count > 0);
        AccountName = current?.Name ?? "未确定";
        CurrentAccountId = current?.Id;
        OnPropertyChanged(nameof(SelectedAccountId));
    }

    /// <summary>
    /// 选项身份的计算规则：本实例为实例键，子实例为“实例键#进程 ID”。
    /// </summary>
    /// <param name="instanceKey">实例键</param>
    /// <param name="endpoint">在线子实例端点；本实例为 null</param>
    internal static string CreateIdentity(string instanceKey, InstanceEndpoint? endpoint)
        => endpoint is { } current ? $"{instanceKey}#{current.ProcessId}" : instanceKey;

    /// <summary>选择变化后交给所属的“运行于”处理。</summary>
    partial void OnIsSelectedChanged(bool value) => _selectionChanged(this);
}
