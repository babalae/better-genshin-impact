using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>
/// 体力计划的专属设置草稿：体力用量、优先合成浓缩、优先满足活动，以及四个消耗树脂任务的开关与各自设置。
/// 子任务设置通过弹窗编辑，体力计划决定的字段（树脂使用、秘境选择等）不在弹窗中显示；
/// 首领的讨伐次数沿用首领自己的树脂使用设置，在首领的设置弹窗中编辑。
/// </summary>
public partial class ResinPlanSettingsViewModel : SoloTaskSettingsViewModel
{
    /// <inheritdoc />
    public override string[] EditedParameterNames =>
        ["max_resin", "craft_condensed_first", "event_priority", "event_strict_resin", "domain", "boss", "stygian", "ley_line", "craft"];

    /// <summary>开启限定用量时的初始点数，约等于一天自然恢复的树脂。</summary>
    public const int DefaultResinLimit = 180;

    /// <summary>载入时的体力用量；0 表示全部。</summary>
    private readonly int _originalMaxResin;

    /// <summary>载入时的优先合成开关。</summary>
    private readonly bool _originalCraftFirst;

    /// <summary>载入时的合成台地区。</summary>
    private readonly string _originalCraftCountry;

    /// <summary>载入时的优先满足活动开关；参数缺失时与任务默认值一致，视为开启。</summary>
    private readonly bool _originalEventPriority;

    /// <summary>载入时的严格使用 60 树脂开关。</summary>
    private readonly bool _originalEventStrictResin;

    /// <summary>是否优先满足活动：移涌领取与紊乱爆发期优先使用树脂。</summary>
    [ObservableProperty] private bool _eventPriority;

    /// <summary>移涌领取是否严格使用 20 档：开启时 20 档领取 3 次，关闭时 40 档领取 2 次。</summary>
    [ObservableProperty] private bool _eventStrictResin;

    /// <summary>是否限定本次体力用量；关闭时用完全部原粹与浓缩树脂。</summary>
    [ObservableProperty] private bool _limitResin;

    /// <summary>限定用量时本次最多消耗的点数。</summary>
    [ObservableProperty] private int _resinLimit;

    /// <summary>刷取前是否先合成浓缩树脂。</summary>
    [ObservableProperty] private bool _craftCondensedFirst;

    /// <summary>合成台所在地区。</summary>
    [ObservableProperty] private string _craftCountry;

    /// <summary>合成台地区选项，与合成浓缩树脂任务一致。</summary>
    public static string[] CountryChoices => CraftCondensedResinSettingsViewModel.CountryChoices;

    /// <summary>自动秘境卡片。</summary>
    public ResinPlanSubTaskEntryViewModel Domain { get; }

    /// <summary>自动首领讨伐卡片。</summary>
    public ResinPlanSubTaskEntryViewModel Boss { get; }

    /// <summary>自动幽境危战卡片。</summary>
    public ResinPlanSubTaskEntryViewModel Stygian { get; }

    /// <summary>自动地脉花卡片。</summary>
    public ResinPlanSubTaskEntryViewModel LeyLine { get; }

    /// <summary>从当前有效参数读取体力计划设置。</summary>
    /// <param name="original">当前有效参数快照。</param>
    public ResinPlanSettingsViewModel(JObject original) : this(original, null) { }

    /// <summary>注入可选的子任务设置弹窗；隔离环境下子任务只显示摘要。</summary>
    /// <param name="original">当前有效参数快照。</param>
    /// <param name="dialogs">子任务设置弹窗。</param>
    [ActivatorUtilitiesConstructor]
    public ResinPlanSettingsViewModel(JObject original, IResinPlanSettingsDialogs? dialogs = null) : base(original)
    {
        // 1. 顶层设置
        _originalMaxResin = ReadInt(Original, "max_resin");
        _limitResin = _originalMaxResin > 0;
        _resinLimit = _originalMaxResin > 0 ? _originalMaxResin : DefaultResinLimit;
        _originalCraftFirst = ReadBool(Original, "craft_condensed_first");
        _craftCondensedFirst = _originalCraftFirst;
        _originalCraftCountry = Original["craft"] is JObject craft ? ReadString(craft, "country") : string.Empty;
        _craftCountry = _originalCraftCountry;
        _originalEventPriority = ReadBool(Original, "event_priority", true);
        _eventPriority = _originalEventPriority;
        _originalEventStrictResin = ReadBool(Original, "event_strict_resin");
        _eventStrictResin = _originalEventStrictResin;

        // 2. 子任务卡片；首领战斗不稳定，参数缺失时默认不参与
        Domain = new(ResinPlanSubTaskSpec.Domain, Original, true, dialogs);
        Boss = new(ResinPlanSubTaskSpec.Boss, Original, false, dialogs);
        Stygian = new(ResinPlanSubTaskSpec.Stygian, Original, true, dialogs);
        LeyLine = new(ResinPlanSubTaskSpec.LeyLine, Original, true, dialogs);
        TrackEdits([Domain, Boss, Stygian, LeyLine]);
    }

    /// <summary>只提交相对快照真实变化的字段；子任务对象整体提交。</summary>
    /// <param name="changes">接收变化的对象。</param>
    public override void CollectChanges(JObject changes)
    {
        WriteInt(changes, "max_resin", _originalMaxResin, LimitResin ? Math.Max(1, ResinLimit) : 0);
        WriteBool(changes, "craft_condensed_first", _originalCraftFirst, CraftCondensedFirst);
        WriteBool(changes, "event_priority", _originalEventPriority, EventPriority);
        WriteBool(changes, "event_strict_resin", _originalEventStrictResin, EventStrictResin);
        if (!string.Equals(_originalCraftCountry, CraftCountry, StringComparison.Ordinal))
        {
            // 合成设置的其他字段由体力计划在运行时决定，这里只改地区
            var craft = Original["craft"] is JObject current ? (JObject)current.DeepClone() : new JObject();
            craft["country"] = CraftCountry;
            changes["craft"] = craft;
        }

        Domain.CollectChanges(changes);
        Boss.CollectChanges(changes);
        Stygian.CollectChanges(changes);
        LeyLine.CollectChanges(changes);
    }
}
