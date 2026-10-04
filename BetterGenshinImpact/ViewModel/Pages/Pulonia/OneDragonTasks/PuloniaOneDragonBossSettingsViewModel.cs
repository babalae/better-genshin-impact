using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.Pages.Pulonia.OneDragonTasks;

/// <summary>自动首领讨伐任务的专属设置草稿；对应 builtin.auto_boss 的常用参数，沿用旧一条龙卡片布局。</summary>
public partial class PuloniaOneDragonBossSettingsViewModel : PuloniaOneDragonFixedSettingsViewModel
{
    /// <summary>载入时的字段快照；与当前输入对比决定是否提交。</summary>
    private readonly string _originalStrategyName;
    /// <summary>载入时的首领名快照。</summary>
    private readonly string _originalBossName;
    /// <summary>载入时的队伍名快照。</summary>
    private readonly string _originalTeamName;
    /// <summary>载入时的次数限制开关快照。</summary>
    private readonly bool _originalSpecifyRunCount;
    /// <summary>载入时的单次运行上限快照。</summary>
    private readonly int _originalRunCount;
    /// <summary>载入时的须臾树脂补充快照。</summary>
    private readonly bool _originalUseTransientResin;
    /// <summary>载入时的脆弱树脂补充快照。</summary>
    private readonly bool _originalUseFragileResin;
    /// <summary>载入时的回雕像开关快照。</summary>
    private readonly bool _originalReturnToStatue;
    /// <summary>载入时的奖励识别快照。</summary>
    private readonly bool _originalRewardRecognitionEnabled;
    /// <summary>载入时的死亡重试次数快照。</summary>
    private readonly int _originalReviveRetryCount;

    /// <summary>战斗策略选项；首领讨伐不提供联合策略。</summary>
    public string[] StrategyChoices { get; } = PuloniaOneDragonSettingChoices.GetStrategies(false);

    /// <summary>本任务使用的战斗策略名称。</summary>
    [ObservableProperty] private string _strategyName;
    /// <summary>讨伐的首领名称。</summary>
    [ObservableProperty] private string _bossName;
    /// <summary>进入战斗前切换的队伍名称，留空不更换。</summary>
    [ObservableProperty] private string _teamName;
    /// <summary>是否限制成功领奖次数。</summary>
    [ObservableProperty] private bool _specifyRunCount;
    /// <summary>单次运行的成功领奖次数上限。</summary>
    [ObservableProperty] private int _runCount;
    /// <summary>原粹不足时是否使用须臾树脂补充。</summary>
    [ObservableProperty] private bool _useTransientResin;
    /// <summary>原粹不足时是否使用脆弱树脂补充。</summary>
    [ObservableProperty] private bool _useFragileResin;
    /// <summary>每轮领奖后是否返回七天神像。</summary>
    [ObservableProperty] private bool _returnToStatueAfterEachRound;
    /// <summary>是否启用奖励名称识别。</summary>
    [ObservableProperty] private bool _rewardRecognitionEnabled;
    /// <summary>角色死亡后的复活重试次数。</summary>
    [ObservableProperty] private int _reviveRetryCount;

    /// <summary>从节点有效参数读取首领讨伐配置。</summary>
    public PuloniaOneDragonBossSettingsViewModel(PuloniaTaskNodeViewModel node) : base(node)
    {
        _originalStrategyName = ReadString(Original, "strategy_name");
        _originalBossName = ReadString(Original, "boss_name");
        _originalTeamName = ReadString(Original, "team_name");
        _originalSpecifyRunCount = ReadBool(Original, "specify_run_count");
        _originalRunCount = ReadInt(Original, "run_count", 1);
        _originalUseTransientResin = ReadBool(Original, "use_transient_resin");
        _originalUseFragileResin = ReadBool(Original, "use_fragile_resin");
        _originalReturnToStatue = ReadBool(Original, "return_to_statue_after_each_round");
        _originalRewardRecognitionEnabled = ReadBool(Original, "reward_recognition_enabled");
        _originalReviveRetryCount = ReadInt(Original, "revive_retry_count", 3);
        _strategyName = _originalStrategyName;
        _bossName = _originalBossName;
        _teamName = _originalTeamName;
        _specifyRunCount = _originalSpecifyRunCount;
        _runCount = _originalRunCount;
        _useTransientResin = _originalUseTransientResin;
        _useFragileResin = _originalUseFragileResin;
        _returnToStatueAfterEachRound = _originalReturnToStatue;
        _rewardRecognitionEnabled = _originalRewardRecognitionEnabled;
        _reviveRetryCount = _originalReviveRetryCount;
    }

    /// <summary>收集首领讨伐配置的真实变化；累计上限与已完成次数不在新系统参数中，不参与提交。</summary>
    public override void CollectChanges(JObject changes)
    {
        WriteString(changes, "strategy_name", _originalStrategyName, StrategyName);
        WriteString(changes, "boss_name", _originalBossName, BossName);
        WriteString(changes, "team_name", _originalTeamName, TeamName);
        WriteBool(changes, "specify_run_count", _originalSpecifyRunCount, SpecifyRunCount);
        WriteInt(changes, "run_count", _originalRunCount, RunCount);
        WriteBool(changes, "use_transient_resin", _originalUseTransientResin, UseTransientResin);
        WriteBool(changes, "use_fragile_resin", _originalUseFragileResin, UseFragileResin);
        WriteBool(changes, "return_to_statue_after_each_round", _originalReturnToStatue, ReturnToStatueAfterEachRound);
        WriteBool(changes, "reward_recognition_enabled", _originalRewardRecognitionEnabled, RewardRecognitionEnabled);
        WriteInt(changes, "revive_retry_count", _originalReviveRetryCount, ReviveRetryCount);
    }
}
