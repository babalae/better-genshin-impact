using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BetterGenshinImpact.Model;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia;

/// <summary>触发器草稿编辑器；所有快捷选项生成同一 Cron，不在输入未校验时修改计划。</summary>
public partial class PuloniaTaskTriggerEditorViewModel : ObservableObject
{
    /// <summary>保存前的独立副本，保留稳定身份和间隔锚点。</summary>
    private readonly PuloniaTaskTrigger _original;
    /// <summary>批量初始化时避免反复计算预览。</summary>
    private bool _initializing = true;
    /// <summary>触发器名称。</summary>
    [ObservableProperty] private string _name = "每日任务";
    /// <summary>启用开关，只有保存后才影响调度。</summary>
    [ObservableProperty] private bool _enabled;
    /// <summary>快捷日程种类，不引入第二套持久化表达式。</summary>
    [ObservableProperty] private string _mode = "daily";
    /// <summary>高级五段 Cron 输入。</summary>
    [ObservableProperty] private string _cron = "0 4 * * *";
    /// <summary>时区 ID。</summary>
    [ObservableProperty] private string _timeZoneId = "China Standard Time";
    /// <summary>小时输入，保留非法草稿以给出明确错误。</summary>
    [ObservableProperty] private string _hour = "4";
    /// <summary>分钟输入。</summary>
    [ObservableProperty] private string _minute = "0";
    /// <summary>每周的星期，0 是周日。</summary>
    [ObservableProperty] private int _weekday = 1;
    /// <summary>每月日期，不存在的日期会跳过。</summary>
    [ObservableProperty] private int _monthDay = 1;
    /// <summary>一次性或固定间隔的本地起算日期。</summary>
    [ObservableProperty] private DateTime? _date = DateTime.Today;
    /// <summary>间隔分钟数。</summary>
    [ObservableProperty] private string _intervalMinutes = "1440";
    /// <summary>捕获控件使用的类型化热键。</summary>
    [ObservableProperty] private HotKey _hotkey;
    /// <summary>复用软件现有的两种热键模式。</summary>
    [ObservableProperty] private HotKeyTypeEnum _hotkeyType;
    /// <summary>运行范围，空字符串表示整个计划。</summary>
    [ObservableProperty] private string _targetTaskId = "";
    /// <summary>账号参数资料引用，不执行切号。</summary>
    [ObservableProperty] private string _accountId = "";
    /// <summary>有效开始窗口，分钟。</summary>
    [ObservableProperty] private string _windowMinutes = "360";
    /// <summary>是否补触发。</summary>
    [ObservableProperty] private bool _catchUp = true;
    /// <summary>忙碌策略。</summary>
    [ObservableProperty] private PuloniaTaskBusyPolicy _busyPolicy;
    /// <summary>兼容原触发器的显式运行预算；空字符串表示不限时，不再提供总时限输入界面。</summary>
    [ObservableProperty] private string _timeoutSeconds = "";
    /// <summary>计算结果或错误，不把非法表达式提交到存储。</summary>
    [ObservableProperty] private string _previewText = "";
    /// <summary>当前草稿是否通过全部校验。</summary>
    [ObservableProperty] private bool _isValid;
    /// <summary>供高级模式显示的最终 Cron。</summary>
    [ObservableProperty] private string _effectiveCron = "";

