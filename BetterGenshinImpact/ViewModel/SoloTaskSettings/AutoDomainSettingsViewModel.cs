using System;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;
using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.ViewModel.Resin;
using Microsoft.Extensions.DependencyInjection;
using BetterGenshinImpact.ViewModel.Pages.Pulonia;
using BetterGenshinImpact.ViewModel.Pages.View;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.ViewModel.SoloTaskSettings;

/// <summary>
/// 自动秘境任务的专属设置草稿；对应 auto_domain 的常用参数，沿用旧一条龙卡片布局。
/// 秘境来源分为“跟随培养计划”和“指定秘境”，培养计划设置只在跟随培养计划时显示。
/// </summary>
public partial class AutoDomainSettingsViewModel : SoloTaskSettingsViewModel
{
    /// <inheritdoc />
    /// <remarks>
    /// 移涌活动秘境只在体力计划中生效，界面不提供编辑；列在这里使其在普通自动秘境节点中不显示为通用卡片，并保持原值。
    /// 体力计划的子任务设置弹窗直接按 Schema 生成卡片，不受此影响。
    /// 培养计划设置（training_guide_*、自定义培养目标）放在秘境来源下方，不再作为通用卡片显示。
    /// </remarks>
    public override string[] EditedParameterNames =>
    [
        "party_name", "domain_name", "sunday_selected_value", "combat_strategy_path", "auto_artifact_salvage", "max_artifact_star", "reward_recognition_enabled", "resin_usage",
        "material_overflow_domain_name", "blessing_overflow_domain_name",
        TrainingGuideCalculateRunsKey, TrainingGuideRunPreferenceKey, TrainingGuideReservePercentKey,
        TrainingGuideRewardRecognitionKey, TrainingGuideRewardFailureBudgetKey, TrainingGuideFallbackDomainKey,
        TrainingGuideFallbackSundayKey, TrainingGuideLowArKey, TrainingGuideDiagnosticsKey, TrainingTargetsJsonKey
    ];

    /// <summary>周日或限时秘境的奖励序号选项；与旧一条龙的下拉一致。</summary>
    public static readonly string[] SundayChoices = ["", "1", "2", "3"];
    /// <summary>分解圣遗物的最高星级选项。</summary>
    public static readonly string[] ArtifactStarChoices = ["4", "3", "2", "1"];

    /// <summary>参数键：是否自动计算今日刷取次数。</summary>
    private const string TrainingGuideCalculateRunsKey = "training_guide_calculate_runs_enabled";
    /// <summary>参数键：刷取偏好（0 适量，1 充分）。</summary>
    private const string TrainingGuideRunPreferenceKey = "training_guide_run_preference";
    /// <summary>参数键：合成收益预留率。</summary>
    private const string TrainingGuideReservePercentKey = "training_guide_crafting_bonus_reserve_percent";
    /// <summary>参数键：按领奖结果更新养成库存。</summary>
    private const string TrainingGuideRewardRecognitionKey = "training_guide_reward_recognition_enabled";
    /// <summary>参数键：奖励识别失败时按固定预算继续。</summary>
    private const string TrainingGuideRewardFailureBudgetKey = "training_guide_reward_failure_budget_enabled";
    /// <summary>参数键：备选秘境。</summary>
    private const string TrainingGuideFallbackDomainKey = "training_guide_fallback_domain_name";
    /// <summary>参数键：备选秘境的周日或限时奖励序号。</summary>
    private const string TrainingGuideFallbackSundayKey = "training_guide_fallback_sunday_selected_value";
    /// <summary>参数键：低冒险等阶适配。</summary>
    private const string TrainingGuideLowArKey = "training_guide_low_ar_adaptation_enabled";
    /// <summary>参数键：培养计划详细诊断。</summary>
    private const string TrainingGuideDiagnosticsKey = "training_guide_diagnostics_enabled";
    /// <summary>参数键：自定义培养目标 JSON。</summary>
    private const string TrainingTargetsJsonKey = "training_targets_json";

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
    /// <summary>载入时的“自动计算今日刷取次数”快照。</summary>
    private readonly bool _originalCalculateRuns;
    /// <summary>载入时的刷取偏好快照，已归一化为 0 或 1。</summary>
    private readonly int _originalRunPreference;
    /// <summary>载入时的合成收益预留率快照，已限制在 0～20。</summary>
    private readonly int _originalReservePercent;
    /// <summary>载入时的“按领奖结果更新养成库存”快照。</summary>
    private readonly bool _originalGuideRewardRecognition;
    /// <summary>载入时的“识别失败时按固定预算继续”快照。</summary>
    private readonly bool _originalRewardFailureBudget;
    /// <summary>载入时的备选秘境快照。</summary>
    private readonly string _originalFallbackDomainName;
    /// <summary>载入时的备选秘境奖励序号快照。</summary>
    private readonly string _originalFallbackSundayValue;
    /// <summary>载入时的低冒险等阶适配快照。</summary>
    private readonly bool _originalLowArAdaptation;
    /// <summary>载入时的培养计划详细诊断快照。</summary>
    private readonly bool _originalDiagnostics;
    /// <summary>载入时的自定义培养目标快照；null 显示为空文本。</summary>
    private readonly string _originalTrainingTargetsJson;

