using System;
using BetterGenshinImpact.GameTask.AutoDomain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.ViewModel.Pages.View;

/// <summary>
/// 自动秘境的“秘境来源”：把 domain_name 拆成“跟随培养计划”和“指定秘境”两个互斥选项。
/// 存储格式不变：跟随培养计划时 domain_name 为 <see cref="AutoDomainTask.TrainingGuideOption"/>，指定秘境时为秘境名（空为未选择）。
/// 在本对象存在期间记住上一次指定的秘境，切回“指定秘境”时恢复。
/// </summary>
public class DomainSourceViewModel : ObservableObject
{
    /// <summary>读取当前保存的秘境名。</summary>
    private readonly Func<string?> _getDomainName;

    /// <summary>写入秘境名。</summary>
    private readonly Action<string> _setDomainName;

    /// <summary>上一次指定的秘境名；切回“指定秘境”时恢复，未指定过时为空字符串。</summary>
    private string _lastManualDomainName = string.Empty;

    /// <summary>建立秘境来源；秘境名由宿主保存，宿主在秘境名变化后调用 <see cref="Refresh"/>。</summary>
    /// <param name="getDomainName">读取当前秘境名。</param>
    /// <param name="setDomainName">写入秘境名。</param>
    public DomainSourceViewModel(Func<string?> getDomainName, Action<string> setDomainName)
    {
        _getDomainName = getDomainName;
        _setDomainName = setDomainName;
        Remember();
    }

    /// <summary>
    /// 是否跟随培养计划。只处理选中：单选按钮在同组另一项被选中时会写回 false，忽略它以免覆盖秘境名。
    /// </summary>
    public bool IsTrainingPlan
    {
        get => IsTrainingPlanValue(_getDomainName());
        set
        {
            if (!value || IsTrainingPlan) return;
            Remember();
            _setDomainName(AutoDomainTask.TrainingGuideOption);
            Refresh();
        }
    }

    /// <summary>是否指定秘境；选中时恢复上一次指定的秘境。与 <see cref="IsTrainingPlan"/> 一样只处理选中。</summary>
    public bool IsManualDomain
    {
        get => !IsTrainingPlan;
        set
        {
            if (!value || !IsTrainingPlan) return;
            _setDomainName(_lastManualDomainName);
            Refresh();
        }
    }

    /// <summary>秘境名变化后刷新两个选项，并记住新指定的秘境。</summary>
    public void Refresh()
    {
        Remember();
        OnPropertyChanged(nameof(IsTrainingPlan));
        OnPropertyChanged(nameof(IsManualDomain));
    }

    /// <summary>判断保存的秘境名是否表示跟随培养计划。</summary>
    /// <param name="domainName">保存的秘境名。</param>
    /// <returns>跟随培养计划时返回 true。</returns>
    public static bool IsTrainingPlanValue(string? domainName) => domainName == AutoDomainTask.TrainingGuideOption;

    /// <summary>当前为指定秘境时记住秘境名（包括未选择），切回时原样恢复。</summary>
    private void Remember()
    {
        var name = _getDomainName();
        if (!IsTrainingPlanValue(name))
            _lastManualDomainName = name ?? string.Empty;
    }
}