    /// <summary>是否编辑热键配置。</summary>
    public bool IsHotkey => _original.Kind == PuloniaTaskTriggerKind.Hotkey;
    /// <summary>是否编辑日程配置。</summary>
    public bool IsSchedule => !IsHotkey;
    /// <summary>捕获控件识别的类型名称，与快捷键设置页保持一致。</summary>
    public string HotkeyTypeName => HotkeyType.ToChineseName();
    /// <summary>界面展示的本地化类型名称。</summary>
    public string LocalizedHotkeyTypeName => HotkeyType.ToLocalizedName();
    /// <summary>当前热键类型支持的输入及生效条件。</summary>
    public string HotkeyHelp => HotkeyType == HotKeyTypeEnum.GlobalRegister
        ? "全局热键：支持组合键或功能键，BGI 运行时生效。普通键请搭配 Ctrl / Alt / Win；F12 为系统保留。"
        : "键鼠监听：支持键盘单键或鼠标侧键，不支持组合键。与现有快捷键一致，启动功能、开启键鼠监听且游戏在前台时生效。";
    /// <summary>高级表达式区域是否可见。</summary>
    public bool IsAdvanced => Mode == "advanced";
    /// <summary>简易时分输入是否可见。</summary>
    public bool ShowTime => Mode != "advanced";
    /// <summary>星期选择是否可见。</summary>
    public bool IsWeekly => Mode == "weekly";
    /// <summary>每月日期是否可见。</summary>
    public bool IsMonthly => Mode == "monthly";
    /// <summary>起算日期是否可见。</summary>
    public bool ShowDate => Mode is "once" or "interval";
    /// <summary>间隔是否可见。</summary>
    public bool IsInterval => Mode == "interval";
    /// <summary>用户可理解的日程类型。</summary>
    public IReadOnlyDictionary<string, string> Modes { get; } = new Dictionary<string, string>
    {
        ["daily"] = "每天", ["weekly"] = "每周", ["monthly"] = "每月", ["once"] = "仅一次",
        ["interval"] = "固定间隔 / CD 周期", ["advanced"] = "高级 Cron"
    };
    /// <summary>星期的中文选项。</summary>
    public IReadOnlyDictionary<int, string> Weekdays { get; } = new Dictionary<int, string>
    { [1] = "周一", [2] = "周二", [3] = "周三", [4] = "周四", [5] = "周五", [6] = "周六", [0] = "周日" };
    /// <summary>每月可选日期。</summary>
    public IReadOnlyList<int> MonthDays { get; } = Enumerable.Range(1, 31).ToArray();
    /// <summary>显式时区选项，不随系统时区更改而悄悄改变日程。</summary>
    public IReadOnlyDictionary<string, string> TimeZones { get; } = TimeZoneInfo.GetSystemTimeZones()
        .ToDictionary(zone => zone.Id, zone => zone.DisplayName);
    /// <summary>忙碌策略的用户文本。</summary>
    public IReadOnlyDictionary<PuloniaTaskBusyPolicy, string> BusyPolicies { get; } = new Dictionary<PuloniaTaskBusyPolicy, string>
    {
        [PuloniaTaskBusyPolicy.Skip] = "同计划已忙碌：跳过 / 合并（推荐）",
        [PuloniaTaskBusyPolicy.QueueOnce] = "同计划运行后：再排一次",
        [PuloniaTaskBusyPolicy.StopCurrent] = "停止当前任务，清理后优先执行"
    };
    /// <summary>完整计划或编辑树内可选子树。</summary>
    public IReadOnlyDictionary<string, string> Targets { get; }
    /// <summary>当前计划绑定的账号参数资料。</summary>
    public IReadOnlyDictionary<string, string> Accounts { get; }

