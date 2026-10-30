using System;
using System.Globalization;
using System.Windows.Data;
using BetterGenshinImpact.GameTask.AutoDomain;

namespace BetterGenshinImpact.View.Converters;

/// <summary>
/// 秘境名与“秘境来源”选项互转：秘境名为 <see cref="AutoDomainTask.TrainingGuideOption"/> 时表示跟随培养计划。
/// 供直接绑定配置的页面（旧一条龙）使用，配置格式不变；切回指定秘境时秘境名为空，不恢复之前的秘境。
/// </summary>
[ValueConversion(typeof(string), typeof(bool))]
public sealed class TrainingGuideDomainSourceConverter : IValueConverter
{
    /// <summary>为 true 时对应“指定秘境”选项：跟随培养计划时为 false，选中时写入空秘境名。</summary>
    public bool IsManualOption { get; set; }

    /// <summary>
    /// 取消选中时是否写回空秘境名。复选框使用 true；单选按钮使用 false，
    /// 同组另一项被选中时单选按钮会写回 false，忽略它以免覆盖另一项写入的秘境名。
    /// </summary>
    public bool ClearWhenUnchecked { get; set; }

    /// <summary>秘境名转为选项是否选中。</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isTrainingPlan = value as string == AutoDomainTask.TrainingGuideOption;
        return IsManualOption ? !isTrainingPlan : isTrainingPlan;
    }

    /// <summary>选项选中时写入对应的秘境名；取消选中按 <see cref="ClearWhenUnchecked"/> 处理。</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool isChecked)
            return Binding.DoNothing;
        if (!isChecked)
            return ClearWhenUnchecked ? string.Empty : Binding.DoNothing;
        return IsManualOption ? string.Empty : AutoDomainTask.TrainingGuideOption;
    }
}
