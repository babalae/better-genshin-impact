using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Core.Mask;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.User32;
using Microsoft.Extensions.Localization;
using System.Globalization;
using System.Text.RegularExpressions;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using System.Collections.ObjectModel;
using BetterGenshinImpact.Core.Script.Dependence;
using BetterGenshinImpact.GameTask.AutoDomain.Model;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Reward;
using Compunet.YoloSharp;
using Microsoft.Extensions.DependencyInjection;
using BetterGenshinImpact.GameTask.AutoCombo;
using BetterGenshinImpact.GameTask.AutoCombo.ComboBuild;
using BetterGenshinImpact.GameTask.AutoCombo.ComboRun;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Service;
using BetterGenshinImpact.View.Pages.SoloTaskSettings;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;
using Newtonsoft.Json;

namespace BetterGenshinImpact.GameTask.AutoDomain;

/// <summary>
/// 自动秘境独立任务：传送并进入秘境，循环执行战斗、寻找石化古树、领取奖励，结束后可选分解圣遗物。
/// 业务设置全部来自 <see cref="AutoDomainParam"/>，结果附带本次识别到的奖励汇总。
/// </summary>
[SoloTaskInfo(TaskId, "自动秘境", Group = "树脂与战斗", Order = 210)]
[SoloTaskSettingsView(typeof(AutoDomainSettingsView), typeof(AutoDomainSettingsViewModel))]
public partial class AutoDomainTask : ISoloTask<AutoDomainParam, ResinClaimData>
{
    /// <summary>本任务支持的树脂使用方式、种类、档位及默认设置。</summary>
    public static ResinUsageCapability ResinCapability => ResinCapabilities.Domain;

    /// <summary>
    /// 任务稳定标识。
    /// </summary>
    public const string TaskId = "auto_domain";

    /// <summary>
    /// 原因码：领取奖励后长时间未检测到秘境结束（通常是背包物品已满）。
    /// </summary>
    public const string DomainEndNotDetectedReason = "domain_end_not_detected";

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根，由创建方传入。只用于读取非本任务的全局设置（按键、拾取键、连招构建等），秘境业务设置来自 <see cref="_taskParam"/>
    /// </summary>
    private readonly AllConfig _allConfig;

    /// <summary>
    /// 配置根。静态方法没有创建方可以传参，暂时读配置根（见 task-context-removal.md 第 5 节）
    /// </summary>
    private static AllConfig Config => ConfigService.Config!;

    /// <summary>
    /// 本次运行的任务参数，在 <see cref="RunAsync"/> 开头赋值
    /// </summary>
    private AutoDomainParam _taskParam = null!;

    private BgiYoloPredictor _predictor = null!;

    /// <summary>
    /// TXT 战斗策略解析结果；使用 JSON、键鼠或自动连招策略时为 null
    /// </summary>
    private CombatScriptBag? _combatScriptBag;

    /// <summary>
    /// 由统一入口执行的 JSON/键鼠策略路径；文本策略时为 null
    /// </summary>
    private string? _entryCombatStrategyPath;

    /// <summary>策略为自动连招（LLM 行为树）时为 true：进本前调用 LLM 建树，循环战斗中 Tick 该树</summary>
    private bool _useComboStrategy;

    /// <summary>建树任务：未配置 PartyName 时进本前启动、与传送进本并行；配置了 PartyName 时为 null，进本后切队再启动</summary>
    private Task<ComboTreeSession>? _comboBuildTask;

    /// <summary>后台建树的取消源：链接主令牌，秘境流程结束时取消，避免宿主异常退出后建树白跑</summary>
    private CancellationTokenSource? _comboBuildCts;
    private readonly Dictionary<string, int> _rewardSummary = new();

    /// <summary>材料目标是否已全部达成。</summary>
    private bool _itemStopReached;

    private CancellationToken _ct;

    private ObservableCollection<OneDragonFlowConfig> ConfigList = [];

    private readonly string challengeCompletedLocalizedString;
    private readonly string autoLeavingLocalizedString;
    private readonly string skipLocalizedString;
    private readonly string leyLineDisorderLocalizedString;
    private readonly string clickanywheretocloseLocalizedString;
    private readonly string matchingChallengeString;
    private readonly string rapidformationString;
    private readonly string limitedFullyString;
    private readonly string limitedFullyAllString;
    private readonly string singlePlayerChallengeString;
    private readonly string startChallengeString;
    private readonly string retryDomainPromptPattern;
    private readonly string petrifiedTreeString;
    private readonly string insufficientCountString;
    private readonly string replenishResinString;
    private readonly string cancelButtonString;

    /// <summary>本次运行的树脂候选、领奖次数与消耗记录。</summary>
    private ResinClaimSelector _selector = null!;
    /// <summary>最近一次树脂判断的结束原因。</summary>
    private ResinStopReason _stopReason;
    /// <summary>是否已开始挑战，用于区分开始前不足与战斗后的提前结束。</summary>
    private bool _hasBattled;

    /// <summary>创建秘境任务。业务参数在 <see cref="RunAsync"/> 中传入，这里只做与参数无关的本地化初始化。</summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    public AutoDomainTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _allConfig = config;

