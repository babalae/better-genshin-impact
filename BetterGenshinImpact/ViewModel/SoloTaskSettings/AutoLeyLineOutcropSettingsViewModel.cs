using System.Collections.ObjectModel;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoLeyLineOutcrop;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.ViewModel.Resin;
using Microsoft.Extensions.DependencyInjection;
using BetterGenshinImpact.Pulonia.Executors;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>自动地脉花任务的专属设置草稿；对应 auto_ley_line_outcrop 的常用参数，沿用旧一条龙卡片布局。</summary>
public partial class AutoLeyLineOutcropSettingsViewModel : SoloTaskSettingsViewModel
{
    /// <inheritdoc />
    public override string[] EditedParameterNames =>
    [
        "one_dragon_mode", "resin_usage", PuloniaSoloTaskExecutor.WeekdayOverridesParameter
    ];

    /// <summary>每周安排的日期顺序，索引与执行器的服务器星期值一致（0 为周日）。</summary>
    private static readonly string[] DayNames = ["周日", "周一", "周二", "周三", "周四", "周五", "周六"];

    /// <summary>地脉花类型选项；首项留空表示沿用默认。</summary>
    public static readonly string[] TypeChoices = ["", "启示之花", "藏金之花"];
    /// <summary>国家选项；首项留空表示沿用默认。</summary>
    public static readonly string[] CountryChoices =
        ["", "蒙德", "璃月", "稻妻", "须弥", "枫丹", "纳塔", "挪德卡莱", "至冬"];

    /// <summary>载入时的跳过准备流程快照。</summary>
    private readonly bool _originalOneDragonMode;

    /// <summary>是否跳过部分准备流程（例如传送回七天神像）。</summary>
    [ObservableProperty] private bool _oneDragonMode;
    /// <summary>周日至周六的运行日期输入行，索引 0 为周日。</summary>
    public ObservableCollection<AutoLeyLineOutcropWeekdayRowViewModel> Weekdays { get; } = [];

    /// <summary>从当前有效参数读取地脉花配置并还原运行日期。</summary>
    /// <param name="original">当前有效参数快照。</param>
    public AutoLeyLineOutcropSettingsViewModel(JObject original) : this(original, null) { }

    /// <summary>初始化后的树脂快照，用于按结构收集变化。</summary>
    private readonly ResinUsage _originalResin;
    /// <summary>树脂使用设置入口。</summary>
    public ResinUsageEntryViewModel Resin { get; }

    /// <summary>注入可选的树脂弹窗服务，不依赖应用静态容器。</summary>
    [ActivatorUtilitiesConstructor]
    public AutoLeyLineOutcropSettingsViewModel(JObject original, IResinSettingsDialogs? dialogs = null) : base(original)
    {
        _originalResin = ResinUsageViewModelSupport.Read(Original["resin_usage"], AutoLeyLineOutcropTask.ResinCapability);
        Resin = new("自动地脉花", AutoLeyLineOutcropTask.ResinCapability, _originalResin, dialogs);
        TrackEdits([Resin]);
        _originalOneDragonMode = ReadBool(Original, "one_dragon_mode", true);
        _oneDragonMode = _originalOneDragonMode;

        var weekly = Original[PuloniaSoloTaskExecutor.WeekdayOverridesParameter] as JObject;
        for (var i = 0; i < DayNames.Length; i++)
        {
            var row = new AutoLeyLineOutcropWeekdayRowViewModel(DayNames[i]);
            if (weekly?[i.ToString()] is JObject day)
            {
                // 未写 enabled 的历史条目按当天执行处理，与执行器的回退一致。
                row.Enabled = ReadBool(day, "enabled", true);
                row.Type = ReadString(day, "ley_line_outcrop_type");
                row.Country = ReadString(day, "country");
            }
            Weekdays.Add(row);
        }
        TrackEdits(Weekdays);
    }

    /// <summary>收集地脉花配置的真实变化；周安排整体重建并保留“全部未勾选则每天执行”的旧语义。</summary>
    /// <param name="changes">接收变化的对象。</param>
    public override void CollectChanges(JObject changes)
    {
        if (Resin.Value != _originalResin) changes["resin_usage"] = JObject.Parse(SoloTaskJson.Serialize(Resin.Value));
        WriteBool(changes, "one_dragon_mode", _originalOneDragonMode, OneDragonMode);

        // 每周安排：七天各一项，只写入非空的类型与国家；全部未勾选时视为每天执行
        var anyEnabledDay = Weekdays.Any(row => row.Enabled);
        var original = Original[PuloniaSoloTaskExecutor.WeekdayOverridesParameter];
        var weekly = original is JObject originalWeekly ? (JObject)originalWeekly.DeepClone() : new JObject();
        for (var i = 0; i < DayNames.Length; i++)
        {
            var row = Weekdays[i];
            var day = (original as JObject)?[i.ToString()] as JObject ?? new JObject();
            var entry = (JObject)day.DeepClone();
            var enabled = !anyEnabledDay || row.Enabled;
            if (enabled != ReadBool(day, "enabled", true)) entry["enabled"] = enabled;
            UpdateWeekdayString(entry, day, "ley_line_outcrop_type", row.Type);
            UpdateWeekdayString(entry, day, "country", row.Country);
            if (entry.Count > 0 || (original as JObject)?.ContainsKey(i.ToString()) == true)
                weekly[i.ToString()] = entry;
        }

        // 七天都只是“当天执行”时与没有每周安排等价，快照里没有安排就不写入
        var noOverride = weekly.Count == 0;
        if (noOverride && original is null or { Type: JTokenType.Null } or JObject { Count: 0 }) return;
        if (!JToken.DeepEquals(weekly, original)) changes[PuloniaSoloTaskExecutor.WeekdayOverridesParameter] = weekly;
    }

    /// <summary>更新实际改动的单日字段，保留其他运行参数，清空输入时恢复继承。</summary>
    private static void UpdateWeekdayString(JObject entry, JObject original, string name, string current)
    {
        if (ReadString(original, name) == current) return;
        if (string.IsNullOrWhiteSpace(current)) entry.Remove(name);
        else entry[name] = current;
    }
}
