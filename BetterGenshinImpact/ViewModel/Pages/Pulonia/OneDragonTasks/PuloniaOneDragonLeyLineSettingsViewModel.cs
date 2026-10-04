using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia.OneDragonTasks;

/// <summary>自动地脉花任务的专属设置草稿；对应 builtin.auto_ley_line 的常用参数，沿用旧一条龙卡片布局。</summary>
public partial class PuloniaOneDragonLeyLineSettingsViewModel : PuloniaOneDragonFixedSettingsViewModel
{
    /// <summary>每周安排的日期顺序，索引与执行器的服务器星期值一致（0 为周日）。</summary>
    private static readonly string[] DayNames = ["周日", "周一", "周二", "周三", "周四", "周五", "周六"];

    /// <summary>载入时的跳过准备流程快照。</summary>
    private readonly bool _originalOneDragonMode;
    /// <summary>载入时的树脂耗尽模式快照。</summary>
    private readonly bool _originalResinExhaustionMode;
    /// <summary>载入时的次数取小值快照。</summary>
    private readonly bool _originalOpenModeCountMin;
    /// <summary>载入时的刷取次数快照。</summary>
    private readonly int _originalCount;

    /// <summary>地脉花类型选项；首项留空表示沿用默认。</summary>
    public static readonly string[] TypeChoices = ["", "启示之花", "藏金之花"];
    /// <summary>国家选项；首项留空表示沿用默认。</summary>
    public static readonly string[] CountryChoices =
        ["", "蒙德", "璃月", "稻妻", "须弥", "枫丹", "纳塔", "挪德卡莱", "至冬"];

    /// <summary>是否跳过部分准备流程（例如传送回七天神像）。</summary>
    [ObservableProperty] private bool _oneDragonMode;
    /// <summary>是否按当前树脂与库存自动计算可刷次数。</summary>
    [ObservableProperty] private bool _resinExhaustionMode;
    /// <summary>是否与手动次数取最小值。</summary>
    [ObservableProperty] private bool _openModeCountMin;
    /// <summary>地脉花刷取次数。</summary>
    [ObservableProperty] private int _count;
    /// <summary>周一至周日的运行日期输入行，索引 0 为周日。</summary>
    public ObservableCollection<PuloniaOneDragonLeyLineWeekdayRowViewModel> Weekdays { get; } = [];

    /// <summary>从节点有效参数读取地脉花配置并还原运行日期。</summary>
    public PuloniaOneDragonLeyLineSettingsViewModel(PuloniaTaskNodeViewModel node) : base(node)
    {
        _originalOneDragonMode = ReadBool(Original, "one_dragon_mode", true);
        _originalResinExhaustionMode = ReadBool(Original, "is_resin_exhaustion_mode");
        _originalOpenModeCountMin = ReadBool(Original, "open_mode_count_min");
        _originalCount = ReadInt(Original, "count", 6);
        _oneDragonMode = _originalOneDragonMode;
        _resinExhaustionMode = _originalResinExhaustionMode;
        _openModeCountMin = _originalOpenModeCountMin;
        _count = _originalCount;
        for (var i = 0; i < DayNames.Length; i++)
        {
            var row = new PuloniaOneDragonLeyLineWeekdayRowViewModel(DayNames[i]);
            if (Original["weekday_overrides"]?[i.ToString()] is JObject day)
            {
                // 未写 enabled 的历史条目按当天执行处理，与执行器的回退一致。
                row.Enabled = ReadBool(day, "enabled", true);
                row.Type = ReadString(day, "ley_line_outcrop_type");
                row.Country = ReadString(day, "country");
            }
            Weekdays.Add(row);
        }
    }

    /// <summary>收集地脉花配置的真实变化；周安排整体重建并保留“全部未勾选则每天执行”的旧语义。</summary>
    public override void CollectChanges(JObject changes)
    {
        WriteBool(changes, "one_dragon_mode", _originalOneDragonMode, OneDragonMode);
        WriteBool(changes, "is_resin_exhaustion_mode", _originalResinExhaustionMode, ResinExhaustionMode);
        WriteBool(changes, "open_mode_count_min", _originalOpenModeCountMin, OpenModeCountMin);
        WriteInt(changes, "count", _originalCount, Count);

        var anyEnabledDay = Weekdays.Any(row => row.Enabled);
        var weekly = new JObject();
        for (var i = 0; i < DayNames.Length; i++)
        {
            var row = Weekdays[i];
            var entry = new JObject { ["enabled"] = !anyEnabledDay || row.Enabled };
            if (!string.IsNullOrWhiteSpace(row.Type)) entry["ley_line_outcrop_type"] = row.Type;
            if (!string.IsNullOrWhiteSpace(row.Country)) entry["country"] = row.Country;
            weekly[i.ToString()] = entry;
        }
        if (!JToken.DeepEquals(weekly, Original["weekday_overrides"])) changes["weekday_overrides"] = weekly;
    }
}
