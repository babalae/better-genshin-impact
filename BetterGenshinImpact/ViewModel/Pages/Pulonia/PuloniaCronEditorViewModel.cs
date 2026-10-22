using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>五段 Cron 表达式编辑器，为直接输入和快捷配置提供唯一的表达式结果。</summary>
public partial class PuloniaCronEditorViewModel : ObservableObject
{
    /// <summary>最终提交给调度器的五段 Cron 表达式。</summary>
    [ObservableProperty] private string _expression;
    /// <summary>快捷设置下拉层是否打开。</summary>
    [ObservableProperty] private bool _isPopupOpen;
    /// <summary>快捷设置页签，0 表示周期执行，1 表示固定时间执行。</summary>
    [ObservableProperty] private int _generatorTabIndex;
    /// <summary>周期单位，minute 表示按分钟，hour 表示按小时。</summary>
    [ObservableProperty] private string _periodUnit = "minute";
    /// <summary>按分钟执行时的周期值。</summary>
    [ObservableProperty] private int _minuteInterval = 5;
    /// <summary>按小时执行时的周期值。</summary>
    [ObservableProperty] private int _hourInterval = 1;
    /// <summary>按小时执行时每个命中小时内的开始分钟。</summary>
    [ObservableProperty] private int _hourStartMinute;
    /// <summary>固定时间类型，支持每天、工作日、每周和每月。</summary>
    [ObservableProperty] private string _fixedFrequency = "daily";
    /// <summary>固定时间的小时。</summary>
    [ObservableProperty] private int _fixedHour = 4;
    /// <summary>固定时间的分钟。</summary>
    [ObservableProperty] private int _fixedMinute;
    /// <summary>每周执行的星期，0 表示周日。</summary>
    [ObservableProperty] private int _fixedWeekday = 1;
    /// <summary>每月执行的日期。</summary>
    [ObservableProperty] private int _fixedMonthDay = 1;

    /// <summary>周期单位选项。</summary>
    public IReadOnlyDictionary<string, string> PeriodUnits { get; } = new Dictionary<string, string>
    {
        ["minute"] = "分钟",
        ["hour"] = "小时"
    };

    /// <summary>分钟周期选项，只提供能整除 60 的值以保持跨小时节奏一致。</summary>
    public IReadOnlyList<int> MinuteIntervals { get; } = [1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30];

    /// <summary>小时周期选项，只提供能整除 24 的值以保持跨天节奏一致。</summary>
    public IReadOnlyList<int> HourIntervals { get; } = [1, 2, 3, 4, 6, 8, 12];

    /// <summary>固定时间类型选项。</summary>
    public IReadOnlyDictionary<string, string> FixedFrequencies { get; } = new Dictionary<string, string>
    {
        ["daily"] = "每天",
        ["weekdays"] = "工作日",
        ["weekly"] = "每周",
        ["monthly"] = "每月"
    };

    /// <summary>星期选项。</summary>
    public IReadOnlyDictionary<int, string> Weekdays { get; } = new Dictionary<int, string>
    {
        [1] = "周一",
        [2] = "周二",
        [3] = "周三",
        [4] = "周四",
        [5] = "周五",
        [6] = "周六",
        [0] = "周日"
    };

    /// <summary>可以直接写入表达式的常用 Cron 预设。</summary>
    public IReadOnlyDictionary<string, string> Presets { get; } = new Dictionary<string, string>
    {
        ["每 5 分钟"] = "*/5 * * * *",
        ["每 15 分钟"] = "*/15 * * * *",
        ["每小时"] = "0 * * * *",
        ["每天 04:00"] = "0 4 * * *",
        ["工作日 09:00"] = "0 9 * * 1-5",
        ["每周一 04:00"] = "0 4 * * 1",
        ["每月 1 日 04:00"] = "0 4 1 * *"
    };

    /// <summary>小时选项。</summary>
    public IReadOnlyList<int> Hours { get; } = Enumerable.Range(0, 24).ToArray();

    /// <summary>分钟选项。</summary>
    public IReadOnlyList<int> Minutes { get; } = Enumerable.Range(0, 60).ToArray();

    /// <summary>每月日期选项。</summary>
    public IReadOnlyList<int> MonthDays { get; } = Enumerable.Range(1, 31).ToArray();