    // 策略目录与当前值显示由统一的 CombatStrategyPicker 负责。

    /// <summary>进入秘境切换的队伍名称，留空不切换。</summary>
    [ObservableProperty] private string _partyName;
    /// <summary>刷取的秘境名称；跟随培养计划时为提升指南选项值。</summary>
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

    /// <summary>是否自动计算今日刷取次数；关闭时只刷提升指南中的第一个秘境。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTrainingGuidePlanning))]
    private bool _trainingGuideCalculateRunsEnabled;
    /// <summary>刷取偏好：0 适量刷取，1 充分刷取。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTrainingGuideReservePercentEditable))]
    private int _trainingGuideRunPreference;
    /// <summary>合成收益预留率（%），范围 0～20。</summary>
    [ObservableProperty] private int _trainingGuideCraftingBonusReservePercent;
    /// <summary>是否按领奖结果更新养成库存。</summary>
    [ObservableProperty] private bool _trainingGuideRewardRecognitionEnabled;
    /// <summary>奖励识别失败时是否按固定预算继续。</summary>
    [ObservableProperty] private bool _trainingGuideRewardFailureBudgetEnabled;
    /// <summary>养成需求完成或无可刷秘境时刷取的备选秘境；空为不刷。</summary>
    [ObservableProperty] private string _trainingGuideFallbackDomainName;
    /// <summary>备选秘境的周日或限时奖励序号。</summary>
    [ObservableProperty] private string _trainingGuideFallbackSundayValue;
    /// <summary>是否启用低冒险等阶适配。</summary>
    [ObservableProperty] private bool _trainingGuideLowArAdaptationEnabled;
    /// <summary>是否保存培养计划详细诊断。</summary>
    [ObservableProperty] private bool _trainingGuideDiagnosticsEnabled;
    /// <summary>自定义培养目标 JSON；留空表示使用游戏内养成计划的需求。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTrainingGuidePlanning))]
    private string _trainingTargetsJson;

    /// <summary>载入时的树脂设置副本，格式错误时以能力默认值作为比较基准。</summary>
    private readonly ResinUsage _originalResin;
    /// <summary>只编辑当前参数草稿的树脂入口。</summary>
    public ResinUsageEntryViewModel Resin { get; }

    /// <summary>秘境来源：跟随培养计划或指定秘境，读写 <see cref="DomainName"/>。</summary>
    public DomainSourceViewModel DomainSource { get; }

    /// <summary>
    /// 是否按培养计划规划次数：开启自动计算，或填写了自定义培养目标（与任务的规划条件一致）。
    /// 刷取偏好、合成预留、库存更新、备选秘境与诊断只在规划时生效。
    /// </summary>
    public bool IsTrainingGuidePlanning => TrainingGuideCalculateRunsEnabled || !string.IsNullOrWhiteSpace(TrainingTargetsJson);

    /// <summary>合成收益预留率是否可编辑：充分刷取时任务固定按 0% 计算。</summary>
    public bool IsTrainingGuideReservePercentEditable => TrainingGuideRunPreference == 0;

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

