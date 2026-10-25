using System;
using BetterGenshinImpact.GameTask.AutoStygianOnslaught;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.ViewModel.Resin;
using Microsoft.Extensions.DependencyInjection;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>自动幽境危战任务的专属设置草稿；对应 auto_stygian_onslaught 的常用参数，按旧卡片风格设计。</summary>
public partial class AutoStygianOnslaughtSettingsViewModel : SoloTaskSettingsViewModel
{
    /// <inheritdoc />
    public override string[] EditedParameterNames =>
    [
        "strategy_name", "combat_script_bag_path", "boss_num", "fight_team_name", "auto_artifact_salvage", "max_artifact_star", "resin_usage"
    ];

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
    /// <summary>载入时的自动分解快照。</summary>
    private readonly bool _originalAutoArtifactSalvage;
    /// <summary>载入时的圣遗物星级快照。</summary>
    private readonly string _originalMaxArtifactStar;

    // 策略目录与当前值显示由统一的 CombatStrategyPicker 负责。

    /// <summary>本任务使用的战斗策略名称；保存时写入 strategy_name 并清空 combat_script_bag_path。</summary>
    [ObservableProperty] private string _strategyName;
    /// <summary>挑战的幽境首领序号。</summary>
    [ObservableProperty] private int _bossNum;
    /// <summary>指派的战斗队伍名称。</summary>
    [ObservableProperty] private string _fightTeamName;
    /// <summary>结束后是否自动分解圣遗物。</summary>
    [ObservableProperty] private bool _autoArtifactSalvage;
    /// <summary>分解圣遗物的最高星级；空字符串表示沿用全局设置。</summary>
    [ObservableProperty] private string _maxArtifactStar;

    /// <summary>从当前有效参数读取幽境危战配置。</summary>
    /// <param name="original">当前有效参数快照。</param>
    public AutoStygianOnslaughtSettingsViewModel(JObject original) : this(original, null) { }

    /// <summary>原树脂快照；与入口结构比较，不因打开页面写入默认值。</summary>
    private readonly ResinUsage _originalResin;
    /// <summary>树脂使用草稿入口。</summary>
    public ResinUsageEntryViewModel Resin { get; }

    /// <summary>注入可选的公共树脂弹窗服务。</summary>
    [ActivatorUtilitiesConstructor]
    public AutoStygianOnslaughtSettingsViewModel(JObject original, IResinSettingsDialogs? dialogs = null) : base(original)
    {
        _originalResin = ResinUsageViewModelSupport.Read(Original["resin_usage"], AutoStygianOnslaughtTask.ResinCapability);
        Resin = new("自动幽境危战", AutoStygianOnslaughtTask.ResinCapability, _originalResin, dialogs);
        TrackEdits([Resin]);
        _originalStrategyNameValue = ReadString(Original, "strategy_name");
        _originalCombatScriptBagPath = ReadString(Original, "combat_script_bag_path");
        // 任务优先使用非空的 combat_script_bag_path，为空时才按 strategy_name 解析，显示与之一致
        _originalStrategyName = ToStrategyName(string.IsNullOrWhiteSpace(_originalCombatScriptBagPath)
            ? _originalStrategyNameValue
            : _originalCombatScriptBagPath);
        _originalBossNum = ReadInt(Original, "boss_num", 1);
        _originalFightTeamName = ReadString(Original, "fight_team_name");
        _originalAutoArtifactSalvage = ReadBool(Original, "auto_artifact_salvage");
        _originalMaxArtifactStar = ReadString(Original, "max_artifact_star");
        _strategyName = _originalStrategyName;
        _bossNum = _originalBossNum;
        _fightTeamName = _originalFightTeamName;
        _autoArtifactSalvage = _originalAutoArtifactSalvage;
        _maxArtifactStar = _originalMaxArtifactStar;
    }

    /// <summary>收集幽境危战配置的真实变化，树脂设置按完整对象提交。</summary>
    /// <param name="changes">接收变化的对象。</param>
    public override void CollectChanges(JObject changes)
    {
        if (Resin.Value != _originalResin) changes["resin_usage"] = JObject.Parse(SoloTaskJson.Serialize(Resin.Value));
        // 修改策略时写策略名并清空脚本包路径，任务在路径为空时按策略名解析
        if (!string.Equals(_originalStrategyName, StrategyName, StringComparison.Ordinal))
        {
            WriteString(changes, "strategy_name", _originalStrategyNameValue, StrategyName);
            WriteString(changes, "combat_script_bag_path", _originalCombatScriptBagPath, string.Empty);
        }
        WriteInt(changes, "boss_num", _originalBossNum, BossNum);
        WriteString(changes, "fight_team_name", _originalFightTeamName, FightTeamName);
        WriteBool(changes, "auto_artifact_salvage", _originalAutoArtifactSalvage, AutoArtifactSalvage);
        WriteString(changes, "max_artifact_star", _originalMaxArtifactStar, MaxArtifactStar);
    }
}
