using BetterGenshinImpact.GameTask.Common.Resin;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Resin;

/// <summary>树脂设置入口，只在弹窗确定或外部 Reset 时整体替换值。</summary>
public partial class ResinUsageEntryViewModel : ObservableObject
{
    /// <summary>标题中的任务名。</summary>
    private readonly string _taskName;
    /// <summary>任务能力。</summary>
    private readonly ResinUsageCapability _capability;
    /// <summary>弹窗服务，离线展示时允许为空。</summary>
    private readonly IResinSettingsDialogs? _dialogs;
    /// <summary>入口持有的独立值，不对外暴露可变引用。</summary>
    private ResinUsage _value;

    /// <summary>建立入口并复制当前值。</summary>
    public ResinUsageEntryViewModel(string taskName, ResinUsageCapability capability, ResinUsage value, IResinSettingsDialogs? dialogs)
    {
        _taskName = taskName;
        _capability = capability;
        _value = value.Clone();
        _dialogs = dialogs;
    }

    /// <summary>当前设置的独立副本，调用方通过 Reset 或弹窗整体更新。</summary>
    public ResinUsage Value => _value.Clone();
    /// <summary>卡片摘要。</summary>
    public string Summary => ResinUsageText.Summary(_value, _capability);
    /// <summary>无效原因，合法时为 null。</summary>
    public string? Problem => _value.Validate(_capability);
    /// <summary>是否显示警告。</summary>
    public bool HasProblem => Problem is not null;
    /// <summary>是否注入了可打开弹窗的服务。</summary>
    public bool CanEdit => _dialogs is not null;

    /// <summary>打开草稿弹窗，取消和相等的值均不通知宿主回写。</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
        if (_dialogs?.EditResinUsage(_taskName, _capability, _value.Clone()) is { } value)
            Reset(value);
    }

    /// <summary>接收外部的新值，复制后仅在结构不同的情况下通知绑定。</summary>
    public void Reset(ResinUsage value)
    {
        if (_value == value) return;
        _value = value.Clone();
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(HasProblem));
    }
}
