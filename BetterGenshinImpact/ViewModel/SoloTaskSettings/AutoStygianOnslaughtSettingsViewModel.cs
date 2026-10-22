using System;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>自动幽境危战任务的专属设置草稿；对应 auto_stygian_onslaught 的常用参数，按旧卡片风格设计。</summary>
public partial class AutoStygianOnslaughtSettingsViewModel : SoloTaskSettingsViewModel
{
    /// <summary>幽境首领序号选项；与任务的 1—3 校验一致。</summary>
    public static readonly int[] BossNumChoices = [1, 2, 3];
    /// <summary>分解圣遗物的最高星级选项。</summary>
    public static readonly string[] ArtifactStarChoices = ["4", "3", "2", "1"];

    /// <summary>载入时的 strategy_name 原值。</summary>
    private readonly string _originalStrategyNameValue;
    /// <summary>载入时的 combat_script_bag_path 原值。</summary>
    private readonly string _originalCombatScriptBagPath;
    /// <summary>载入时的战斗策略显示名快照（按任务实际使用的策略换算）。</summary>
    private readonly string _originalStrategyName;
    /// <summary>载入时的首领序号快照。</summary>
    private readonly int _originalBossNum;
    /// <summary>载入时的队伍名快照。</summary>
    private readonly string _originalFightTeamName;
    /// <summary>载入时的树脂模式快照。</summary>
    private readonly bool _originalSpecifyResinUse;
    /// <summary>载入时的自动分解快照。</summary>
    private readonly bool _originalAutoArtifactSalvage;
    /// <summary>载入时的圣遗物星级快照。</summary>
    private readonly string _originalMaxArtifactStar;
    /// <summary>载入时的原粹树脂次数快照。</summary>
    private readonly int _originalOriginalResinUseCount;
    /// <summary>载入时的浓缩树脂次数快照。</summary>
    private readonly int _originalCondensedResinUseCount;
    /// <summary>载入时的须臾树脂次数快照。</summary>
    private readonly int _originalTransientResinUseCount;
    /// <summary>载入时的脆弱树脂次数快照。</summary>
    private readonly int _originalFragileResinUseCount;

    /// <summary>战斗策略选项；幽境危战不提供联合策略，当前值不在列表中时追加显示。</summary>
    public string[] StrategyChoices { get; }

    /// <summary>本任务使用的战斗策略名称；保存时写入 strategy_name 并清空 combat_script_bag_path。</summary>
    [ObservableProperty] private string _strategyName;
    /// <summary>挑战的幽境首领序号。</summary>
    [ObservableProperty] private int _bossNum;
    /// <summary>指派的战斗队伍名称。</summary>
    [ObservableProperty] private string _fightTeamName;
    /// <summary>是否按下方数量指定树脂使用。</summary>
    [ObservableProperty] private bool _specifyResinUse;
    /// <summary>原粹树脂挑战次数。</summary>
    [ObservableProperty] private int _originalResinUseCount;
    /// <summary>浓缩树脂挑战次数。</summary>
    [ObservableProperty] private int _condensedResinUseCount;
    /// <summary>须臾树脂挑战次数。</summary>
    [ObservableProperty] private int _transientResinUseCount;
    /// <summary>脆弱树脂挑战次数。</summary>
    [ObservableProperty] private int _fragileResinUseCount;
    /// <summary>结束后是否自动分解圣遗物。</summary>
    [ObservableProperty] private bool _autoArtifactSalvage;
    /// <summary>分解圣遗物的最高星级；空字符串表示沿用全局设置。</summary>
    [ObservableProperty] private string _maxArtifactStar;

    /// <summary>从当前有效参数读取幽境危战配置。</summary>
    /// <param name="original">当前有效参数快照。</param>
    public AutoStygianOnslaughtSettingsViewModel(JObject original) : base(original)
    {
        _originalStrategyNameValue = ReadString(Original, "strategy_name");
        _originalCombatScriptBagPath = ReadString(Original, "combat_script_bag_path");
        // 任务优先使用非空的 combat_script_bag_path，为空时才按 strategy_name 解析，显示与之一致
        _originalStrategyName = ToStrategyName(string.IsNullOrWhiteSpace(_originalCombatScriptBagPath)
            ? _originalStrategyNameValue
            : _originalCombatScriptBagPath);
        _originalBossNum = ReadInt(Original, "boss_num", 1);
        _originalFightTeamName = ReadString(Original, "fight_team_name");
        _originalSpecifyResinUse = ReadBool(Original, "specify_resin_use");
        _originalAutoArtifactSalvage = ReadBool(Original, "auto_artifact_salvage");
        _originalMaxArtifactStar = ReadString(Original, "max_artifact_star");
        _originalOriginalResinUseCount = ReadInt(Original, "original_resin_use_count");
        _originalCondensedResinUseCount = ReadInt(Original, "condensed_resin_use_count");
        _originalTransientResinUseCount = ReadInt(Original, "transient_resin_use_count");
        _originalFragileResinUseCount = ReadInt(Original, "fragile_resin_use_count");
        StrategyChoices = WithCurrentChoice(PuloniaOneDragonSettingChoices.GetStrategies(false), _originalStrategyName);
        _strategyName = _originalStrategyName;
        _bossNum = _originalBossNum;
        _fightTeamName = _originalFightTeamName;
        _specifyResinUse = _originalSpecifyResinUse;
        _autoArtifactSalvage = _originalAutoArtifactSalvage;
        _maxArtifactStar = _originalMaxArtifactStar;
        _originalResinUseCount = _originalOriginalResinUseCount;
        _condensedResinUseCount = _originalCondensedResinUseCount;
        _transientResinUseCount = _originalTransientResinUseCount;
        _fragileResinUseCount = _originalFragileResinUseCount;
    }

    /// <summary>收集幽境危战配置的真实变化；自定义树脂优先级保留给高级设置编辑。</summary>
    /// <param name="changes">接收变化的对象。</param>
    public override void CollectChanges(JObject changes)
    {
        // 修改策略时写策略名并清空脚本包路径，任务在路径为空时按策略名解析
        if (!string.Equals(_originalStrategyName, StrategyName, StringComparison.Ordinal))
        {
            WriteString(changes, "strategy_name", _originalStrategyNameValue, StrategyName);
            WriteString(changes, "combat_script_bag_path", _originalCombatScriptBagPath, string.Empty);
        }
        WriteInt(changes, "boss_num", _originalBossNum, BossNum);
        WriteString(changes, "fight_team_name", _originalFightTeamName, FightTeamName);
        WriteBool(changes, "specify_resin_use", _originalSpecifyResinUse, SpecifyResinUse);
        WriteInt(changes, "original_resin_use_count", _originalOriginalResinUseCount, OriginalResinUseCount);
        WriteInt(changes, "condensed_resin_use_count", _originalCondensedResinUseCount, CondensedResinUseCount);
        WriteInt(changes, "transient_resin_use_count", _originalTransientResinUseCount, TransientResinUseCount);
        WriteInt(changes, "fragile_resin_use_count", _originalFragileResinUseCount, FragileResinUseCount);
        WriteBool(changes, "auto_artifact_salvage", _originalAutoArtifactSalvage, AutoArtifactSalvage);
        WriteString(changes, "max_artifact_star", _originalMaxArtifactStar, MaxArtifactStar);
    }
}
