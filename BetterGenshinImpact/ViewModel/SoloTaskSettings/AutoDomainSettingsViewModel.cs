using System.Collections.ObjectModel;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>自动秘境任务的专属设置草稿；对应 auto_domain 的常用参数，沿用旧一条龙卡片布局。</summary>
public partial class AutoDomainSettingsViewModel : SoloTaskSettingsViewModel
{
    /// <inheritdoc />
    public override string[] EditedParameterNames =>
    [
        "party_name", "domain_name", "sunday_selected_value", "combat_strategy_path", "specify_resin_use",
        "original_resin_use_count", "condensed_resin_use_count", "transient_resin_use_count", "fragile_resin_use_count",
        "auto_artifact_salvage", "max_artifact_star", "reward_recognition_enabled",
        PuloniaSoloTaskExecutor.WeekdayOverridesParameter, PuloniaSoloTaskExecutor.WeekdayOverridesEnabledParameter
    ];

    /// <summary>周日或限时秘境的奖励序号选项；与旧一条龙的下拉一致。</summary>
    public static readonly string[] SundayChoices = ["", "1", "2", "3"];
    /// <summary>分解圣遗物的最高星级选项。</summary>
    public static readonly string[] ArtifactStarChoices = ["4", "3", "2", "1"];
    /// <summary>每周安排的日期顺序，索引与执行器的服务器星期值一致（0 为周日）。</summary>
    private static readonly string[] DayNames = ["周日", "周一", "周二", "周三", "周四", "周五", "周六"];

    /// <summary>载入时的队伍名快照；与当前输入对比决定是否提交。</summary>
    private readonly string _originalPartyName;
    /// <summary>载入时的秘境名快照。</summary>
    private readonly string _originalDomainName;
    /// <summary>载入时的周日奖励序号快照。</summary>
    private readonly string _originalSundayValue;
    /// <summary>载入时的战斗策略显示名快照（由 combat_strategy_path 换算）。</summary>
    private readonly string _originalStrategyName;
    /// <summary>载入时的圣遗物星级快照。</summary>
    private readonly string _originalMaxArtifactStar;
    /// <summary>载入时的树脂模式快照。</summary>
    private readonly bool _originalSpecifyResinUse;
    /// <summary>载入时的自动分解快照。</summary>
    private readonly bool _originalAutoArtifactSalvage;
    /// <summary>载入时的奖励识别快照。</summary>
    private readonly bool _originalRewardRecognitionEnabled;
    /// <summary>载入时的原粹树脂次数快照。</summary>
    private readonly int _originalOriginalResinUseCount;
    /// <summary>载入时的浓缩树脂次数快照。</summary>
    private readonly int _originalCondensedResinUseCount;
    /// <summary>载入时的须臾树脂次数快照。</summary>
    private readonly int _originalTransientResinUseCount;
    /// <summary>载入时的脆弱树脂次数快照。</summary>
    private readonly int _originalFragileResinUseCount;

    // 策略目录与当前值显示由统一的 CombatStrategyPicker 负责。

    /// <summary>进入秘境切换的队伍名称，留空不切换。</summary>
    [ObservableProperty] private string _partyName;
    /// <summary>每日刷取的秘境名称。</summary>
    [ObservableProperty] private string _domainName;
    /// <summary>每日模式下的周日奖励序号。</summary>
    [ObservableProperty] private string _sundayValue;
    /// <summary>本任务使用的战斗策略名称；保存时写入 combat_strategy_path。</summary>
    [ObservableProperty] private string _strategyName;
    /// <summary>是否按下方数量指定树脂使用。</summary>
    [ObservableProperty] private bool _specifyResinUse;
    /// <summary>原粹树脂刷取次数。</summary>
    [ObservableProperty] private int _originalResinUseCount;
    /// <summary>浓缩树脂刷取次数。</summary>
    [ObservableProperty] private int _condensedResinUseCount;
    /// <summary>须臾树脂刷取次数。</summary>
    [ObservableProperty] private int _transientResinUseCount;
    /// <summary>脆弱树脂刷取次数。</summary>
    [ObservableProperty] private int _fragileResinUseCount;
    /// <summary>结束后是否自动分解圣遗物。</summary>
    [ObservableProperty] private bool _autoArtifactSalvage;
    /// <summary>分解圣遗物的最高星级。</summary>
    [ObservableProperty] private string _maxArtifactStar;
    /// <summary>是否启用奖励名称识别。</summary>
    [ObservableProperty] private bool _rewardRecognitionEnabled;
    /// <summary>是否启用每周秘境配置；关闭时执行每日配置。</summary>
    [ObservableProperty] private bool _weeklyEnabled;
    /// <summary>周日至周六的每周安排输入行，索引 0 为周日。</summary>
    public ObservableCollection<AutoDomainWeekdayRowViewModel> Weekdays { get; } = [];

