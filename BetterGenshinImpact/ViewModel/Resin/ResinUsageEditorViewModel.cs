using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common.Resin;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Resin;

/// <summary>领奖类树脂设置草稿；切换显示不丢值，耗尽确认通过可替换的异步委托完成。</summary>
public partial class ResinUsageEditorViewModel : ObservableObject, IResinSettingsDialogContent
{
    /// <summary>任务能力。</summary>
    private readonly ResinUsageCapability _capability;
    /// <summary>视图层提供的确认入口，不在 VM 中创建窗口。</summary>
    private readonly Func<ResinConfirmation, Task<bool>> _confirm;
    /// <summary>保留无法显示在选项中的旧档位，打开无效设置也不静默改值。</summary>
    private readonly OriginalResinTier _initialTier;
    /// <summary>每行开关的编辑版本，防止旧确认覆盖用户后续编辑。</summary>
    private readonly Dictionary<ResinKind, int> _toggleVersions = new();
    /// <summary>方式编辑版本，使切换方式前发出的确认失效。</summary>
    private int _modeVersion;
    /// <summary>回退或恢复期间抑制确认，避免属性通知重入。</summary>
    private bool _restoring;
    /// <summary>等待中的确认数；未确认完毕前不能提交草稿。</summary>
    private int _pendingConfirmations;
    /// <summary>串行确认链，避免多个异步确认窗口重叠。</summary>
    private Task _confirmationTask = Task.CompletedTask;

    /// <summary>当前使用方式。</summary>
    [ObservableProperty] private ResinUsageMode _mode;
    /// <summary>成功领奖总次数；隐藏时保留。</summary>
    [ObservableProperty] private int _runCount;
    /// <summary>当前原粹档位选项。</summary>
    [ObservableProperty] private ResinTierChoice? _selectedTier;

    /// <summary>复制当前设置后建立固定四行，全部初始化完成后才订阅编辑事件。</summary>
    public ResinUsageEditorViewModel(string taskName, ResinUsageCapability capability, ResinUsage current,
        Func<ResinConfirmation, Task<bool>> confirm)
    {
        _capability = capability;
        _confirm = confirm;
        Title = $"树脂使用 · {taskName}";
        _mode = current.Mode;
        _runCount = current.RunCount;
        _initialTier = current.OriginalTier;
        TierChoices = capability.Tiers.Select(tier => new ResinTierChoice(tier, ResinUsageText.TierDisplayName(tier))).ToArray();
        _selectedTier = TierChoices.FirstOrDefault(choice => choice.Tier == current.OriginalTier);
        Kinds = ResinKinds.InOrder.Select(kind => new ResinKindRowViewModel(kind, current.Allowed[kind], current.Counts[kind])).ToArray();
        foreach (var row in Kinds)
        {
            _toggleVersions[row.Kind] = 0;
            row.PropertyChanged += OnKindChanged;
        }
        RefreshPresentation();
    }