        IStringLocalizer<AutoDomainTask> stringLocalizer =
            App.GetService<IStringLocalizer<AutoDomainTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(config.OtherConfig.GameCultureInfoName);
        this.challengeCompletedLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "挑战达成");
        this.autoLeavingLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "自动退出");
        this.skipLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "跳过");
        this.leyLineDisorderLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "地脉异常");
        this.clickanywheretocloseLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "点击任意位置关闭");
        this.matchingChallengeString = stringLocalizer.WithCultureGet(cultureInfo, "匹配挑战");
        this.rapidformationString = stringLocalizer.WithCultureGet(cultureInfo, "快速编队");
        this.limitedFullyString = stringLocalizer.WithCultureGet(cultureInfo, "限时全部开放");
        this.limitedFullyAllString = stringLocalizer.WithCultureGet(cultureInfo, "限时开放");
        this.singlePlayerChallengeString = stringLocalizer.WithCultureGet(cultureInfo, "单人挑战");
        this.startChallengeString = stringLocalizer.WithCultureGet(cultureInfo, "开始挑战");
        this.retryDomainPromptPattern = stringLocalizer.WithCultureGet(cultureInfo, "是否仍要.*挑战.*秘境");
        this.petrifiedTreeString = stringLocalizer.WithCultureGet(cultureInfo, "石化古树");
        this.insufficientCountString = stringLocalizer.WithCultureGet(cultureInfo, "数量不足");
        this.replenishResinString = stringLocalizer.WithCultureGet(cultureInfo, "补充原粹树脂");
        this.cancelButtonString = stringLocalizer.WithCultureGet(cultureInfo, "取消");
    }

    private RecognitionObject GetConfirmRa(params string[] targetText)
    {
        using var screenArea = _runtime.CaptureToRectArea();
        var x = (int)(screenArea.Width * 0.5);
        var y = (int)(screenArea.Height * 0.5);
        var width = (int)(screenArea.Width * 0.5);
        var height = (int)(screenArea.Height * 0.5);
        return RecognitionObject.OcrMatch(x, y, width, height, targetText);
    }

    /// <summary>
    /// 按 1080P 坐标移动鼠标并左键单击。与原 GlobalMethod.Click 在默认 1920x1080 脚本尺寸下的行为一致，
    /// 但直接使用本运行环境换算坐标，不依赖脚本宿主 GlobalMethod 的静态分辨率设置
    /// </summary>
    /// <param name="x">1080P 下的横坐标</param>
    /// <param name="y">1080P 下的纵坐标</param>
    private void Click1080P(double x, double y)
    {
        _runtime.GameRegion1080PPosMove(x, y);
        InputHub.Foreground.Mouse.LeftButtonDown().Sleep(60).LeftButtonUp();
    }

    /// <summary>
    /// 按本次参数完成依赖参数的初始化：解析战斗策略、校验秘境；树脂设置和进度在任务入口统一初始化。
    /// </summary>
    /// <param name="param">本次运行的任务参数</param>
    /// <returns>参数不满足业务要求时返回失败结果；初始化成功返回 null</returns>
    private SoloTaskResult? Initialize(AutoDomainParam param)
    {
        _useComboStrategy = false;
        _entryCombatStrategyPath = null;
        _combatScriptBag = null;
        _comboBuildTask = null;
        _guideDomainName = null;

        // 1. 解析战斗策略路径：JS、旧入口传入的是已存在的路径；任务计划保存的可能是策略名，需要先解析
        var strategy = CombatStrategyResolver.Parse(param.CombatStrategyPath);
        if (!strategy.Exists)
            return SoloTaskResult.Failed(SoloTaskReasons.StrategyNotFound, $"战斗策略不存在：{param.CombatStrategyPath}");
        var strategyPath = strategy.Path;

        // 2. 选择了具体秘境时必须能找到传送点。未选择秘境（站在秘境门口直接开始）与“根据提升指南选择秘境”保持原有流程
        if (!string.IsNullOrEmpty(param.DomainName)
            && param.DomainName != TrainingGuideOption
            && !MapLazyAssets.Get().DomainPositionMap.ContainsKey(param.DomainName))
        {
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, $"未找到对应的秘境{param.DomainName}的传送点。");
        }

        // 2.5 解析本次运行的自定义培养目标；未传入时为 null，沿用游戏内培养计划
        try
        {
            _guideCustomTargets = TrainingGuideCustomTargets.Parse(param.TrainingTargetsJson);
        }
        catch (Exception e) when (e is ArgumentException or JsonException)
        {
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, $"自定义培养目标无效：{e.Message}");
        }

        // 3. 树脂设置已在入口统一校验，此处只选择战斗引擎。
        // 4. 按策略类型选择战斗引擎：自动连招 / 统一入口（JSON、键鼠）/ TXT
        if (strategy.Kind == CombatStrategyKind.Combo)
        {
            _useComboStrategy = true;
            Logger.LogInformation("自动秘境：检测到自动连招策略，将使用LLM行为树");
        }
        else if (strategy.Kind is not (CombatStrategyKind.Txt or CombatStrategyKind.AutoSelect))
        {
            _entryCombatStrategyPath = strategyPath;
            Logger.LogInformation("自动秘境：使用统一战斗入口执行所选策略");
        }
        else
        {
            _combatScriptBag = CombatScriptParser.ReadAndParse(strategyPath);
        }

        return null;
    }

    /// <summary>
    /// 运行自动秘境。参数无效或策略不存在时返回失败结果；秘境流程正常结束返回未确认结果并附带奖励汇总；取消时抛出 OperationCanceledException。
    /// </summary>
    /// <param name="param">本次运行的任务参数</param>
    /// <param name="ct">本次运行的取消令牌</param>
    /// <returns>任务结果，数据为本次识别到的奖励汇总</returns>
    public async Task<SoloTaskResult<ResinClaimData>> RunAsync(AutoDomainParam param, CancellationToken ct)
    {
        param.ResolveResinUsage();
        if (param.ResinUsage.Validate(ResinCapability) is { } problem)
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, problem);
        _selector = new ResinClaimSelector(param.ResinUsage, ResinCapability);
        _stopReason = ResinStopReason.NoUsableOption;
        _hasBattled = false;
        _taskParam = param;
        _ct = ct;
        _rewardSummary.Clear();
        _itemStopReached = false;

        // 依赖参数的初始化（原构造函数中的策略解析、树脂记录等）
        var initFailure = Initialize(param);
        if (initFailure != null)
        {
            Logger.LogError("自动秘境：{Msg}", initFailure.Message);
            return initFailure;
        }

        using var predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiTree);
        _predictor = predictor;

        _guidePlanning = _guideCustomTargets != null ||
            (_taskParam.DomainName == TrainingGuideOption && _taskParam.TrainingGuideCalculateRunsEnabled);
        using var diagnostics = TrainingGuideDiagnostics.Begin(_guidePlanning && _taskParam.TrainingGuideDiagnosticsEnabled, Logger);
        if (TrainingGuideDiagnostics.Enabled)
        {
            Logger.LogInformation("培养计划详细判断保存至 {Path}，按日期统一记录", TrainingGuideDiagnostics.DirectoryPath);
            TrainingGuideDiagnostics.Detail("培养任务开始：{StartedAt:O}", DateTime.Now);
        }
        ResetTrainingGuideState();

        Init();
        var targetFailure = ValidateTargetItemSettings();
        if (targetFailure != null)
        {
            Logger.LogError("自动秘境：{Msg}", targetFailure.Message);
            return targetFailure;
        }

        // 培养计划奖励识别需要 1920×1080；不支持且未开启预算兜底时按参数无效结束
        try
        {
            CheckGuideRewardResolution();
        }
        catch (InvalidOperationException e)
        {
            Logger.LogError("自动秘境：{Msg}", e.Message);
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, e.Message);
        }

        if (_taskParam.TargetItems.Count > 0)
        {
            Logger.LogInformation("自动秘境：材料目标 {Targets}，达到后提前结束",
                string.Join("，", _taskParam.TargetItems.Select(x => $"{x.ItemName}×{x.TargetCount}")));
        }

        // 自动吃药只在本次秘境期间启用，返回（含异常）时撤销
        using var autoEatLease = _taskParam.AutoEat ? TaskTriggerDispatcher.Instance().AddTrigger("AutoEat") : null;

        Notify.Event(NotificationEvent.DomainStart).Success("自动秘境启动");

        // 自动连招：秘境外识别队伍后启动 LLM 后台建树，
        // 建树与传送进本并行，战斗启动前在 StartComboFight 中等待其完成；
        // 建树令牌链接主令牌，秘境流程结束（含异常退出）时在 finally 中取消
        if (_useComboStrategy)
        {
            if (string.IsNullOrEmpty(_taskParam.PartyName))
            {
                var avatars = await AutoComboBuildTask.EnsureMainUiAndRecognizeTeamAsync(_runtime, _allConfig, Logger, ct);
                Logger.LogInformation("自动秘境：识别队伍：{Avatars}，后台启动 LLM 建树", string.Join("、", avatars.Select(a => a.Name)));

                var config = _allConfig.AutoComboBuildConfig;
                _comboBuildCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _comboBuildTask = AutoComboBuildTask.BuildComboTreeAsync(avatars, config, Logger, _comboBuildCts.Token);
            }
        }

        try
        {
            // 复活重试
            for (var i = 0; i < _taskParam.ReviveRetryCount; i++)
            {
                try
                {
                    if (_guidePlanning) await RunTrainingGuidePlan();
                    else await DoDomain();
                    // 其他场景不重试
                    break;
                }
                catch (RetryException e)
                {
                    // 只有选择了秘境的时候才会重试
                    if (!string.IsNullOrEmpty(_taskParam.DomainName))
                    {
                        var msg = e.Message;
                        if (msg.Contains("复活"))
                        {
                            msg = "存在角色死亡，复活后重试秘境...";
                        }

                        Logger.LogWarning("自动秘境：{Text}", msg);
                        await _runtime.Delay(2000, ct);
                        Notify.Event(NotificationEvent.DomainRetry).Error(msg);
                        continue;
                    }

                    throw;
                }
            }
            await _runtime.Delay(2000, ct);
            await Bv.WaitForMainUi(_runtime, _ct, 30);
            await _runtime.Delay(2000, ct);

            await ArtifactSalvage();
            Notify.Event(NotificationEvent.DomainEnd).Success("自动秘境结束");
            // 按树脂设置刷取完成或材料目标达成返回成功；提前结束按未确认结束返回，并附带本次识别到的奖励汇总
            return ResinClaimResults.Complete(_selector, _stopReason, "秘境流程已结束", _hasBattled,
                RewardSummary.From(_rewardSummary), _itemStopReached);
        }
        catch (SoloTaskAbortException e) when (e.Result.ReasonCode == SoloTaskReasons.ResinExhausted)
        {
            return ResinClaimResults.Complete(_selector, ResinStopReason.ResinExhausted,
                "秘境流程已结束", _hasBattled, RewardSummary.From(_rewardSummary));
        }
        finally
        {
            // 正常结束时建树任务早已完成（已在按 F 前 await），取消是空操作；异常退出时立即掐断后台 LLM 请求
            if (_comboBuildCts != null)
            {
                await _comboBuildCts.CancelAsync();
                _comboBuildCts.Dispose();
                _comboBuildCts = null;
            }
        }
    }

    private async Task DoDomain()
    {
        // 传送到秘境
        await TpDomain();
        // 切换队伍
        // await SwitchParty(_taskParam.PartyName);

        // 前置进入秘境
        await EnterDomain();

        var combatScenes = new CombatScenes(_runtime, _allConfig);
        for (var i = 0; i < _taskParam.DomainRoundNum && !_selector.IsTargetReached; i++)
        {
            _hasBattled = true;
            // 0. 关闭秘境提示
            Logger.LogDebug("0. 关闭秘境提示");
            await CloseDomainTip();

            if (_useComboStrategy)
            {
                ESkillCdTracker.Clear();
                // 自动连招策略：战斗引擎内部初始化队伍，无需TXTSpecific步骤

                // PartyName 非空：切队发生在 EnterDomain 的秘境挑战页，
                // 此时再根据切换后的实际战斗队伍进行构建
                if (_comboBuildTask == null)
                {
                    var avatars = await AutoComboBuildTask.EnsureMainUiAndRecognizeTeamAsync(_runtime, _allConfig, Logger, _ct);
                    Logger.LogInformation("自动秘境：识别队伍：{Avatars}，启动 LLM 建树", string.Join("、", avatars.Select(a => a.Name)));

                    var config = _allConfig.AutoComboBuildConfig;
                    _comboBuildCts = CancellationTokenSource.CreateLinkedTokenSource(_ct);
                    _comboBuildTask = AutoComboBuildTask.BuildComboTreeAsync(avatars, config, Logger, _comboBuildCts!.Token);
                }

                if (!_comboBuildTask.IsCompleted)
                {
                    Logger.LogInformation("自动秘境：{Text}", "0. 等待 LLM 建树完成");
                }
                await _comboBuildTask; // 建树失败在此抛出快速结束任务，结果由 StartComboFight 内再次 await 获取

                // 1. 走到钥匙处启动
                Logger.LogInformation("自动秘境：{Text}", "1. 走到钥匙处启动");
                await WalkToPressF();

                // 2. 执行战斗（LLM行为树）
                Logger.LogInformation("自动秘境：{Text}", "2. 执行战斗策略(自动连招)");
                await StartComboFight();
            }
            else if (_entryCombatStrategyPath != null)
            {
                ESkillCdTracker.Clear();
                // JSON/键鼠策略：由统一入口初始化，无需文本战斗的队伍准备步骤
                // 1. 走到钥匙处启动
                Logger.LogInformation("自动秘境：{Text}", "1. 走到钥匙处启动");
                await WalkToPressF();

                // 2. 执行战斗（统一入口：JSON/键鼠）
                Logger.LogInformation("自动秘境：{Text}", "2. 执行战斗策略(JSON/键鼠)");
                await StartEntryFight();
            }
            else
            {
                //0.5. 初始化队伍，只执行一次
                if (i == 0)
                {
                    combatScenes = new CombatScenes(_runtime, _allConfig).InitializeTeam(_runtime.CaptureToRectArea());
                }

                RetryTeamInit(combatScenes);

                // 0. 切换到第一个角色
                var combatCommands = FindCombatScriptAndSwitchAvatar(combatScenes);

                // 1. 走到钥匙处启动
                Logger.LogInformation("自动秘境：{Text}", "1. 走到钥匙处启动");
                await WalkToPressF();

                // 2. 执行战斗（战斗线程、视角线程、检测战斗完成线程）
                Logger.LogInformation("自动秘境：{Text}", "2. 执行战斗策略");
                await StartFight(combatScenes, combatCommands);
                combatScenes.AfterTask();
            }
            EndFightWait();

            // 3. 寻找石化古树 并左右移动直到石化古树位于屏幕中心
            Logger.LogInformation("自动秘境：{Text}", "3. 寻找石化古树");
            await FindPetrifiedTree();

            // 4. 走到石化古树处
            Logger.LogInformation("自动秘境：{Text}", "4. 走到石化古树处");
            await WalkToPressF();

            // 5. 快速领取奖励并判断是否有下一轮
            Logger.LogInformation("自动秘境：{Text}", "5. 领取奖励");
            if (!await GettingTreasure())
            {
                if (_guidePlanning && _guideNextAction == GuideNextAction.AdvancePlan)
                    Logger.LogInformation("培养计划：当前入口执行结束，退出后按缓存计划选择下一步");
                else
                    Logger.LogInformation("体力耗尽或者设置轮次已达标，结束自动秘境");
                // 结束原因已在领奖时记录，直接返回，避免被下方的轮数上限覆盖
                return;
            }
        }
        // JS 额外轮数上限仍可独立结束任务。
        _stopReason = ResinStopReason.TargetReached;
    }

    private void Init()
    {
        LogScreenResolution();

        // 材料目标在 RunAsync 中单独输出；树脂设置与材料目标同时生效，任一达成即结束。
        Logger.LogInformation("自动秘境：{ResinUsage}", ResinUsageText.Summary(_taskParam.ResinUsage, ResinCapability));
    }

    /// <summary>
    /// 材料目标模式启动前校验目标配置与奖励识别能力。
    /// </summary>
    /// <returns>目标配置或奖励识别能力无效时的失败结果；有效时返回 null。</returns>
    private SoloTaskResult? ValidateTargetItemSettings()
    {
        if (_taskParam.TargetItems.Count == 0)
        {
            return null;
        }

        try
        {
            TargetItemCatalog.Instance.ValidateDomainTargets(_taskParam.DomainName, _taskParam.TargetItems);
            RewardResultRecognizer.Instance.EnsureSupported(_runtime);
            return null;
        }
        catch (Exception e) when (e is TargetItemConfigurationException or InvalidOperationException)
        {
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, e.Message);
        }
    }

    private void LogScreenResolution()
    {
        var gameScreenSize = SystemControl.GetGameScreenRect(_runtime.Window.Handle);
        if (gameScreenSize.Width * 9 != gameScreenSize.Height * 16)
        {
            Logger.LogError("游戏窗口分辨率不是 16:9 ！当前分辨率为 {Width}x{Height} , 非 16:9 分辨率的游戏无法正常使用自动秘境功能 !",
                gameScreenSize.Width, gameScreenSize.Height);
            throw new Exception("游戏窗口分辨率不是 16:9");
        }

        if (gameScreenSize.Width < 1920 || gameScreenSize.Height < 1080)
        {
            Logger.LogWarning("游戏窗口分辨率小于 1920x1080 ！当前分辨率为 {Width}x{Height} , 小于 1920x1080 的分辨率的游戏可能无法正常使用自动秘境功能 !",
                gameScreenSize.Width, gameScreenSize.Height);
        }
    }

    private void RetryTeamInit(CombatScenes combatScenes)
    {
        if (!combatScenes.CheckTeamInitialized())
        {
            combatScenes.InitializeTeam(_runtime.CaptureToRectArea());
            if (!combatScenes.CheckTeamInitialized())
            {
                throw new Exception("识别队伍角色失败，请在较暗背景下重试，比如游戏时间调整成夜晚。或者直接使用强制指定当前队伍角色的功能。");
            }
        }
    }

    private async Task TpDomain()
    {
        // 初次进入及切换目标共用传送路径，不复用上一轮画面坐标。
        _guideEntryPositions.Clear();
        if (_taskParam.DomainName == TrainingGuideOption && !_guidePlanning)
        {
            _guideDomainName = null;
            _guideDomainName = (await ScanGuideDomains(firstOnly: true)).FirstOrDefault()
                ?? throw new InvalidOperationException("提升指南：没有可用的材料秘境，停止任务");
        }
        // 传送到秘境
        if (!string.IsNullOrEmpty(_guideDomainName ?? _taskParam.DomainName))
        {
            if (MapLazyAssets.Get().DomainPositionMap.TryGetValue(_guideDomainName ?? _taskParam.DomainName, out var domainPosition))
            {
                Logger.LogInformation("自动秘境：传送到秘境{Text}", _guideDomainName ?? _taskParam.DomainName);
                await new TpTask(_runtime, _allConfig, _ct).Tp(domainPosition.X, domainPosition.Y);
                await _runtime.Delay(1000, _ct);
                if (!await Bv.WaitForMainUi(_runtime, _ct) && _guidePlanning)
                    throw new InvalidOperationException("传送后未确认回到主界面，停止接近秘境入口");

                await ApproachDomainEntrance(_guideDomainName ?? _taskParam.DomainName);
                await _runtime.Delay(300, _ct);

            }
            else
            {
                Logger.LogError("自动秘境：未找到对应的秘境{Text}的传送点", _taskParam.DomainName);
                throw new Exception($"未找到对应的秘境{_taskParam.DomainName}的传送点");
            }
        }
    }

    /// <summary>
    /// 接近秘境入口；已有交互提示时无需移动，普通任务和培养计划共用此逻辑。
    /// </summary>
    /// <param name="domainName">本次传送的秘境名称，用于选择接近方向。</param>
    private async Task ApproachDomainEntrance(string domainName)
    {
        var menuFound = false;
        AutoPickAssets pickAssets;
        using (var gameCaptureRegion = _runtime.CaptureToRectArea())
        {
            pickAssets = AutoPickAssets.Get(gameCaptureRegion, _allConfig.AutoPickConfig.PickKey);
            using var interaction = gameCaptureRegion.Find(pickAssets.PickRo);
            if (interaction.IsExist()) return;
        }
        try
        {
            if ("芬德尼尔之顶".Equals(domainName))
            {
                menuFound = await NewRetry.WaitForElementAppear(_runtime,
                    pickAssets.PickRo,
                    () => InputHub.Foreground.SimulateAction(GIActions.MoveBackward, KeyType.KeyDown),
                    _ct,
                    20,
                    500
                );
                InputHub.Foreground.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
            }
            else if ("无妄引咎密宫".Equals(domainName))
            {
                InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                await _runtime.Delay(500, _ct);
                InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);

                menuFound = await NewRetry.WaitForElementAppear(_runtime,
                    pickAssets.PickRo,
                    () => InputHub.Foreground.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown),
                    _ct,
                    20,
                    500
                );
                InputHub.Foreground.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
            }
            else if ("太山府".Equals(domainName))
            {
                menuFound = await NewRetry.WaitForElementAppear(_runtime,
                    pickAssets.PickRo,
                    () => { },
                    _ct,
                    20,
                    500
                );
            }
            else
            {
                menuFound = await NewRetry.WaitForElementAppear(_runtime,
                    pickAssets.PickRo,
                    () => InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown),
                    _ct,
                    20,
                    500
                );
                InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            }

        }
        finally
        {
            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            InputHub.Foreground.SimulateAction(GIActions.MoveBackward, KeyType.KeyUp);
            InputHub.Foreground.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
        }

        if (!menuFound)
        {
            throw new Exception("请检查是否在秘境门前");
        }
    }

    /// <summary>
    /// 切换队伍
    /// </summary>
    /// <param name="partyName"></param>
    /// <returns></returns>
    private async Task<bool> SwitchParty(string? partyName)
    {
        if (!string.IsNullOrEmpty(partyName))
        {
            var b = await new SwitchPartyTask(_runtime).Start(partyName, _ct);
            await _runtime.Delay(500, _ct);
            return b;
        }

        return true;
    }

    private async Task EnterDomain()
    {
        AutoFightAssets fightAssets;
        AutoPickAssets pickAssets;
        using (var gameCaptureRegion = _runtime.CaptureToRectArea())
        {
            fightAssets = AutoFightAssets.Get(gameCaptureRegion);
            pickAssets = AutoPickAssets.Get(gameCaptureRegion, _allConfig.AutoPickConfig.PickKey);
        }

        var domainName = _guideDomainName ?? _taskParam.DomainName;
        if (!string.IsNullOrEmpty(domainName)
            && MapLazyAssets.Get().DomainPositionMap.TryGetValue(domainName, out var domainPosition)
            && "至冬".Equals(domainPosition.Country, StringComparison.Ordinal))
        {
            var domainOptionName = domainPosition.Name ?? domainName;
            Logger.LogInformation("自动秘境：至冬秘境使用文本OCR选择交互项 {Text}", domainOptionName);
            if (!await new ChooseFOptionTask(_runtime, _allConfig).SingleSelectText(domainOptionName, _ct))
            {
                Logger.LogWarning("未能通过文本OCR选择秘境，直接F");
                await NewRetry.WaitForElementDisappear(_runtime,
                    pickAssets.PickRo,
                    () => InputHub.Foreground.Keyboard.KeyPress(pickAssets.PickVk),
                    _ct,
                    20,
                    500
                );
            }

            // 交互输入生效和截图源刷新都存在延迟，等待交互键消失后再识别秘境菜单。
            await NewRetry.WaitForElementDisappear(_runtime,
                pickAssets.PickRo,
                (Action?)null,
                _ct,
                20,
                500
            );
        }
        else
        {
            await NewRetry.WaitForElementDisappear(_runtime,
                pickAssets.PickRo,
                () => InputHub.Foreground.Keyboard.KeyPress(pickAssets.PickVk),
                _ct,
                20,
                500
            );
        }
        // 培养指南只等待入口菜单，不在选择前检查挑战按钮或解锁状态。
        using var menuScreen = _runtime.CaptureToRectArea();
        var menuFound = await NewRetry.WaitForElementAppear(_runtime,
            (_guidePlanning || _taskParam.DomainName == TrainingGuideOption)
                ? RecognitionObject.OcrMatch(0, 0, menuScreen.Width * .47, menuScreen.Height * .12, "秘境入口")
                : GetConfirmRa(singlePlayerChallengeString),
            null,//只等待,不执行操作
            _ct,
            20,
            500
        );
        if (!menuFound)
        {
            if (_guidePlanning || _taskParam.DomainName == TrainingGuideOption)
                throw new InvalidOperationException("未确认秘境入口菜单，停止培养任务");
            Logger.LogWarning("单人挑战 按键未出现，请检查是否已进入秘境页面");
        }

        using var limitedFullyStringRa = _runtime.CaptureToRectArea();
        var limitedFullyStringRaocrList =
            limitedFullyStringRa.FindMulti(RecognitionObject.Ocr(0, 0, limitedFullyStringRa.Width * 0.5,
                limitedFullyStringRa.Height * 0.5));
        var limitedFullyStringRaocrListdone = limitedFullyStringRaocrList.LastOrDefault(t =>
            Regex.IsMatch(t.Text, this.limitedFullyString) || Regex.IsMatch(t.Text, this.limitedFullyAllString));
        // 检测是否为限时全开秘境
        if (limitedFullyStringRaocrListdone != null)
        {
            Logger.LogInformation("自动秘境：{Text}", "检测到秘境限时全开");
        }
        foreach (var region in limitedFullyStringRaocrList) region.Dispose();

        var serverTime = ServerTimeHelper.GetServerTimeNow();
        if (_guidePlanning || _taskParam.DomainName == TrainingGuideOption)
        {
            var allOpen = serverTime is { DayOfWeek: DayOfWeek.Sunday, Hour: >= 4 }
                || serverTime is { DayOfWeek: DayOfWeek.Monday, Hour: < 4 }
                || limitedFullyStringRaocrListdone != null;
            await SelectTrainingGuideLevel(allOpen);
        }
        else if (serverTime is { DayOfWeek: DayOfWeek.Sunday, Hour: >= 4 } || serverTime is { DayOfWeek: DayOfWeek.Monday, Hour: < 4 } || limitedFullyStringRaocrListdone != null)
        {
            using var ra0 = _runtime.CaptureToRectArea();
            using var artifactArea = ra0.Find(RecognitionAssets.Get("AutoFight", "ArtifactArea", ra0)); //检测是否为圣遗物副本
            if (artifactArea.IsEmpty())
            {
                if (int.TryParse(_taskParam.SundaySelectedValue, out int sundaySelectedValue))
                {
                    if (sundaySelectedValue > 0)
                    {
                        Logger.LogInformation(limitedFullyStringRaocrListdone != null ? "自动秘境：限时全开秘境奖励序号 {sundaySelectedValue}" : "自动秘境：周日设置了秘境奖励序号 {sundaySelectedValue}", sundaySelectedValue);
                        using var abnormalscreenRa = _runtime.CaptureToRectArea();
                        _runtime.GameRegion1080PPosMove(abnormalscreenRa.Width / 4, abnormalscreenRa.Height / 2); //移到左侧
                        for (var i = 0; i < 100; i++)
                        {
                            InputHub.Foreground.Mouse.VerticalScroll(-1);
                            await _runtime.Delay(10, _ct);
                        }

                        await _runtime.Delay(400, _ct);

                        using var abnormalRa = _runtime.CaptureToRectArea();
                        var ocrList =
                            abnormalRa.FindMulti(RecognitionObject.Ocr(0, 0, abnormalRa.Width * 0.5,
                                abnormalRa.Height));
                        var done = ocrList.LastOrDefault(t =>
                            Regex.IsMatch(t.Text, this.leyLineDisorderLocalizedString));
                        if (done != null)
                        {
                            await _runtime.Delay(300, _ct);

                            switch (sundaySelectedValue)
                            {
                                case 1:
                                    Click1080P(done.X, done.Y - abnormalRa.Height / 5);
                                    break;
                                case 2:
                                    Click1080P(done.X, done.Y - abnormalRa.Height / 10);
                                    break;
                                case 3:
                                    Click1080P(done.X, done.Y);
                                    break;
                                default:
                                    Logger.LogWarning("无效的 sundaySelectedValue 值: {sundaySelectedValue}",
                                        sundaySelectedValue);
                                    break;
                            }
                        }
                    }
                    else
                    {
                        Logger.LogInformation(limitedFullyStringRaocrListdone != null ? "自动秘境：限时全开秘境未设置特定秘境奖励" : "自动秘境：周日秘境未设置特定秘境奖励");
                    }
                }
                else
                {
                    Logger.LogWarning(_taskParam.SundaySelectedValue == "" ? "未设置秘境奖励序号" : "设置秘境奖励序号错误，请检查配置页面");
                }
            }

            await _runtime.Delay(300, _ct);
        }

        // 点击单人挑战确认并等待队伍界面--使用图像模版匹配的方法，也可以使用文字OCR的方法识别“单人挑战”直到消失
        await NewRetry.WaitForElementAppear(_runtime,
            ElementRecognition.Get("PartyBtnChooseView", _runtime.SystemInfo),
            () =>
            {
                using var ra = _runtime.CaptureToRectArea();
                var ra2 = ra.Find(RecognitionAssets.Get("AutoFight", "Confirm", ra));
                if (!ra2.IsEmpty())
                {
                    ra2.Click();
                    ra2.Dispose();
                    Logger.LogInformation("自动秘境：点击 {Text}", singlePlayerChallengeString);
                }

                using var confirmRectArea2 = ra.Find(RecognitionObject.Ocr(ra.Width * 0.263, ra.Height * 0.32,
                    ra.Width - ra.Width * 0.263 * 2, ra.Height - ra.Height * 0.32 - ra.Height * 0.353));
                if (confirmRectArea2.IsExist() && Regex.IsMatch(confirmRectArea2.Text, retryDomainPromptPattern))
                {
                    Logger.LogWarning("自动秘境：检测到树脂不足提示：{Text}", confirmRectArea2.Text);
                    throw new SoloTaskAbortException(SoloTaskResult.Skipped(SoloTaskReasons.ResinExhausted, ResinUsageText.Skipped));
                }
            },
            _ct,
            10,
            1000
        );

        // 等待队伍选择界面出现
        var teamUiFound = await NewRetry.WaitForElementAppear(_runtime,
            ElementRecognition.Get("PartyBtnChooseView", _runtime.SystemInfo),
            () => { Logger.LogInformation("自动秘境：进入 {Text}", "队伍选择界面"); },
            _ct,
            10,
            1000
        );
        if (!teamUiFound)
        {
            Logger.LogWarning("队伍选择界面未出现，跳过切换队伍。");
        }
        else
        {
            await SwitchParty(_taskParam.PartyName);
        }

        // 点击开始挑战确认并等待“开始挑战”文字消失
        var startFightFound = await NewRetry.WaitForElementDisappear(_runtime,
            GetConfirmRa(startChallengeString),
            screen =>
            {
                screen.Find(RecognitionAssets.Get("AutoFight", "Confirm", screen), ra =>
                {
                    ra.Click();
                    ra.Dispose();
                    Logger.LogInformation("自动秘境：点击 {Text}", startChallengeString);
                });
            },
            _ct,
            10,
            1000
        );
        if (!startFightFound)
        {
            Logger.LogWarning("开始挑战按钮未出现或未能点击。");
        }

        // 载入
        await _runtime.Delay(1000, _ct);
    }

    private async Task CloseDomainTip()
    {
        //先等待秘境提示出现,如果直接出现Enter也属于完成条件
        var domainTipFound = await NewRetry.WaitForAction(_runtime, () =>
        {
            using var ra = _runtime.CaptureToRectArea();

            var ocrList = ra.FindMulti(RecognitionObject.Ocr(0, ra.Height * 0.2, ra.Width, ra.Height * 0.6));
            var ocrListLeft = ra.Find(RecognitionAssets.Get("AutoFight", "AbnormalIcon", ra));
            return (ocrList.Any(t => t.Text.Contains(leyLineDisorderLocalizedString) ||
                                     t.Text.Contains(clickanywheretocloseLocalizedString))) || ocrListLeft.IsExist();
        }, _ct, 40, 500);
        if (!domainTipFound)
        {
            Logger.LogWarning("秘境提示未出现或未能点击。");
        }

        //持续点击，直到左下角出现目标文字
        var leftBottomFound = await NewRetry.WaitForAction(_runtime, () =>
        {
            using var ra = _runtime.CaptureToRectArea();
            var ocrList = ra.FindMulti(RecognitionObject.Ocr(0, ra.Height * 0.2, ra.Width, ra.Height * 0.6));
            // 查找目标文字
            var done = ocrList.FirstOrDefault(t =>
                Regex.IsMatch(t.Text, this.leyLineDisorderLocalizedString) ||
                Regex.IsMatch(t.Text, this.clickanywheretocloseLocalizedString));
            if (done != null)
            {
                done.Click();
                done.Dispose();
                Logger.LogInformation("自动秘境：点击 {Text}", done.Text);
            }

            // 检查左下角区域是否还存在目标文字，消失则继续，存在则结束
            using var leftBottom = _runtime.CaptureToRectArea();
            var leftBottomOcr = leftBottom.Find(RecognitionAssets.Get("AutoFight", "AbnormalIcon", leftBottom));
            return leftBottomOcr.IsExist();
        }, _ct, 20, 500);
        if (!leftBottomFound)
        {
            //尝试随意点击一下右下角
            _runtime.GameRegion1080PPosClick(1515, 892);
            Logger.LogWarning("秘境提示未出现或未能点击。");
        }

        await _runtime.Delay(500, _ct);
    }

    private List<CombatCommand> FindCombatScriptAndSwitchAvatar(CombatScenes combatScenes)
    {
        var combatCommands = _combatScriptBag.FindCombatScript(combatScenes.GetAvatars());
        var avatar = combatScenes.SelectAvatar(combatCommands[0].Name);
        avatar?.SwitchWithoutCts();
        _runtime.Sleep(200);
        return combatCommands;
    }

    /// <summary>
    /// 走到钥匙处启动
    /// </summary>
    private async Task WalkToPressF()
    {
        if (_ct.IsCancellationRequested)
        {
            return;
        }

        await Task.Run((Action)(() =>
        {
            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            _runtime.Sleep(30, _ct);
            // 组合键好像不能直接用 postmessage
            if (!_taskParam.WalkToF)
            {
                InputHub.Foreground.SimulateAction(GIActions.SprintKeyboard, KeyType.KeyDown);
            }

            try
            {
                var startTime = DateTime.Now;
                while (!_ct.IsCancellationRequested)
                {
                    using var gameCaptureRegion = _runtime.CaptureToRectArea();
                    var pickAssets = AutoPickAssets.Get(gameCaptureRegion, _allConfig.AutoPickConfig.PickKey);
                    using var fRectArea = gameCaptureRegion.Find(pickAssets.PickRo);
                    if (fRectArea.IsEmpty())
                    {
                        _runtime.Sleep(100, _ct);
                    }
                    else
                    {
                        Logger.LogInformation("检测到交互键");
                        InputHub.Foreground.Keyboard.KeyPress(pickAssets.PickVk);
                        break;
                    }

                    // 超时直接放弃整个秘境
                    if (DateTime.Now - startTime > TimeSpan.FromSeconds(60))
                    {
                        Logger.LogWarning("自动秘境：{Text}", "前往目标位置处超时，如果选择了秘境名称，将在传送后重试秘境！");
                        Avatar.TpForRecover(_runtime, _allConfig, _ct, new RetryException("前往目标位置处超时，先传送到七天神像，然后重试秘境"));
                    }
                }
            }
            finally
            {
                InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                _runtime.Sleep(50);
                if (!_taskParam.WalkToF)
                {
                    InputHub.Foreground.SimulateAction(GIActions.SprintKeyboard, KeyType.KeyUp);
                }
            }
        }), _ct);
    }

    private Task StartFight(CombatScenes combatScenes, List<CombatCommand> combatCommands)
    {
        CancellationTokenSource cts = new();
        _ct.Register(cts.Cancel);
        combatScenes.BeforeTask(cts.Token);
        // 战斗操作
        var combatTask = new Task(() =>
        {
            try
            {
                AutoFightTask.FightStatusFlag = true;
                while (!cts.Token.IsCancellationRequested)
                {
                    // 通用化战斗策略
                    foreach (var command in combatCommands)
                    {
                        command.Execute(combatScenes);
                    }
                }
            }
            catch (NormalEndException e)
            {
                Logger.LogInformation("战斗操作中断：{Msg}", e.Message);
            }
            catch (Exception e)
            {
                Logger.LogWarning(e.Message);
                throw;
            }
            finally
            {
                Logger.LogInformation("自动战斗线程结束");
                InputHub.ReleaseAll();
                AutoFightTask.FightStatusFlag = false;
            }
        }, cts.Token);

        // 对局结束检测
        var domainEndTask = DomainEndDetectionTask(cts);
        // 自动吃药
        // var autoEatRecoveryHpTask = AutoEatRecoveryHpTask(cts.Token);
        combatTask.Start();
        domainEndTask.Start();
        // autoEatRecoveryHpTask.Start();
        return Task.WhenAll(combatTask, domainEndTask);
    }

    /// <summary>
    /// 自动连招战斗入口：Tick 后台构建的连招行为树（注入建树会话，不读静态暂存），
    /// 秘境的DomainEndDetectionTask通过CancellationToken控制战斗结束。
    /// </summary>
    private async Task StartComboFight()
    {
        CancellationTokenSource cts = new();
        _ct.Register(cts.Cancel);

        // 建树已在 WalkToPressF 前汇合完成，此处 await 已完成的任务同步返回结果
        var comboTreeSession = await _comboBuildTask!;

        // 抑制其自带的结束检测（FightFinishDetectEnabled=false），由秘境的DomainEndDetectionTask控制战斗结束
        var comboTask = new AutoComboRunTask(_runtime, _allConfig, comboTreeSession);
        var comboParam = new AutoFightParam { FightFinishDetectEnabled = false };

        var domainEndTask = DomainEndDetectionTask(cts);

        var combatTask = Task.Run(async () =>
        {
            try
            {
                // 组合调用使用 RunAsync，保留子任务异常向外传播的语义
                var result = await comboTask.RunAsync(comboParam, cts.Token);
                if (result.Kind == SoloTaskResultKind.Failed)
                {
                    Logger.LogWarning("自动连招战斗任务失败：{Msg}", result.Message);
                }
            }
            catch (RetryException)
            {
                // 复活/恢复信号必须传回 RunAsync 的重试循环，复活后重试秘境
                await cts.CancelAsync();
                throw;
            }
            catch (OperationCanceledException)
            {
                // 对局结束取消战斗，正常流程
            }
            catch (Exception e)
            {
                Logger.LogWarning("自动连招战斗任务异常：{Msg}", e.Message);
            }
        }, cts.Token);

        domainEndTask.Start();
        await Task.WhenAll(combatTask, domainEndTask);
    }

    /// <summary>
    /// JSON/键鼠策略战斗入口：委托给AutoFightEntryTask，抑制其自带的结束检测和拾取逻辑，
    /// 秘境的DomainEndDetectionTask通过CancellationToken控制战斗结束。
    /// </summary>
    private async Task StartEntryFight()
    {
        CancellationTokenSource cts = new();
        _ct.Register(cts.Cancel);

        var jsonParam = new AutoFightParam
        {
            CombatStrategyPath = _entryCombatStrategyPath!,
            FightFinishDetectEnabled = false,
            ExpBasedPickupEnabled = false,
            KazuhaPickupEnabled = false,
            PickDropsAfterFightEnabled = false,
            Timeout = 600,
        };

        var jsonTask = new AutoFightEntryTask(_runtime, _allConfig);

        var domainEndTask = DomainEndDetectionTask(cts);

        var combatTask = Task.Run(async () =>
        {
            try
            {
                // 组合调用使用 RunAsync，保留子任务异常向外传播的语义
                var result = await jsonTask.RunAsync(jsonParam, cts.Token);
                if (result.Kind == SoloTaskResultKind.Failed)
                {
                    Logger.LogWarning("JSON/键鼠战斗任务失败：{Msg}", result.Message);
                }
            }
            catch (RetryException)
            {
                // 复活/恢复信号必须传回 RunAsync 的重试循环，复活后重试秘境
                await cts.CancelAsync();
                throw;
            }
            catch (OperationCanceledException)
            {
                // 对局结束取消战斗，正常流程
            }
            catch (Exception e)
            {
                Logger.LogWarning("JSON/键鼠战斗任务异常：{Msg}", e.Message);
            }
        }, cts.Token);

        domainEndTask.Start();
        await Task.WhenAll(combatTask, domainEndTask);
    }

    private void EndFightWait()
    {
        if (_ct.IsCancellationRequested)
        {
            return;
        }

        // 沿用本次运行参数中的战后等待，不能重新读取其他页面的值。
        var s = _taskParam.FightEndDelay;
        if (s > 0)
        {
            Logger.LogInformation("战斗结束后等待 {Second} 秒", s);
            _runtime.Sleep((int)(s * 1000), _ct);
        }
    }

    /// <summary>
    /// 对局结束检测
    /// </summary>
    private Task DomainEndDetectionTask(CancellationTokenSource cts)
    {
        return new Task(async () =>
        {
            try
            {
                while (!_ct.IsCancellationRequested)
                {
                    if (IsDomainEnd())
                    {
                        await cts.CancelAsync();
                        break;
                    }

                    await _runtime.Delay(1000, cts.Token);
                }
            }
            catch
            {
            }
        }, cts.Token);
    }

    private bool IsDomainEnd()
    {
        using var ra = _runtime.CaptureToRectArea();

        var fightAssets = AutoFightAssets.Get(ra);
        var endTipsRect = ra.DeriveCrop(fightAssets.EndTipsUpperRect);
        var text = OcrFactory.Paddle.Ocr(endTipsRect.SrcMat);
        if (Regex.IsMatch(text, this.challengeCompletedLocalizedString))
        {
            Logger.LogInformation("检测到秘境结束提示(挑战达成)，结束秘境");
            return true;
        }

        endTipsRect = ra.DeriveCrop(fightAssets.EndTipsRect);
        text = OcrFactory.Paddle.Ocr(endTipsRect.SrcMat);
        if (Regex.IsMatch(text, this.autoLeavingLocalizedString))
        {
            Logger.LogInformation("检测到秘境结束提示(xxx秒后自动退出)，结束秘境");
            return true;
        }

        return false;
    }

    private Task AutoEatRecoveryHpTask(CancellationToken ct)
    {
        return new Task(async () =>
        {
            if (!_taskParam.AutoEat)
            {
                return;
            }

            if (!IsTakeFood())
            {
                Logger.LogInformation("未装备 “{Tool}”，不启用红血自动吃药功能", "便携营养袋");
                return;
            }

            try
            {
                while (!_ct.IsCancellationRequested)
                {
                    using var capture = _runtime.CaptureToRectArea();
                    if (Bv.CurrentAvatarIsLowHp(capture))
                    {
                        // 模拟按键 "Z"
                        InputHub.Foreground.SimulateAction(GIActions.QuickUseGadget);
                        Logger.LogInformation("检测到红血，按Z吃药");
                        // TODO 吃饱了会一直吃
                    }

                    await _runtime.Delay(500, ct);
                }
            }
            catch (Exception e)
            {
                Logger.LogDebug(e, "红血自动吃药检测时发生异常");
            }
        }, ct);
    }

    private bool IsTakeFood()
    {
        // 获取图像
        using var ra = _runtime.CaptureToRectArea();
        // 识别道具图标下是否是数字
        var s = _runtime.SystemInfo.AssetScale;
        var countArea = ra.DeriveCrop(1800 * s, 845 * s, 40 * s, 20 * s);
        var count = OcrFactory.Paddle.OcrWithoutDetector(countArea.CacheGreyMat);
        return int.TryParse(count, out _);
    }

    /// <summary>
    /// 旋转视角后寻找石化古树
    /// </summary>
    private Task FindPetrifiedTree()
    {
        CancellationTokenSource treeCts = new();
        _ct.Register(treeCts.Cancel);
        // 中键回正视角
        InputHub.Foreground.Mouse.MiddleButtonClick();
        _runtime.Sleep(900, _ct);

        // 左右移动直到石化古树位于屏幕中心任务
        var moveAvatarTask = MoveAvatarHorizontallyTask(treeCts);

        // 锁定东方向视角线程
        var lockCameraToEastTask = LockCameraToEastTask(treeCts, moveAvatarTask);
        lockCameraToEastTask.Start();
        return Task.WhenAll(moveAvatarTask, lockCameraToEastTask);
    }

    private Task MoveAvatarHorizontallyTask(CancellationTokenSource treeCts)
    {
        return new Task(() =>
        {
            var keyConfig = _allConfig.KeyBindingsConfig;
            var moveLeftKey = keyConfig.MoveLeft.ToVK();
            var moveRightKey = keyConfig.MoveRight.ToVK();
            var moveForwardKey = keyConfig.MoveForward.ToVK();
            var captureArea = _runtime.SystemInfo.ScaleMax1080PCaptureRect;
            var middleX = captureArea.Width / 2;
            var leftKeyDown = false;
            var rightKeyDown = false;
            var noDetectCount = 0;
            var prevKey = moveLeftKey;
            var backwardsAndForwardsCount = 0;
            while (!_ct.IsCancellationRequested)
            {
                using var capture = _runtime.CaptureToRectArea();
                var treeRect = DetectTree(capture);
                if (treeRect != default)
                {
                    var treeMiddleX = treeRect.X + treeRect.Width / 2;
                    if (treeRect.X + treeRect.Width < middleX && !_taskParam.ShortMovement)
                    {
                        backwardsAndForwardsCount = 0;
                        // 树在左边 往左走
                        Debug.WriteLine($"树在左边 往左走 {treeMiddleX}  {middleX}");
                        if (rightKeyDown)
                        {
                            // 先松开D键
                            InputHub.Foreground.Keyboard.KeyUp(moveRightKey);
                            rightKeyDown = false;
                        }

                        if (!leftKeyDown)
                        {
                            InputHub.Foreground.Keyboard.KeyDown(moveLeftKey);
                            leftKeyDown = true;
                        }
                    }
                    else if (treeRect.X > middleX && !_taskParam.ShortMovement)
                    {
                        backwardsAndForwardsCount = 0;
                        // 树在右边 往右走
                        Debug.WriteLine($"树在右边 往右走 {treeMiddleX}  {middleX}");
                        if (leftKeyDown)
                        {
                            // 先松开A键
                            InputHub.Foreground.Keyboard.KeyUp(moveLeftKey);
                            leftKeyDown = false;
                        }

                        if (!rightKeyDown)
                        {
                            InputHub.Foreground.Keyboard.KeyDown(moveRightKey);
                            rightKeyDown = true;
                        }
                    }
                    else
                    {
                        // 树在中间 松开所有键
                        if (rightKeyDown)
                        {
                            InputHub.Foreground.Keyboard.KeyUp(moveRightKey);
                            prevKey = moveRightKey;
                            rightKeyDown = false;
                        }

                        if (leftKeyDown)
                        {
                            InputHub.Foreground.Keyboard.KeyUp(moveLeftKey);
                            prevKey = moveLeftKey;
                            leftKeyDown = false;
                        }

                        // 松开按键后使用小碎步移动
                        if (treeMiddleX < middleX)
                        {
                            if (prevKey == moveRightKey)
                            {
                                backwardsAndForwardsCount++;
                            }

                            InputHub.Foreground.Keyboard.KeyDown(moveLeftKey);
                            _runtime.Sleep(60);
                            InputHub.Foreground.Keyboard.KeyUp(moveLeftKey);
                            prevKey = moveLeftKey;
                        }
                        else if (treeMiddleX > middleX)
                        {
                            if (prevKey == moveLeftKey)
                            {
                                backwardsAndForwardsCount++;
                            }

                            InputHub.Foreground.Keyboard.KeyDown(moveRightKey);
                            _runtime.Sleep(60);
                            InputHub.Foreground.Keyboard.KeyUp(moveRightKey);
                            prevKey = moveRightKey;
                        }
                        else
                        {
                            InputHub.Foreground.Keyboard.KeyDown(moveForwardKey);
                            _runtime.Sleep(60);
                            InputHub.Foreground.Keyboard.KeyUp(moveForwardKey);
                            _runtime.Sleep(500, _ct);
                            treeCts.Cancel();
                            break;
                        }
                    }
                }
                else
                {
                    backwardsAndForwardsCount = 0;
                    // 左右巡逻
                    noDetectCount++;
                    if (noDetectCount > 40)
                    {
                        if (leftKeyDown)
                        {
                            InputHub.Foreground.Keyboard.KeyUp(moveLeftKey);
                            leftKeyDown = false;
                        }

                        if (!rightKeyDown)
                        {
                            InputHub.Foreground.Keyboard.KeyDown(moveRightKey);
                            rightKeyDown = true;
                        }
                    }
                    else
                    {
                        if (rightKeyDown)
                        {
                            InputHub.Foreground.Keyboard.KeyUp(moveRightKey);
                            rightKeyDown = false;
                        }

                        if (!leftKeyDown)
                        {
                            InputHub.Foreground.Keyboard.KeyDown(moveLeftKey);
                            leftKeyDown = true;
                        }
                    }
                }

                if (backwardsAndForwardsCount >= _taskParam.LeftRightMoveTimes)
                {
                    // 左右移动5次说明已经在树中心了
                    InputHub.Foreground.Keyboard.KeyDown(moveForwardKey);
                    _runtime.Sleep(60);
                    InputHub.Foreground.Keyboard.KeyUp(moveForwardKey);
                    _runtime.Sleep(500, _ct);
                    treeCts.Cancel();
                    break;
                }

                _runtime.Sleep(60, _ct);
            }

            _runtime.MaskWindowDrawingBoard.ClearAll();
        });
    }

    private Rect DetectTree(ImageRegion region)
    {
        var result = _predictor.Predictor.Detect(region.CacheImage);
        var list = new List<MaskWindowDrawingShape>();
        foreach (var box in result)
        {
            var rect = new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height);
            list.Add(region.ToMaskWindowDrawingRect(rect));
        }

        region.DrawingBoard.Set("TreeBox", list);

        if (list.Count > 0)
        {
            var box = result[0];
            return new Rect(box.Bounds.X, box.Bounds.Y, box.Bounds.Width, box.Bounds.Height);
        }

        return default;
    }

    private Task LockCameraToEastTask(CancellationTokenSource cts, Task moveAvatarTask)
    {
        return new Task(() =>
        {
            var continuousCount = 0; // 连续东方向次数
            var started = false;
            while (!cts.Token.IsCancellationRequested)
            {
                using var captureRegion = _runtime.CaptureToRectArea();
                var angle = CameraOrientation.Compute(captureRegion.SrcMat);
                CameraOrientation.DrawDirection(captureRegion, angle);
                if (angle is >= 356 or <= 4)
                {
                    // 算作对准了
                    continuousCount++;
                    // 360 度 东方向视角
                    if (continuousCount > 5)
                    {
                        if (!started && moveAvatarTask.Status != TaskStatus.Running)
                        {
                            started = true;
                            moveAvatarTask.Start();
                        }
                    }
                }
                else
                {
                    continuousCount = 0;
                }

                if (angle <= 180)
                {
                    // 左移视角
                    var moveAngle = (int)Math.Round(angle);
                    if (moveAngle > 2)
                    {
                        moveAngle *= 2;
                    }

                    InputHub.Foreground.Mouse.MoveMouseBy(-moveAngle, 0);
                }
                else if (angle is > 180 and < 360)
                {
                    // 右移视角
                    var moveAngle = 360 - (int)Math.Round(angle);
                    if (moveAngle > 2)
                    {
                        moveAngle *= 2;
                    }

                    InputHub.Foreground.Mouse.MoveMouseBy(moveAngle, 0);
                }

                _runtime.Sleep(100, _ct);
            }

            Logger.LogInformation("锁定东方向视角线程结束");
            _runtime.MaskWindowDrawingBoard.ClearAll();
        });
    }

    /// <summary>
    /// 领取奖励
    /// </summary>
    private async Task<bool> GettingTreasure()
    {
        _guideRoundResin = 0;
        _guideRoundRewards = null;
        bool isLastTurn = false;
        // 等待窗口弹出
        await _runtime.Delay(300, _ct);

        // 1. OCR 直到确认弹出框弹出
        bool chooseResinPrompt = await NewRetry.WaitForAction(_runtime, () =>
        {
            using var ra = _runtime.CaptureToRectArea();
            var regionList = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.25, ra.Height * 0.2, ra.Width * 0.5, ra.Height * 0.6));
            var res = regionList.FirstOrDefault(t => Regex.IsMatch(t.Text, petrifiedTreeString));
            if (res != null)
            {
                // 解决水龙王按下左键后没松开，然后后续点击按下就没反应了，界面上点一下
                res.Click();
                return true;
            }

            return false;
        }, _ct, 10, 500);
        Debug.WriteLine("识别到选择树脂页");
        await _runtime.Delay(800, _ct);

        // 再 OCR 一次，弹出框，确认当前是否有原粹树脂
        using var ra2 = _runtime.CaptureToRectArea();
        var textListInPrompt = ra2.FindMulti(RecognitionObject.Ocr(ra2.Width * 0.25, ra2.Height * 0.2, ra2.Width * 0.5, ra2.Height * 0.6));
        var shortage = textListInPrompt.Any(t => Regex.IsMatch(t.Text, insufficientCountString, RegexOptions.IgnoreCase)
            || Regex.IsMatch(t.Text, replenishResinString, RegexOptions.IgnoreCase));
        if (chooseResinPrompt)
        {
            using var capture = _runtime.CaptureToRectArea();
            var regions = capture.FindMulti(RecognitionObject.Ocr(capture.Width * 0.25, capture.Height * 0.2, capture.Width * 0.5, capture.Height * 0.6));
            var offered = ResinRewardPrompt.DetectOffered(regions, capture.Width / 2d, _taskParam.GameCultureInfo);
            var candidates = _selector.Candidates(offered);
            if (candidates.Count == 0)
            {
                // “补充原粹树脂”只说明原粹不足；其他行仍有使用按钮时继续由选择器筛选。
                _stopReason = ResinExecutionRules.NoCandidateReason(offered, shortage);
                await ExitDomain();
                return false;
            }

            ResinStatus? status = null;
            try
            {
                status = ResinStatus.RecogniseFromRegion(capture, _runtime.SystemInfo, OcrFactory.Paddle);
                status.Print(Logger);
            }
            catch (Exception e) when (e is not OperationCanceledException and not NormalEndException)
            {
                Logger.LogWarning("自动秘境：顶栏树脂识别失败，无法预测最后一轮，将继续：{Reason}", e.Message);
            }

            var claimed = false;
            if (_guidePlanning && _guideActivePlan != null && _taskParam.ResinUsage.Mode != ResinUsageMode.PerType)
            {
                // 培养计划：刷至耗尽或指定次数时，按剩余需求在候选中选择树脂与原粹档位；顶栏库存未识别时无法规划，停止领取
                var used = status == null ? null : await UseTrainingGuideResin(candidates, status);
                if (used is { } claim)
                {
                    _selector.RecordClaim(claim.Kind, claim.OriginalSpent);
                    isLastTurn = ResinExecutionRules.IsLastRound(_taskParam.ResinUsage, _selector,
                        status!.OriginalResinCount, status.CondensedResinCount, claim.Kind, claim.OriginalSpent);
                    claimed = true;
                }
            }
            else
            {
                foreach (var kind in candidates)
                {
                    if (kind == ResinKind.Original && _taskParam.ResinUsage.OriginalTier != OriginalResinTier.Default)
                    {
                        var tier = _taskParam.ResinUsage.OriginalTier;
                        var switched = ResinRewardPrompt.SwitchOriginalTier(_runtime,
                            tier == OriginalResinTier.Only20 ? 20 : 40, Logger, "自动秘境", _ct);
                        if (!switched && (tier == OriginalResinTier.Only20
                            || !ResinRewardPrompt.SwitchOriginalTier(_runtime, 20, Logger, "自动秘境", _ct))) continue;
                        using var fresh = _runtime.CaptureToRectArea();
                        regions = fresh.FindMulti(RecognitionObject.Ocr(fresh.Width * 0.25, fresh.Height * 0.2, fresh.Width * 0.5, fresh.Height * 0.6));
                    }

                    // 培养计划按实际消耗扣减预算，原粹档位必须唯一确认，不能按默认 20 记账
                    if (_guidePlanning && kind == ResinKind.Original && ReadGuideOriginalCost(regions) == null)
                        throw new InvalidOperationException("未确认原粹树脂消耗档位，停止领取");
                    var (success, originalSpent) = ResinRewardPrompt.PressUseResin(_runtime, regions, kind,
                        Logger, "自动秘境", _ct, _taskParam.GameCultureInfo);
                    if (!success) continue;
                    _selector.RecordClaim(kind, originalSpent);
                    // 浓缩、须臾、脆弱树脂每次按 60 体计入培养预算
                    if (_guidePlanning) _guideRoundResin = kind == ResinKind.Original ? originalSpent : 60;
                    isLastTurn = ResinExecutionRules.IsLastRound(_taskParam.ResinUsage, _selector,
                        status?.OriginalResinCount, status?.CondensedResinCount, kind, originalSpent);
                    claimed = true;
                    break;
                }
            }
            if (!claimed)
            {
                _stopReason = ResinStopReason.NoUsableOption;
                await ExitDomain();
                return false;
            }
        }
        else
        {
            // 如果没有选择树脂的提示，说明只有原粹树脂；保持旧流程，实际消耗无法识别时按 20。
            if (shortage)
            {
                _stopReason = ResinStopReason.ResinExhausted;
                await ExitDomain();
                return false;
            }
            var originalSpent = ResinRewardPrompt.GetResinNum(new Region { Text = string.Concat(textListInPrompt.Select(r => r.Text)) },
                ResinKind.Original, Logger, "自动秘境");
            _selector.RecordClaim(ResinKind.Original, originalSpent);
            isLastTurn = _selector.IsTargetReached;
        }
        if (isLastTurn)
            _stopReason = _selector.IsTargetReached ? ResinStopReason.TargetReached : ResinStopReason.ResinExhausted;

        Notify.Event(NotificationEvent.DomainReward).Success("自动秘境奖励领取");

        _runtime.Sleep(1000, _ct);
        await TryRecognizeRewardResult();
        // 培养计划切换入口或达到轮数上限时本轮后退出；树脂原因未结束时，按目标完成记录结束原因
        if (_guidePlanning && UpdateGuideAfterReward(isLastTurn))
        {
            if (!isLastTurn) _stopReason = ResinStopReason.TargetReached;
            isLastTurn = true;
        }

        if (_taskParam.TargetItems.Count > 0 && _itemStopReached)
        {
            Logger.LogInformation("自动秘境：材料目标已全部达成，本轮领奖后结束");
            isLastTurn = true;
            // 材料目标达成按目标完成结算，不视为树脂不足。
            _stopReason = ResinStopReason.TargetReached;
            // 材料目标达成后不再切换培养入口或备选秘境
            if (_guidePlanning) _guideNextAction = GuideNextAction.Stop;
        }

        for (var i = 0; i < 30; i++)
        {
            using var ra = _runtime.CaptureToRectArea();
            if (_guidePlanning && isLastTurn)
            {
                using var plannedExit = ra.Find(RecognitionAssets.Get("AutoFight", "Exit", ra));
                if (plannedExit.IsExist())
                {
                    plannedExit.Click();
                    return false;
                }
            }
            // 优先点击继续
            using var confirmRectArea = ra.Find(RecognitionAssets.Get("AutoFight", "Confirm", ra));
            if (!confirmRectArea.IsEmpty())
            {
                if (isLastTurn)
                {
                    // 最后一回合 退出
                    using var exitRectArea = ra.Find(RecognitionAssets.Get("AutoFight", "Exit", ra));
                    if (!exitRectArea.IsEmpty())
                    {
                        exitRectArea.Click();
                        return false;
                    }
                }
                else
                {
                    if (!chooseResinPrompt)
                    {
                        // TODO 前面没有弹框的情况下，意味着只有原粹树脂，要再识别一次右上角确认树脂余量，没有余量直接退出
                    }

                    // 有体力继续
                    confirmRectArea.Click();
                    await _runtime.Delay(60, _ct); // 双击
                    confirmRectArea.Click();

                    if (!chooseResinPrompt)
                    {
                        // 真没树脂了还有提示兜底
                        await _runtime.Delay(900, _ct);
                        using var noResinPromptCapture = _runtime.CaptureToRectArea();
                        var textListInNoResinPrompt = noResinPromptCapture.FindMulti(RecognitionObject.Ocr(ra2.Width * 0.25, ra2.Height * 0.2, ra2.Width * 0.5, ra2.Height * 0.6));
                        if (textListInNoResinPrompt.Any(t => Regex.IsMatch(t.Text, retryDomainPromptPattern)))
                        {
                            var cancelBtn = textListInNoResinPrompt.FirstOrDefault(t => Regex.IsMatch(t.Text, cancelButtonString));
                            if (cancelBtn != null)
                            {
                                _stopReason = ResinStopReason.ResinExhausted;
                                cancelBtn.Click();
                                return false;
                            }
                        }
                    }

                    return true;
                }
            }

            _runtime.Sleep(300, _ct);
        }

        // 业务原因提前结束：携带失败结果，继承 NormalEndException，旧入口的“正常结束”语义不变
        throw new SoloTaskAbortException(SoloTaskResult.Failed(DomainEndNotDetectedReason, "未检测到秘境结束，可能是背包物品已满。"));
    }

    private async Task TryRecognizeRewardResult()
    {
        // 只有培养计划需要识别、但当前分辨率不支持时跳过；常规奖励识别与材料目标仍按原逻辑识别
        if (!_taskParam.ShouldRecognizeRewards() || (_guideRewardResolutionUnsupported
                && !_taskParam.RewardRecognitionEnabled && _taskParam.TargetItems.Count == 0))
        {
            return;
        }

        try
        {
            if (!await WaitForRewardResultReady())
            {
                Logger.LogWarning("自动秘境：奖励结果页未检测到退出按钮，已跳过本轮奖励识别");
                return;
            }

            // 使用多页识别（自动检测是否需要翻页）
            Logger.LogInformation("自动秘境：开始奖励识别");
            // 培养计划依据奖励更新库存，要求数量可靠；通用奖励数量失败时可忽略
            var useRewardsForPlanning = _guidePlanning && GuideRewardRecognitionEnabled;
            var rewards = RewardResultRecognizer.Instance.RecognizeMultiPage(_runtime,
                requireReliableCounts: useRewardsForPlanning,
                allowUnreliableCount: useRewardsForPlanning ? TrainingGuideRewardPolicy.IsCommonReward : null);
            if (useRewardsForPlanning)
                _guideRoundRewards = new Dictionary<string, int>(rewards);

            if (rewards.Count == 0)
            {
                Logger.LogWarning("自动秘境：本轮奖励识别结果为空");
                return;
            }

            RewardResultRecognizer.MergeIntoSummary(_rewardSummary, rewards);
            Logger.LogInformation("自动秘境：本轮奖励识别结果 {Rewards}",
                string.Join(", ", rewards.Select(r => $"{r.Key} x{r.Value}")));

            if (_taskParam.TargetItems.Count > 0)
            {
                var progress = TargetItemCatalog.Instance.CalculateDomain(_taskParam.TargetItems, _rewardSummary);
                _itemStopReached = progress.AllSatisfied;
                Logger.LogInformation("自动秘境：材料目标进度 {Progress}",
                    string.Join("，", progress.Targets.Select(x => $"{x.ItemName} {x.CountedCount}/{x.TargetCount}")));
            }
        }
        catch (Exception e) when (e is not OperationCanceledException and not NormalEndException)
        {
            Logger.LogWarning(e, "自动秘境：奖励识别失败，已跳过本轮奖励汇总");
        }
    }

    private async Task<bool> WaitForRewardResultReady()
    {
        for (var i = 0; i < 20; i++)
        {
            using var capture = _runtime.CaptureToRectArea();
            using var exitRegion = capture.Find(RecognitionAssets.Get("AutoFight", "Exit", capture));
            if (exitRegion.IsExist())
            {
                return true;
            }
            await _runtime.Delay(300, _ct);
        }
        return false;
    }

    private async Task ExitDomain()
    {
        InputHub.Foreground.Keyboard.KeyPress(VK.VK_ESCAPE);
        await _runtime.Delay(500, _ct);
        InputHub.Foreground.Keyboard.KeyPress(VK.VK_ESCAPE);
        await _runtime.Delay(800, _ct);
        using var capture = _runtime.CaptureToRectArea();
        Bv.ClickBlackConfirmButton(capture);
    }

    private async Task ArtifactSalvage()
    {
        if (!_taskParam.AutoArtifactSalvage)
        {
            return;
        }

        if (!int.TryParse(_taskParam.MaxArtifactStar, out var star))
        {
            star = 4;
        }

        await new AutoArtifactSalvageTask(_runtime).RunAsync(new AutoArtifactSalvageTaskParam(star, javaScript: null, artifactSetFilter: null, maxNumToCheck: null, recognitionFailurePolicy: null), _ct);
    }
}