    /// <summary>从当前有效参数读取全部字段并还原每周安排。</summary>
    /// <param name="original">当前有效参数快照。</param>
    public AutoDomainSettingsViewModel(JObject original) : base(original)
    {
        _originalPartyName = ReadString(Original, "party_name");
        _originalDomainName = ReadString(Original, "domain_name");
        _originalSundayValue = ReadString(Original, "sunday_selected_value");
        // 默认参数里的策略可能是绝对路径，界面统一显示为策略名
        _originalStrategyName = ToStrategyName(ReadString(Original, "combat_strategy_path"));
        _originalMaxArtifactStar = ReadString(Original, "max_artifact_star");
        _originalSpecifyResinUse = ReadBool(Original, "specify_resin_use");
        _originalAutoArtifactSalvage = ReadBool(Original, "auto_artifact_salvage");
        _originalRewardRecognitionEnabled = ReadBool(Original, "reward_recognition_enabled");
        _originalOriginalResinUseCount = ReadInt(Original, "original_resin_use_count");
        _originalCondensedResinUseCount = ReadInt(Original, "condensed_resin_use_count");
        _originalTransientResinUseCount = ReadInt(Original, "transient_resin_use_count");
        _originalFragileResinUseCount = ReadInt(Original, "fragile_resin_use_count");
        _partyName = _originalPartyName;
        _domainName = _originalDomainName;
        _sundayValue = _originalSundayValue;
        _strategyName = _originalStrategyName;
        _maxArtifactStar = _originalMaxArtifactStar;
        _specifyResinUse = _originalSpecifyResinUse;
        _autoArtifactSalvage = _originalAutoArtifactSalvage;
        _rewardRecognitionEnabled = _originalRewardRecognitionEnabled;
        _originalResinUseCount = _originalOriginalResinUseCount;
        _condensedResinUseCount = _originalCondensedResinUseCount;
        _transientResinUseCount = _originalTransientResinUseCount;
        _fragileResinUseCount = _originalFragileResinUseCount;

        // 旧计划按 weekday_overrides 是否存在内容判断开关；新开关关闭时保留各日期配置。
        var weekly = Original[PuloniaSoloTaskExecutor.WeekdayOverridesParameter] as JObject;
        _weeklyEnabled = ReadBool(Original, PuloniaSoloTaskExecutor.WeekdayOverridesEnabledParameter, weekly is { Count: > 0 });
        for (var i = 0; i < DayNames.Length; i++)
        {
            var row = new AutoDomainWeekdayRowViewModel(DayNames[i]);
            if (weekly?[i.ToString()] is JObject day)
            {
                row.PartyName = ReadString(day, "party_name");
                row.DomainName = ReadString(day, "domain_name");
                row.SundayValue = ReadString(day, "sunday_selected_value");
            }
            Weekdays.Add(row);
        }
    }

    /// <summary>收集全部卡片输入的真实变化；每周安排整体重建，留空字段不写入即回退每日配置。</summary>
    /// <param name="changes">接收变化的对象。</param>
    public override void CollectChanges(JObject changes)
    {
        WriteString(changes, "party_name", _originalPartyName, PartyName);
        WriteString(changes, "domain_name", _originalDomainName, DomainName);
        WriteString(changes, "sunday_selected_value", _originalSundayValue, SundayValue);
        // 任务接受策略名或路径；修改时写入策略名，便于跨机器使用
        WriteString(changes, "combat_strategy_path", _originalStrategyName, StrategyName);
        WriteBool(changes, "specify_resin_use", _originalSpecifyResinUse, SpecifyResinUse);
        WriteInt(changes, "original_resin_use_count", _originalOriginalResinUseCount, OriginalResinUseCount);
        WriteInt(changes, "condensed_resin_use_count", _originalCondensedResinUseCount, CondensedResinUseCount);
        WriteInt(changes, "transient_resin_use_count", _originalTransientResinUseCount, TransientResinUseCount);
        WriteInt(changes, "fragile_resin_use_count", _originalFragileResinUseCount, FragileResinUseCount);
        WriteBool(changes, "auto_artifact_salvage", _originalAutoArtifactSalvage, AutoArtifactSalvage);
        WriteString(changes, "max_artifact_star", _originalMaxArtifactStar, MaxArtifactStar);
        WriteBool(changes, "reward_recognition_enabled", _originalRewardRecognitionEnabled, RewardRecognitionEnabled);

        // 每周安排：保留原有日期，只更新实际变化的输入；缺失视同空对象，避免无变化时写入。
        var originalWeekly = Original[PuloniaSoloTaskExecutor.WeekdayOverridesParameter] as JObject ?? new JObject();
        var weekly = (JObject)originalWeekly.DeepClone();
        // 开关只控制执行，关闭后仍保存单日输入，重新开启时无需重填。
        WriteBool(changes, PuloniaSoloTaskExecutor.WeekdayOverridesEnabledParameter,
            ReadBool(Original, PuloniaSoloTaskExecutor.WeekdayOverridesEnabledParameter, originalWeekly.Count > 0), WeeklyEnabled);
        for (var i = 0; i < DayNames.Length; i++)
        {
            var row = Weekdays[i];
            var day = originalWeekly[i.ToString()] as JObject ?? new JObject();
            var entry = (JObject)day.DeepClone();
            // 只更新改动的输入；其他执行器支持的按日覆盖继续保留。
            UpdateWeekdayString(entry, day, "party_name", row.PartyName);
            UpdateWeekdayString(entry, day, "domain_name", row.DomainName);
            UpdateWeekdayString(entry, day, "sunday_selected_value", row.SundayValue);
            if (entry.Count > 0 || originalWeekly.ContainsKey(i.ToString()))
                weekly[i.ToString()] = entry;
        }
        if (!JToken.DeepEquals(weekly, originalWeekly))
            changes[PuloniaSoloTaskExecutor.WeekdayOverridesParameter] = weekly;
    }

    /// <summary>修改单日输入，清空时恢复默认继承，保持未编辑字段的原始表示。</summary>
    private static void UpdateWeekdayString(JObject entry, JObject original, string name, string current)
    {
        if (ReadString(original, name) == current) return;
        if (string.IsNullOrWhiteSpace(current)) entry.Remove(name);
        else entry[name] = current;
    }
}