    /// <inheritdoc />
    public string Title { get; }
    /// <summary>固定顺序的浓缩、原粹、须臾、脆弱四行。</summary>
    public IReadOnlyList<ResinKindRowViewModel> Kinds { get; }
    /// <summary>任务支持的档位选项。</summary>
    public IReadOnlyList<ResinTierChoice> TierChoices { get; }
    /// <summary>档位只有一个选项时不可编辑。</summary>
    public bool IsTierEditable => _capability.Tiers.Count > 1;
    /// <summary>默认档位的任务专属含义，切换档位时仍保留这段说明。</summary>
    public string TierDescription => _capability.Hints.DefaultTierDescription ?? "默认使用领奖界面当前档位";
    /// <summary>刷至耗尽的单选状态；单选取消不反向切换方式。</summary>
    public bool IsExhaustMode { get => Mode == ResinUsageMode.Exhaust; set { if (value) Mode = ResinUsageMode.Exhaust; } }
    /// <summary>指定领奖次数的单选状态。</summary>
    public bool IsRunCountMode { get => Mode == ResinUsageMode.RunCount; set { if (value) Mode = ResinUsageMode.RunCount; } }
    /// <summary>按树脂分别指定的单选状态。</summary>
    public bool IsPerTypeMode { get => Mode == ResinUsageMode.PerType; set { if (value) Mode = ResinUsageMode.PerType; } }
    /// <summary>任务是否支持刷至耗尽。</summary>
    public bool IsExhaustModeEnabled => _capability.Modes.Contains(ResinUsageMode.Exhaust);
    /// <summary>任务是否支持指定领奖次数。</summary>
    public bool IsRunCountModeEnabled => _capability.Modes.Contains(ResinUsageMode.RunCount);
    /// <summary>任务是否支持按树脂分别指定。</summary>
    public bool IsPerTypeModeEnabled => _capability.Modes.Contains(ResinUsageMode.PerType);
    /// <summary>耗尽方式说明。</summary>
    public string ExhaustModeDescription => ResinUsageText.ModeDescription(ResinUsageMode.Exhaust, _capability);
    /// <summary>总次数方式说明。</summary>
    public string RunCountModeDescription => ResinUsageText.ModeDescription(ResinUsageMode.RunCount, _capability);
    /// <summary>分别指定方式说明或不可用原因。</summary>
    public string PerTypeModeDescription => ResinUsageText.ModeDescription(ResinUsageMode.PerType, _capability);
    /// <summary>总领奖次数只在指定次数方式显示。</summary>
    public bool ShowRunCount => Mode == ResinUsageMode.RunCount;
    /// <summary>当前树脂分组标题。</summary>
    public string KindsHeader => ResinUsageText.KindsHeader(Mode);
    /// <summary>当前树脂分组说明。</summary>
    public string KindsDescription => ResinUsageText.KindsDescription(Mode);
    /// <inheritdoc />
    public string Summary => ResinUsageText.Summary(BuildValue(), _capability);
    /// <inheritdoc />
    public string? ValidationMessage => BuildValue().Validate(_capability);
    /// <inheritdoc />
    public bool CanConfirm => _pendingConfirmations == 0 && ValidationMessage is null;
    /// <summary>当前确认链，供宿主或测试等待异步回退完成；普通绑定无需使用。</summary>
    public Task PendingConfirmation => _confirmationTask;

    /// <summary>整体构造新值；开关、次数和隐藏字段均原样保留。</summary>
    public ResinUsage BuildValue()
    {
        var value = new ResinUsage { Mode = Mode, RunCount = RunCount, OriginalTier = SelectedTier?.Tier ?? _initialTier };
        foreach (var row in Kinds)
        {
            value.Allowed[row.Kind] = row.IsAllowed;
            value.Counts[row.Kind] = row.Count;
        }
        return value;
    }

    /// <summary>切换方式只更新显示；进入耗尽且已有补充树脂时进行一次组合确认。</summary>
    partial void OnModeChanged(ResinUsageMode value)
    {
        _modeVersion++;
        RefreshPresentation();
        if (value != ResinUsageMode.Exhaust || _restoring) return;
        var enabled = Kinds.Where(row => IsSupplement(row.Kind) && row.IsAllowed && _capability.IsSupported(row.Kind)).ToArray();
        if (enabled.Length > 0)
            QueueConfirmation(new("刷至树脂耗尽", ResinUsageText.SwitchToExhaustMessage(enabled.Select(row => row.Kind))), enabled);
    }

    /// <summary>总次数变化后重新校验与更新摘要。</summary>
    partial void OnRunCountChanged(int value) => RefreshValue();
    /// <summary>档位变化后重新校验与更新摘要。</summary>
    partial void OnSelectedTierChanged(ResinTierChoice? value) => RefreshValue();