    /// <summary>是否正在配置分钟周期。</summary>
    public bool IsMinutePeriod => PeriodUnit == "minute";

    /// <summary>是否正在配置小时周期。</summary>
    public bool IsHourPeriod => PeriodUnit == "hour";

    /// <summary>固定时间是否需要选择星期。</summary>
    public bool IsWeeklyFixed => FixedFrequency == "weekly";

    /// <summary>固定时间是否需要选择每月日期。</summary>
    public bool IsMonthlyFixed => FixedFrequency == "monthly";

    /// <summary>当前快捷选项将生成的表达式。</summary>
    public string GeneratedExpression => GeneratorTabIndex == 0 ? CreatePeriodExpression() : CreateFixedExpression();

    /// <summary>当前快捷选项的语义说明。</summary>
    public string GeneratedDescription => GeneratorTabIndex == 0
        ? PeriodUnit == "minute"
            ? $"每 {MinuteInterval} 分钟执行"
            : $"每 {HourInterval} 小时的第 {HourStartMinute:D2} 分执行"
        : FixedFrequency switch
        {
            "weekdays" => $"每个工作日 {FixedHour:D2}:{FixedMinute:D2} 执行",
            "weekly" => $"每{Weekdays[FixedWeekday]} {FixedHour:D2}:{FixedMinute:D2} 执行",
            "monthly" => $"每月 {FixedMonthDay} 日 {FixedHour:D2}:{FixedMinute:D2} 执行",
            _ => $"每天 {FixedHour:D2}:{FixedMinute:D2} 执行"
        };

    /// <summary>使用已有表达式建立编辑器。</summary>
    public PuloniaCronEditorViewModel(string expression)
    {
        _expression = expression;
    }

    /// <summary>快捷字段变化后刷新候选表达式和条件区域。</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(GeneratedExpression) or nameof(GeneratedDescription)) return;

        if (e.PropertyName == nameof(PeriodUnit))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsMinutePeriod)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsHourPeriod)));
        }
        if (e.PropertyName == nameof(FixedFrequency))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsWeeklyFixed)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsMonthlyFixed)));
        }
        if (e.PropertyName is nameof(GeneratorTabIndex) or nameof(PeriodUnit) or nameof(MinuteInterval)
            or nameof(HourInterval) or nameof(HourStartMinute) or nameof(FixedFrequency)
            or nameof(FixedHour) or nameof(FixedMinute) or nameof(FixedWeekday) or nameof(FixedMonthDay))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(GeneratedExpression)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(GeneratedDescription)));
        }
    }

    /// <summary>把当前快捷配置转换为表达式，并关闭下拉层。</summary>
    [RelayCommand]
    private void ApplyGeneratedExpression()
    {
        Expression = GeneratedExpression;
        IsPopupOpen = false;
    }

    /// <summary>应用常用预设，并直接写入唯一表达式。</summary>
    [RelayCommand]
    private void ApplyPreset(string expression)
    {
        Expression = expression;
        IsPopupOpen = false;
    }

    /// <summary>生成周期执行的五段 Cron 表达式。</summary>
    private string CreatePeriodExpression()
    {
        if (PeriodUnit == "minute")
            return MinuteInterval == 1 ? "* * * * *" : $"*/{MinuteInterval} * * * *";

        var hour = HourInterval == 1 ? "*" : $"*/{HourInterval}";
        return $"{HourStartMinute.ToString(CultureInfo.InvariantCulture)} {hour} * * *";
    }

    /// <summary>生成固定时间执行的五段 Cron 表达式。</summary>
    private string CreateFixedExpression()
    {
        var minute = FixedMinute.ToString(CultureInfo.InvariantCulture);
        var hour = FixedHour.ToString(CultureInfo.InvariantCulture);
        return FixedFrequency switch
        {
            "weekdays" => $"{minute} {hour} * * 1-5",
            "weekly" => $"{minute} {hour} * * {FixedWeekday.ToString(CultureInfo.InvariantCulture)}",
            "monthly" => $"{minute} {hour} {FixedMonthDay.ToString(CultureInfo.InvariantCulture)} * *",
            _ => $"{minute} {hour} * * *"
        };
    }
}