    /// <summary>从计划和触发器建立独立草稿，识别常见 Cron 后还原简易配置。</summary>
    public PuloniaTaskTriggerEditorViewModel(PuloniaTaskTrigger trigger, PuloniaTaskPlan plan)
    {
        _original = PuloniaTaskJson.Read<PuloniaTaskTrigger>(PuloniaTaskJson.Write(trigger));
        Name = trigger.Name; Enabled = trigger.Enabled; TimeZoneId = trigger.TimeZoneId; Cron = trigger.Cron;
        HotkeyType = trigger.HotkeyType; Hotkey = HotKey.FromString(trigger.Hotkey);
        TargetTaskId = trigger.TargetTaskId ?? ""; AccountId = trigger.AccountId ?? "";
        IntervalMinutes = trigger.IntervalMinutes.ToString(CultureInfo.InvariantCulture);
        WindowMinutes = trigger.WindowMinutes.ToString(CultureInfo.InvariantCulture);
        TimeoutSeconds = trigger.TimeoutSeconds?.ToString(CultureInfo.InvariantCulture) ?? "";
        CatchUp = trigger.CatchUp; BusyPolicy = trigger.BusyPolicy;
        if (trigger.ScheduleKind != PuloniaTaskScheduleKind.Cron)
        {
            Mode = trigger.ScheduleKind == PuloniaTaskScheduleKind.Once ? "once" : "interval";
            var local = TimeZoneInfo.ConvertTime(trigger.AnchorUtc, TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId));
            Date = local.Date; Hour = local.Hour.ToString(); Minute = local.Minute.ToString();
        }
        else
        {
            Mode = "advanced";
            var match = Regex.Match(Cron.Trim(), @"^(\d+)\s+(\d+)\s+(\*|\d+)\s+\*\s+(\*|\d+)$");
            if (match.Success && (match.Groups[3].Value == "*" || match.Groups[4].Value == "*"))
            {
                Minute = match.Groups[1].Value; Hour = match.Groups[2].Value;
                Mode = match.Groups[4].Value != "*" ? "weekly" : match.Groups[3].Value != "*" ? "monthly" : "daily";
                if (Mode == "weekly") Weekday = int.Parse(match.Groups[4].Value) % 7;
                if (Mode == "monthly") MonthDay = int.Parse(match.Groups[3].Value);
            }
        }
        var targets = new Dictionary<string, string> { [""] = "整个计划" };
        AddTargets(plan.RootTask, targets, 0);
        Targets = targets;
        var accounts = new Dictionary<string, string> { [""] = "不指定资料（当前登录账号）" };
        foreach (var account in plan.Accounts.Where(item => item.Enabled)) accounts[account.AccountId] = account.AccountId;
        Accounts = accounts;
        _initializing = false;
        RefreshPreview();
    }

    /// <summary>以稳定节点 ID 建立范围选项，不把运行地址或外部引用展开节点写回计划。</summary>
    private static void AddTargets(PuloniaTask task, Dictionary<string, string> targets, int depth)
    {
        if (depth > 0) targets[task.Id] = new string('　', depth - 1) + task.Name;
        foreach (var child in task.Children) AddTargets(child, targets, depth + 1);
    }

    /// <summary>字段变化后更新预览与区域可见性，复杂时间逻辑不写进 XAML。</summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_initializing || e.PropertyName is nameof(PreviewText) or nameof(IsValid) or nameof(EffectiveCron)) return;
        if (e.PropertyName == nameof(Mode))
            foreach (var property in new[] { nameof(IsAdvanced), nameof(ShowTime), nameof(IsWeekly), nameof(IsMonthly), nameof(ShowDate), nameof(IsInterval) })
                base.OnPropertyChanged(new PropertyChangedEventArgs(property));
        RefreshPreview();
    }

    /// <summary>快捷配置只改变草稿；CD 时长可手动修改，不承诺游戏实际副作用已完成。</summary>
    [RelayCommand]
    private void ApplyPreset(string preset)
    {
        _initializing = true;
        if (preset.StartsWith("interval", StringComparison.Ordinal))
        {
            Mode = "interval";
            IntervalMinutes = (int.Parse(preset[8..], CultureInfo.InvariantCulture) * 60).ToString(CultureInfo.InvariantCulture);
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId));
            Date = local.Date; Hour = local.Hour.ToString(); Minute = local.Minute.ToString();
        }
        else { Mode = preset == "weekly4" ? "weekly" : preset == "monthly4" ? "monthly" : "daily"; Hour = "4"; Minute = "0"; Weekday = 1; MonthDay = 1; }
        _initializing = false;
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Mode)));
    }

    /// <summary>进入高级模式时沿用当前快捷表单生成的表达式，不跳回原始默认 Cron。</summary>
    partial void OnModeChanged(string oldValue, string newValue)
    {
        if (!_initializing && newValue == "advanced" && IsValid && EffectiveCron.Length > 0
            && !EffectiveCron.StartsWith("不使用", StringComparison.Ordinal)) Cron = EffectiveCron;
    }

    /// <summary>快捷选择固定 UTC 时区，不把服务器日切与系统夏令时混在一起。</summary>
    [RelayCommand]
    private void SetTimeZone(string id) => TimeZoneId = id;

    /// <summary>沿用快捷键页面的类型切换交互，清空不兼容的旧键值，等待用户重新捕获。</summary>
    [RelayCommand]
    private void SwitchHotkeyType()
        => HotkeyType = HotkeyType == HotKeyTypeEnum.GlobalRegister ? HotKeyTypeEnum.KeyboardMonitor : HotKeyTypeEnum.GlobalRegister;

    /// <summary>切换类型时同步输入规则、帮助和预览，不将组合键偷偷转换成监听单键。</summary>
    partial void OnHotkeyTypeChanged(HotKeyTypeEnum value)
    {
        if (!_initializing) Hotkey = HotKey.None;
        OnPropertyChanged(nameof(HotkeyTypeName));
        OnPropertyChanged(nameof(LocalizedHotkeyTypeName));
        OnPropertyChanged(nameof(HotkeyHelp));
    }

    /// <summary>卡片开关只同步已保存的启用状态与生效点，其他未保存字段继续保留为草稿。</summary>
    public void SynchronizeEnabled(PuloniaTaskTrigger saved)
    {
        _original.Enabled = saved.Enabled;
        _original.ActivatedAtUtc = saved.ActivatedAtUtc;
        Enabled = saved.Enabled;
    }

    /// <summary>解析整数输入，不允许浮点截断或静默修正。</summary>
    private static int Number(string value, string label, int min, int max)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result < min || result > max)
            throw new FormatException($"{label}必须是 {min}—{max} 的整数。");
        return result;
    }

    /// <summary>创建已校验的提交副本；只有内容真正变化才重置配置生效起点。</summary>
    public PuloniaTaskTrigger CreateTrigger()
    {
        var result = PuloniaTaskJson.Read<PuloniaTaskTrigger>(PuloniaTaskJson.Write(_original));
        result.Name = Name.Trim(); result.Enabled = Enabled; result.TimeZoneId = TimeZoneId;
        result.Hotkey = Hotkey.ToString(); result.HotkeyType = HotkeyType;
        result.TargetTaskId = string.IsNullOrEmpty(TargetTaskId) ? null : TargetTaskId;
        result.AccountId = string.IsNullOrEmpty(AccountId) ? null : AccountId;
        result.WindowMinutes = Number(WindowMinutes, "有效窗口（分钟）", 1, 10080);
        result.TimeoutSeconds = string.IsNullOrWhiteSpace(TimeoutSeconds) ? null : Number(TimeoutSeconds, "总时限（秒）", 1, 604800);
        result.CatchUp = CatchUp; result.BusyPolicy = BusyPolicy;
        if (IsSchedule)
        {
            result.ScheduleKind = Mode == "once" ? PuloniaTaskScheduleKind.Once : Mode == "interval" ? PuloniaTaskScheduleKind.Interval : PuloniaTaskScheduleKind.Cron;
            if (Mode == "advanced") result.Cron = Cron.Trim();
            else
            {
                var hour = Number(Hour, "小时", 0, 23); var minute = Number(Minute, "分钟", 0, 59);
                if (result.ScheduleKind == PuloniaTaskScheduleKind.Cron)
                    result.Cron = $"{minute} {hour} {(Mode == "monthly" ? MonthDay.ToString() : "*")} * {(Mode == "weekly" ? Weekday.ToString() : "*")}";
                if (ShowDate)
                {
                    if (Date is null) throw new FormatException("请选择起算日期。");
                    var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
                    var originalLocal = TimeZoneInfo.ConvertTime(_original.AnchorUtc, zone);
                    // 单纯改名/策略不能把持久化锚点的秒数截断，导致固定 CD 漂移。
                    result.AnchorUtc = Date.Value.Date == originalLocal.Date && hour == originalLocal.Hour && minute == originalLocal.Minute
                        && TimeZoneId == _original.TimeZoneId ? _original.AnchorUtc
                        : PuloniaCronExpression.ResolveLocal(Date.Value.Date.AddHours(hour).AddMinutes(minute), zone);
                    result.IntervalMinutes = Number(IntervalMinutes, "间隔分钟", 1, 525600);
                }
            }
        }
        if (PuloniaTaskSchedule.Signature(result) != PuloniaTaskSchedule.Signature(_original)) result.ActivatedAtUtc = DateTimeOffset.UtcNow;
        PuloniaTaskSchedule.Validate(result);
        return result;
    }

    /// <summary>预览五次执行，非法配置直接展示错误并阻止保存。</summary>
    private void RefreshPreview()
    {
        try
        {
            var trigger = CreateTrigger();
            EffectiveCron = trigger.ScheduleKind == PuloniaTaskScheduleKind.Cron ? trigger.Cron : "不使用 Cron（保存独立 UTC 起点）";
            var zone = TimeZoneInfo.FindSystemTimeZoneById(trigger.TimeZoneId);
            var lines = new List<string>();
            var after = DateTimeOffset.UtcNow;
            for (var index = 0; index < 5; index++)
            {
                var next = PuloniaTaskSchedule.Next(trigger, after);
                if (next is null) break;
                lines.Add(TimeZoneInfo.ConvertTime(next.Value, zone).ToString("yyyy-MM-dd ddd HH:mm zzz", CultureInfo.GetCultureInfo("zh-CN")));
                after = next.Value;
            }
            PreviewText = IsHotkey ? "保存后在左侧卡片启用。" + HotkeyHelp + " 快捷键冲突会在下方显示。" : lines.Count > 0
                ? "接下来执行（所选时区）：\n" + string.Join("\n", lines) : "没有未来执行时间（一次性时间可能已过去）。";
            IsValid = true;
        }
        catch (Exception ex) { PreviewText = "配置无效：" + ex.Message; EffectiveCron = "—"; IsValid = false; }
    }
}
