using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.ViewModel.Resin;
using Microsoft.Extensions.DependencyInjection;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>自动秘境任务的专属设置草稿；对应 auto_domain 的常用参数，沿用旧一条龙卡片布局。</summary>
public partial class AutoDomainSettingsViewModel : SoloTaskSettingsViewModel
{
    /// <inheritdoc />
    /// <remarks>
    /// 移涌活动秘境只在体力计划中生效，界面不提供编辑；列在这里使其在普通自动秘境节点中不显示为通用卡片，并保持原值。
    /// 体力计划的子任务设置弹窗直接按 Schema 生成卡片，不受此影响。
    /// </remarks>
    public override string[] EditedParameterNames =>
    [
        "party_name", "domain_name", "sunday_selected_value", "combat_strategy_path", "auto_artifact_salvage", "max_artifact_star", "reward_recognition_enabled", "resin_usage",
        "material_overflow_domain_name", "blessing_overflow_domain_name"
    ];

    /// <summary>周日或限时秘境的奖励序号选项；与旧一条龙的下拉一致。</summary>
    public static readonly string[] SundayChoices = ["", "1", "2", "3"];
    /// <summary>分解圣遗物的最高星级选项。</summary>
    public static readonly string[] ArtifactStarChoices = ["4", "3", "2", "1"];

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
    /// <summary>载入时的自动分解快照。</summary>
    private readonly bool _originalAutoArtifactSalvage;
    /// <summary>载入时的奖励识别快照。</summary>
    private readonly bool _originalRewardRecognitionEnabled;

    // 策略目录与当前值显示由统一的 CombatStrategyPicker 负责。

    /// <summary>进入秘境切换的队伍名称，留空不切换。</summary>
    [ObservableProperty] private string _partyName;
    /// <summary>刷取的秘境名称。</summary>
    [ObservableProperty] private string _domainName;
    /// <summary>周日或限时秘境的奖励序号。</summary>
    [ObservableProperty] private string _sundayValue;
    /// <summary>本任务使用的战斗策略名称；保存时写入 combat_strategy_path。</summary>
    [ObservableProperty] private string _strategyName;
    /// <summary>结束后是否自动分解圣遗物。</summary>
    [ObservableProperty] private bool _autoArtifactSalvage;
    /// <summary>分解圣遗物的最高星级。</summary>
    [ObservableProperty] private string _maxArtifactStar;
    /// <summary>是否启用奖励名称识别。</summary>
    [ObservableProperty] private bool _rewardRecognitionEnabled;
    /// <summary>载入时的树脂设置副本，格式错误时以能力默认值作为比较基准。</summary>
    private readonly ResinUsage _originalResin;
    /// <summary>只编辑当前参数草稿的树脂入口。</summary>
    public ResinUsageEntryViewModel Resin { get; }

    /// <summary>从当前有效参数读取全部字段。</summary>
    /// <param name="original">当前有效参数快照。</param>
    public AutoDomainSettingsViewModel(JObject original) : this(original, null) { }

    /// <summary>通过 DI 注入可选弹窗服务；隔离容器未注册时仍能显示草稿。</summary>
    [ActivatorUtilitiesConstructor]
    public AutoDomainSettingsViewModel(JObject original, IResinSettingsDialogs? dialogs = null) : base(original)
    {
        _originalResin = ResinUsageViewModelSupport.Read(Original["resin_usage"], AutoDomainTask.ResinCapability);
        Resin = new("自动秘境", AutoDomainTask.ResinCapability, _originalResin, dialogs);
        TrackEdits([Resin]);
        _originalPartyName = ReadString(Original, "party_name");
        _originalDomainName = ReadString(Original, "domain_name");
        _originalSundayValue = ReadString(Original, "sunday_selected_value");
        // 默认参数里的策略可能是绝对路径，界面统一显示为策略名
        _originalStrategyName = ToStrategyName(ReadString(Original, "combat_strategy_path"));
        _originalMaxArtifactStar = ReadString(Original, "max_artifact_star");
        _originalAutoArtifactSalvage = ReadBool(Original, "auto_artifact_salvage");
        _originalRewardRecognitionEnabled = ReadBool(Original, "reward_recognition_enabled");
        _partyName = _originalPartyName;
        _domainName = _originalDomainName;
        _sundayValue = _originalSundayValue;
        _strategyName = _originalStrategyName;
        _maxArtifactStar = _originalMaxArtifactStar;
        _autoArtifactSalvage = _originalAutoArtifactSalvage;
        _rewardRecognitionEnabled = _originalRewardRecognitionEnabled;
    }

    /// <summary>收集全部卡片输入的真实变化。</summary>
    /// <param name="changes">接收变化的对象。</param>
    public override void CollectChanges(JObject changes)
    {
        if (Resin.Value != _originalResin) changes["resin_usage"] = JObject.Parse(SoloTaskJson.Serialize(Resin.Value));
        WriteString(changes, "party_name", _originalPartyName, PartyName);
        WriteString(changes, "domain_name", _originalDomainName, DomainName);
        WriteString(changes, "sunday_selected_value", _originalSundayValue, SundayValue);
        // 任务接受策略名或路径；修改时写入策略名，便于跨机器使用
        WriteString(changes, "combat_strategy_path", _originalStrategyName, StrategyName);
        WriteBool(changes, "auto_artifact_salvage", _originalAutoArtifactSalvage, AutoArtifactSalvage);
        WriteString(changes, "max_artifact_star", _originalMaxArtifactStar, MaxArtifactStar);
        WriteBool(changes, "reward_recognition_enabled", _originalRewardRecognitionEnabled, RewardRecognitionEnabled);
    }
}