    /// <summary>只监听开关和次数编辑；显示属性通知不会触发确认。</summary>
    private void OnKindChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ResinKindRowViewModel row || e.PropertyName is not (nameof(row.IsAllowed) or nameof(row.Count))) return;
        if (e.PropertyName == nameof(row.IsAllowed))
        {
            _toggleVersions[row.Kind]++;
            // 锁定行不能被后续编辑关闭；初始化直接复制，保留导入值并由统一校验提示其问题。
            if (!_restoring && _capability.LockedKind == row.Kind && !row.IsAllowed)
            {
                RestoreAllowed(row, true);
            }
            else if (!_restoring && Mode == ResinUsageMode.Exhaust && IsSupplement(row.Kind)
                && row.IsAllowed && _capability.IsSupported(row.Kind))
            {
                QueueConfirmation(new($"使用{row.DisplayName}",
                    ResinUsageText.EnableSupplementMessage(row.Kind, _capability.Kinds[row.Kind])), [row]);
            }
        }
        RefreshValue();
    }

    /// <summary>恢复开关时抑制递归确认，仍保留正常属性通知和校验。</summary>
    private void RestoreAllowed(ResinKindRowViewModel row, bool allowed)
    {
        _restoring = true;
        try { row.IsAllowed = allowed; }
        finally { _restoring = false; }
    }

    /// <summary>补充树脂固定为须臾和脆弱。</summary>
    private static bool IsSupplement(ResinKind kind) => kind is ResinKind.Transient or ResinKind.Fragile;

    /// <summary>确认串行排队，保留当时的编辑版本；开关先显示开启，等待期间不能提交。</summary>
    private void QueueConfirmation(ResinConfirmation confirmation, IReadOnlyList<ResinKindRowViewModel> affected)
    {
        var snapshot = affected.Select(row => (Row: row, Version: _toggleVersions[row.Kind])).ToArray();
        _pendingConfirmations++;
        OnPropertyChanged(nameof(CanConfirm));
        _confirmationTask = ConfirmQueuedAsync(_confirmationTask, confirmation, snapshot, _modeVersion);
        OnPropertyChanged(nameof(PendingConfirmation));
    }

    /// <summary>否决只回退仍属于本次操作的开关，避免异步结果覆盖后续切换；回退不会再次弹窗。</summary>
    private async Task ConfirmQueuedAsync(Task previous, ResinConfirmation confirmation,
        (ResinKindRowViewModel Row, int Version)[] snapshot, int modeVersion)
    {
        try
        {
            await previous;
            if (modeVersion != _modeVersion || Mode != ResinUsageMode.Exhaust) return;
            if (!snapshot.Any(item => item.Row.IsAllowed && _toggleVersions[item.Row.Kind] == item.Version)) return;
            if (await _confirm(confirmation)) return;
            if (modeVersion != _modeVersion || Mode != ResinUsageMode.Exhaust) return;
            foreach (var item in snapshot)
                if (item.Row.IsAllowed && _toggleVersions[item.Row.Kind] == item.Version)
                    RestoreAllowed(item.Row, false);
        }
        finally
        {
            _pendingConfirmations--;
            RefreshValue();
        }
    }

    /// <summary>同步三组单选、条件行和四行说明，不改变任何草稿值。</summary>
    private void RefreshPresentation()
    {
        foreach (var row in Kinds) row.UpdatePresentation(Mode, _capability);
        OnPropertyChanged(nameof(IsExhaustMode));
        OnPropertyChanged(nameof(IsRunCountMode));
        OnPropertyChanged(nameof(IsPerTypeMode));
        OnPropertyChanged(nameof(ShowRunCount));
        OnPropertyChanged(nameof(KindsHeader));
        OnPropertyChanged(nameof(KindsDescription));
        RefreshValue();
    }

    /// <summary>所有编辑均通过同一任务层校验和文本更新状态。</summary>
    private void RefreshValue()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(CanConfirm));
    }
}
