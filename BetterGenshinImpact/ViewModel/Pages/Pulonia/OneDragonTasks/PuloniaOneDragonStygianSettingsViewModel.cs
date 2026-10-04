using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia.OneDragonTasks;

/// <summary>自动幽境危战任务的专属设置草稿；对应 builtin.auto_stygian 的常用参数，按旧卡片风格新设计。</summary>
public partial class PuloniaOneDragonStygianSettingsViewModel : PuloniaOneDragonFixedSettingsViewModel
{
    /// <summary>载入时的字段快照；与当前输入对比决定是否提交。</summary>
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

    /// <summary>幽境首领序号选项；与执行器的 1—3 校验一致。</summary>
    public static readonly int[] BossNumChoices = [1, 2, 3];
    /// <summary>分解圣遗物的最高星级选项。</summary>
    public static readonly string[] ArtifactStarChoices = ["4", "3", "2", "1"];
    /// <summary>战斗策略选项；幽境危战不提供联合策略。</summary>
    public string[] StrategyChoices { get; } = PuloniaOneDragonSettingChoices.GetStrategies(false);

    /// <summary>本任务使用的战斗策略名称。</summary>
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
    /// <summary>分解圣遗物的最高星级。</summary>
    [ObservableProperty] private string _maxArtifactStar;

    /// <summary>从节点有效参数读取幽境危战配置。</summary>
    public PuloniaOneDragonStygianSettingsViewModel(PuloniaTaskNodeViewModel node) : base(node)
    {
        _originalStrategyName = ReadString(Original, "strategy_name");
        _originalBossNum = ReadInt(Original, "boss_num", 1);
        _originalFightTeamName = ReadString(Original, "fight_team_name");
        _originalSpecifyResinUse = ReadBool(Original, "specify_resin_use");
        _originalAutoArtifactSalvage = ReadBool(Original, "auto_artifact_salvage");
        _originalMaxArtifactStar = ReadString(Original, "max_artifact_star");
        _originalOriginalResinUseCount = ReadInt(Original, "original_resin_use_count");
        _originalCondensedResinUseCount = ReadInt(Original, "condensed_resin_use_count");
        _originalTransientResinUseCount = ReadInt(Original, "transient_resin_use_count");
        _originalFragileResinUseCount = ReadInt(Original, "fragile_resin_use_count");
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

    /// <summary>收集幽境危战配置的真实变化；自定义树脂优先级保留给通用卡片编辑。</summary>
    public override void CollectChanges(JObject changes)
    {
        WriteString(changes, "strategy_name", _originalStrategyName, StrategyName);
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
