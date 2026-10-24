using System;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoCombo.ComboRun;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Controls.CombatStrategy;

/// <summary>自动连招说明与建树状态，允许在未建树时保存选择。</summary>
public partial class ComboCategoryViewModel : StrategyCategoryViewModel
{
    /// <summary>统一选择回调。</summary>
    private readonly Action<CombatStrategyRef> _select;
    /// <summary>是否已经有建树会话。</summary>
    [ObservableProperty] private bool _isTreeBuilt;
    /// <summary>当前会话的角色列表或未建树说明。</summary>
    [ObservableProperty] private string _statusText = "";

    /// <summary>创建自动连招类别。</summary>
    public ComboCategoryViewModel(Action<CombatStrategyRef> select) : base(CombatStrategyKind.Combo, AutoFightParam.ComboStrategyName) => _select = select;

    /// <summary>每次打开时读取最新建树会话。</summary>
    public void Refresh()
    {
        var session = AutoComboRuntime.Session;
        IsTreeBuilt = session != null;
        StatusText = session == null ? "⚠ 尚未建树，请先运行一次“自动连招建树”。" : "已建树：" + string.Join(" · ", session.Avatars.Select(a => a.Name));
    }

    /// <summary>使用自动连招，实际运行时由工厂检查建树状态。</summary>
    [RelayCommand]
    private void Select() => _select(CombatStrategyResolver.Parse(AutoFightParam.ComboStrategyName));
}