        // 培养计划设置：缺失时按独立任务页的默认值显示，取值范围与任务参数一致
        _originalCalculateRuns = ReadBool(Original, TrainingGuideCalculateRunsKey, true);
        _originalRunPreference = ReadInt(Original, TrainingGuideRunPreferenceKey) == 1 ? 1 : 0;
        _originalReservePercent = Math.Clamp(ReadInt(Original, TrainingGuideReservePercentKey, 7), 0, 20);
        _originalGuideRewardRecognition = ReadBool(Original, TrainingGuideRewardRecognitionKey, true);
        _originalRewardFailureBudget = ReadBool(Original, TrainingGuideRewardFailureBudgetKey);
        _originalFallbackDomainName = ReadString(Original, TrainingGuideFallbackDomainKey);
        _originalFallbackSundayValue = ReadString(Original, TrainingGuideFallbackSundayKey);
        _originalLowArAdaptation = ReadBool(Original, TrainingGuideLowArKey);
        _originalDiagnostics = ReadBool(Original, TrainingGuideDiagnosticsKey);
        _originalTrainingTargetsJson = ReadString(Original, TrainingTargetsJsonKey);
        _trainingGuideCalculateRunsEnabled = _originalCalculateRuns;
        _trainingGuideRunPreference = _originalRunPreference;
        _trainingGuideCraftingBonusReservePercent = _originalReservePercent;
        _trainingGuideRewardRecognitionEnabled = _originalGuideRewardRecognition;
        _trainingGuideRewardFailureBudgetEnabled = _originalRewardFailureBudget;
        _trainingGuideFallbackDomainName = _originalFallbackDomainName;
        _trainingGuideFallbackSundayValue = _originalFallbackSundayValue;
        _trainingGuideLowArAdaptationEnabled = _originalLowArAdaptation;
        _trainingGuideDiagnosticsEnabled = _originalDiagnostics;
        _trainingTargetsJson = _originalTrainingTargetsJson;

        // 秘境来源只拆分界面选项，跟随培养计划仍保存为 domain_name 的提升指南选项值
        DomainSource = new DomainSourceViewModel(() => DomainName, value => DomainName = value);
    }

    /// <summary>秘境名变化（选择、清空或切换来源）后刷新秘境来源。</summary>
    /// <param name="value">新的秘境名。</param>
    partial void OnDomainNameChanged(string value) => DomainSource.Refresh();

    /// <summary>收集全部卡片输入的真实变化。</summary>
    /// <param name="changes">接收变化的对象。</param>
    /// <exception cref="FormatException">自定义培养目标不是有效的目标列表。</exception>
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

        // 培养计划设置
        WriteBool(changes, TrainingGuideCalculateRunsKey, _originalCalculateRuns, TrainingGuideCalculateRunsEnabled);
        WriteInt(changes, TrainingGuideRunPreferenceKey, _originalRunPreference, TrainingGuideRunPreference == 1 ? 1 : 0);
        WriteInt(changes, TrainingGuideReservePercentKey, _originalReservePercent, Math.Clamp(TrainingGuideCraftingBonusReservePercent, 0, 20));
        WriteBool(changes, TrainingGuideRewardRecognitionKey, _originalGuideRewardRecognition, TrainingGuideRewardRecognitionEnabled);
        WriteBool(changes, TrainingGuideRewardFailureBudgetKey, _originalRewardFailureBudget, TrainingGuideRewardFailureBudgetEnabled);
        WriteString(changes, TrainingGuideFallbackDomainKey, _originalFallbackDomainName, TrainingGuideFallbackDomainName);
        WriteString(changes, TrainingGuideFallbackSundayKey, _originalFallbackSundayValue, TrainingGuideFallbackSundayValue);
        WriteBool(changes, TrainingGuideLowArKey, _originalLowArAdaptation, TrainingGuideLowArAdaptationEnabled);
        WriteBool(changes, TrainingGuideDiagnosticsKey, _originalDiagnostics, TrainingGuideDiagnosticsEnabled);
        CollectTrainingTargets(changes);
    }

    /// <summary>
    /// 自定义培养目标变化时写入：留空保存为 null（使用游戏内养成计划），否则按任务的解析规则校验后保存。
    /// </summary>
    /// <param name="changes">接收变化的对象。</param>
    /// <exception cref="FormatException">目标列表格式错误。</exception>
    private void CollectTrainingTargets(JObject changes)
    {
        var json = (TrainingTargetsJson ?? string.Empty).Trim();
        if (string.Equals(json, _originalTrainingTargetsJson.Trim(), StringComparison.Ordinal)) return;
        if (json.Length == 0)
        {
            changes[TrainingTargetsJsonKey] = JValue.CreateNull();
            return;
        }

        try
        {
            TrainingGuideCustomTargets.Parse(json);
        }
        catch (Exception e) when (e is ArgumentException or Newtonsoft.Json.JsonException)
        {
            throw new FormatException($"自定义培养目标无效：{e.Message}", e);
        }

        changes[TrainingTargetsJsonKey] = json;
    }
}
