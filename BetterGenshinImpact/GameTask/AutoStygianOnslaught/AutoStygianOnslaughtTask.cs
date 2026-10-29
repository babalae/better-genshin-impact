using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.AutoDomain.Model;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Common.StateMachine;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.View.Pages.SoloTaskSettings;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;

namespace BetterGenshinImpact.GameTask.AutoStygianOnslaught;

/// <summary>
/// 状态机状态定义
/// 每个状态代表一个明确的 UI 场景
/// </summary>
public enum StygianState
{
    Unknown,               // 未知状态
    MainWorld,             // 主世界（有派蒙图标）
    EventMenu,             // 活动菜单（活动一览）
    StygianOnslaughtPage,  // 幽境危战页面（前往挑战）
    TeleportMap,           // 传送地图（传送按钮）
    DomainEntrance,        // 秘境入口（交互提示）
    DifficultySelect,      // 难度选择（单人挑战、困难、至危挑战）
    DomainLoading,         // 秘境加载中
    DomainLobby,           // 秘境门厅（地脉异常图标 + 有背包图标，可走到钥匙）
    BossSelect,            // Boss选择界面（开始挑战、角色预览）
    BattleArena,           // 战斗场地（地脉异常图标 + 无背包图标，准备战斗）
    BattleLoading,         // 战斗加载
    InBattle,              // 战斗中（无明显UI）
    BattleResultWin,       // 战斗结果-胜利（有返回按钮）
    BattleResultLose,      // 战斗结果-失败
    LeylineFlowerPrompt,   // 地脉花领取界面
    ResinSelect,           // 树脂选择
    ContinueOrExit,        // 继续或退出选择
    Exiting,               // 退出中
}

/// <summary>
/// 自动幽境危战任务 - 使用状态机模式（注册式状态处理器）
/// 
/// 设计模式：
/// 1. 继承 StateMachineBase 获得状态机基础设施
/// 2. 子类只需实现 DetectCurrentState() 和注册各状态处理器
/// 3. 运行时调用 RunStateMachineUntil() 自动驱动状态机
/// </summary>
[SoloTaskInfo(TaskId, "自动幽境危战", Group = "树脂与战斗", Order = 230, WaitForMainUi = false)]
[SoloTaskSettingsView(typeof(AutoStygianOnslaughtSettingsView), typeof(AutoStygianOnslaughtSettingsViewModel))]
public class AutoStygianOnslaughtTask : StateMachineBase<StygianState, BvPage>, ISoloTask<AutoStygianOnslaughtParam, ResinClaimData>
{
    /// <summary>本任务支持的树脂使用方式、种类、档位及默认设置。</summary>
    public static ResinUsageCapability ResinCapability => ResinCapabilities.StygianOnslaught;

    /// <summary>
    /// 独立任务稳定标识。
    /// </summary>
    public const string TaskId = "auto_stygian_onslaught";

    /// <summary>
    /// 本任务专用原因码：幽境危战的紊乱爆发期已结束，未执行挑战。
    /// </summary>
    public const string EventEndedReason = "event_ended";

    /// <summary>
    /// 日志与通知中使用的任务名称。
    /// </summary>
    private const string TaskName = "自动幽境危战";

    /// <summary>
    /// 实现基类 Logger 抽象属性 - 复用 TaskControl.Logger
    /// </summary>
    protected override ILogger Logger => TaskControl.Logger;

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根，由创建方传入
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 本次运行的业务参数，在 RunAsync 开头赋值。
    /// </summary>
    private AutoStygianOnslaughtParam _taskParam = null!;

    /// <summary>
    /// TXT 战斗策略解析结果；使用 JSON/键鼠策略时为空。
    /// </summary>
    private CombatScriptBag? _combatScriptBag;

    /// <summary>
    /// JSON/键鼠策略路径；使用 TXT 策略时为空。
    /// </summary>
    private string? _entryCombatStrategyPath;

    /// <summary>本次统一树脂选择与记账。</summary>
    private ResinClaimSelector _selector = null!;
    /// <summary>本轮领奖后的最后一轮判断，供两个状态处理入口共用。</summary>
    private bool _isLastRound;
    /// <summary>本次领奖未找到候选或点击失败，停止后续挑战。</summary>
    private bool _rewardStopped;
    /// <summary>本次树脂结束原因。</summary>
    private ResinStopReason _stopReason;
    /// <summary>是否已开始挑战。</summary>
    private bool _hasBattled;

    private LowerHeadThenWalkToTask? _lowerHeadThenWalkToTask;

    /// <summary>
    /// 创建自动幽境危战任务。业务参数通过 <see cref="RunAsync"/> 传入。
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境</param>
    /// <param name="config">配置根</param>
    public AutoStygianOnslaughtTask(GameRuntime runtime, AllConfig config) : base(runtime)
    {
        _runtime = runtime;
        _config = config;

        // 注册所有状态处理器（不依赖业务参数）
        RegisterAllStateHandlers();
    }

    /// <summary>
    /// 解析本次运行使用的战斗策略路径。
    /// 参数中的 <see cref="AutoStygianOnslaughtParam.CombatScriptBagPath"/> 非空时直接使用（与原构造函数一致）；
    /// 为空时按 <see cref="AutoStygianOnslaughtParam.StrategyName"/> 解析。
    /// </summary>
    /// <param name="param">本次运行的业务参数</param>
    /// <param name="path">解析得到的策略文件或目录路径；失败时为尝试过的路径</param>
    /// <returns>策略存在时返回 true</returns>
    private static bool TryResolveStrategyPath(AutoStygianOnslaughtParam param, out string path)
    {
        var strategy = CombatStrategyResolver.Parse(string.IsNullOrWhiteSpace(param.CombatScriptBagPath)
            ? param.StrategyName : param.CombatScriptBagPath);
        path = strategy.Path;
        return strategy.Exists;

    }

    /// <summary>
    /// 根据本次参数初始化战斗策略和树脂使用记录（原构造函数中依赖参数的部分）。
    /// </summary>
    /// <param name="param">本次运行的业务参数</param>
    /// <param name="strategyPath">已解析的战斗策略路径</param>
    private void InitializeFromParam(AutoStygianOnslaughtParam param, string strategyPath)
    {
        _taskParam = param;
        _entryCombatStrategyPath = null;
        _combatScriptBag = null;
        if (CombatStrategyResolver.Parse(strategyPath).Kind is not (CombatStrategyKind.Txt or CombatStrategyKind.AutoSelect))
        {
            _entryCombatStrategyPath = strategyPath;
            Logger.LogInformation("自动幽境危战：使用统一战斗入口执行所选策略");
        }
        else
        {
            _combatScriptBag = CombatScriptParser.ReadAndParse(strategyPath);
        }

    }

    /// <summary>
    /// 注册状态图并扫描 Attribute 标记的检测器、处理器。
    /// </summary>
    private void RegisterAllStateHandlers()
    {
        RegisterStateMethodsByAttribute();

        // 注册状态转换关系 - 有限状态机的核心
        // 每个状态只能转换到有限的下一个状态，用于优化检测
        // 注意：候选状态的顺序影响检测优先级，更具体的状态应该放前面
        RegisterStateTransitions(
            // 导航阶段
            (StygianState.MainWorld, [StygianState.EventMenu, StygianState.StygianOnslaughtPage]),
            (StygianState.EventMenu, [StygianState.StygianOnslaughtPage]),
            (StygianState.StygianOnslaughtPage, [StygianState.MainWorld, StygianState.TeleportMap, StygianState.DomainEntrance]),
            (StygianState.TeleportMap, [StygianState.DomainEntrance]),
            (StygianState.DomainEntrance, [StygianState.DifficultySelect]),
            (StygianState.DifficultySelect, [StygianState.DomainLobby]),
            (StygianState.DomainLobby, [StygianState.BossSelect, StygianState.LeylineFlowerPrompt]),

            // 战斗阶段
            (StygianState.BossSelect, [StygianState.BattleArena]),
            (StygianState.BattleArena, [StygianState.BattleResultWin, StygianState.BattleResultLose]),
            (StygianState.BattleResultWin, [StygianState.DomainLobby]),
            (StygianState.BattleResultLose, [StygianState.BossSelect]),
            (StygianState.LeylineFlowerPrompt, [StygianState.ResinSelect]),
            (StygianState.ResinSelect, [StygianState.ContinueOrExit, StygianState.DomainLobby]),
            (StygianState.ContinueOrExit, [StygianState.BattleArena, StygianState.MainWorld])
        );
    }

    /// <summary>
    /// 运行自动幽境危战：导航进入秘境、循环战斗领奖、退出后按参数分解圣遗物。
    /// </summary>
    /// <param name="param">本次运行的业务参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>
    /// 首领序号无效返回 Failed(invalid_parameter)；战斗策略不存在返回 Failed(strategy_not_found)；
    /// 紊乱爆发期已结束返回 Skipped(event_ended)；其余正常结束返回 Unverified。
    /// </returns>
    public async Task<SoloTaskResult<ResinClaimData>> RunAsync(AutoStygianOnslaughtParam param, CancellationToken ct)
    {
        if (param.ResinUsage.Validate(ResinCapability) is { } problem)
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, problem);
        _selector = new ResinClaimSelector(param.ResinUsage, ResinCapability);
        _stopReason = ResinStopReason.NoUsableOption;
        _isLastRound = false;
        _rewardStopped = false;
        _hasBattled = false;
        // 1. 参数校验：首领序号只能是 1—3
        if (param.BossNum is < 1 or > 3)
        {
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, "幽境首领序号必须为 1—3。");
        }

        // 2. 解析战斗策略：优先使用参数中的路径，为空时按策略名解析
        if (!TryResolveStrategyPath(param, out var strategyPath))
        {
            return SoloTaskResult.Failed(SoloTaskReasons.StrategyNotFound, $"{TaskName}战斗策略不存在：{strategyPath}");
        }

        if (CombatStrategyResolver.Parse(strategyPath).Kind == CombatStrategyKind.Combo)
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, "幽境危战不支持自动连招策略。");

        // 3. 依赖参数的初始化（原构造函数逻辑）
        InitializeFromParam(param, strategyPath);

        _lowerHeadThenWalkToTask = new LowerHeadThenWalkToTask(_runtime, "chest_tip.png", 20000);
        Initialize(ct, StygianState.Unknown);

        Init();
        Notify.Event(NotificationEvent.DomainStart).Success($"{TaskName}启动");

        // 4. 执行秘境流程。保持原有语义：流程中的普通异常只记录日志，仍继续执行后处理；取消向上抛出
        var shouldContinuePostProcessing = true;
        string? domainErrorMessage = null;
        try
        {
            shouldContinuePostProcessing = await DoDomain();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            // 内部子任务被取消（非本次运行取消），保持原行为
        }
        catch (Exception e)
        {
            Logger.LogInformation(e.Message);
            domainErrorMessage = e.Message;
        }

        // 被取消引起的提前结束（框架以 NormalEndException 表示）不再继续后处理
        ct.ThrowIfCancellationRequested();

        if (!shouldContinuePostProcessing)
        {
            Notify.Event(NotificationEvent.DomainEnd).Success($"{TaskName}结束");
            return SoloTaskResult.Skipped(EventEndedReason, "幽境危战紊乱爆发期已结束，未执行挑战。");
        }

        // 5. 后处理：分解圣遗物
        await _runtime.Delay(3000, ct);
        await ArtifactSalvage();
        Notify.Event(NotificationEvent.DomainEnd).Success($"{TaskName}结束");

        // 按树脂设置刷取完成返回成功；树脂不足等提前结束为“已执行，未核验”
        return ResinClaimResults.Complete(_selector, _stopReason,
            domainErrorMessage == null ? "幽境危战流程已结束" : $"幽境危战流程异常结束：{domainErrorMessage}",
            _hasBattled);
    }

    private async Task<bool> DoDomain()
    {
        var page = new BvPage(_runtime, _ct);

        // 阶段1：导航到秘境 - 状态机自动驱动
        // MainWorld → EventMenu → StygianOnslaughtPage → TeleportMap → DomainEntrance → DifficultySelect → DomainLobby → BossSelect → BattleArena
        await new ReturnMainUiTask(_runtime).Start(_ct);
        await RunStateMachineUntil(page, StygianState.StygianOnslaughtPage);
        await RunStateMachineUntil(page, [StygianState.BattleArena, StygianState.MainWorld]);

        if (CurrentState == StygianState.MainWorld)
        {
            Logger.LogInformation($"{TaskName}：活动已结束，已返回主界面");
            return false;
        }

        // 阶段2：战斗循环
        _hasBattled = true;
        await BattleLoopStateMachine(page);

        await ExitDomain(page);
        return true;
    }

    #region 状态检测器 - 注册给基类使用（接收 ImageRegion 参数复用截图）

    // ========== 第一优先级：快速模板匹配 ==========

    [StateDetector(StygianState.ContinueOrExit, Order = 10)]
    private bool DetectContinueOrExit(ImageRegion ra)
    {
        return ra.Find(RecognitionAssets.Get("AutoFight", "Confirm", ra)).IsExist() &&
               ra.Find(RecognitionAssets.Get("AutoFight", "Exit", ra)).IsExist();
    }

    [StateDetector(StygianState.TeleportMap, Order = 20)]
    private bool DetectTeleportMap(ImageRegion ra)
    {
        return ra.Find(RecognitionAssets.Get("QuickTeleport", "TeleportButton", ra)).IsExist();
    }

    [StateDetector(StygianState.DomainLobby, Order = 30)]
    private bool DetectDomainLobby(ImageRegion ra)
    {
        return ra.Find(ElementRecognition.Get("LeylineDisorderIcon", ra)).IsExist() &&
               ra.Find(ElementRecognition.Get("Inventory", ra)).IsExist();
    }

    [StateDetector(StygianState.BattleArena, Order = 40)]
    private bool DetectBattleArena(ImageRegion ra)
    {
        return ra.Find(ElementRecognition.Get("LeylineDisorderIcon", ra)).IsExist() &&
               !ra.Find(ElementRecognition.Get("Inventory", ra)).IsExist();
    }

    [StateDetector(StygianState.MainWorld, Order = 50)]
    private bool DetectMainWorld(ImageRegion ra)
    {
        return ra.Find(ElementRecognition.Get("PaimonMenu", ra)).IsExist();
    }

    // ========== 第二优先级：模板匹配 + 局部 OCR ==========

    [StateDetector(StygianState.BattleResultWin, Order = 60)]
    private bool DetectBattleResultWin(ImageRegion ra)
    {
        return ra.Find(ElementRecognition.Get("BtnWhiteCancel", ra)).IsExist() &&
               ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.35, ra.Height * 0.7, ra.Width * 0.3, ra.Height * 0.2))
                 .Any(o => o.Text.Contains("返回"));
    }

    [StateDetector(StygianState.BattleResultLose, Order = 70)]
    private bool DetectBattleResultLose(ImageRegion ra)
    {
        return ra.Find(ElementRecognition.Get("BtnWhiteConfirm", ra)).IsExist() &&
               ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.2, ra.Height * 0.3, ra.Width * 0.6, ra.Height * 0.3))
                 .Any(o => o.Text.Contains("挑战失败") || o.Text.Contains("重新挑战"));
    }

    // ========== 第三优先级：OCR 检测 ==========

    [StateDetector(StygianState.ResinSelect, Order = 80)]
    private bool DetectResinSelect(ImageRegion ra)
    {
        var ocrResult = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.2, ra.Height * 0.2, ra.Width * 0.6, ra.Height * 0.6));
        return ocrResult.Any(t => t.Text.Contains("地脉之花")) &&
               ocrResult.Any(t => t.Text.Contains("浓缩树脂") || t.Text.Contains("原粹树脂"));
    }

    [StateDetector(StygianState.LeylineFlowerPrompt, Order = 90)]
    private bool DetectLeylineFlowerPrompt(ImageRegion ra)
    {
        var ocrResult = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.2, ra.Height * 0.2, ra.Width * 0.6, ra.Height * 0.6));
        var found = ocrResult.Any(t => t.Text.Contains("地脉之花"));
        return found;
    }

    [StateDetector(StygianState.BossSelect, Order = 100)]
    private bool DetectBossSelect(ImageRegion ra)
    {
        // "角色预览" 在右上角，"开始挑战" 在右下角
        // 检测右侧整个区域
        var ocrResult = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.5, 0, ra.Width * 0.5, ra.Height));
        var hasPreview = ocrResult.Any(o => o.Text.Contains("角色预览"));
        var hasStart = ocrResult.Any(o => o.Text.Contains("开始挑战"));
        var found = hasPreview && hasStart;
        return found;
    }

    [StateDetector(StygianState.DifficultySelect, Order = 110)]
    private bool DetectDifficultySelect(ImageRegion ra)
    {
        // "单人挑战" 在右下角
        return ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.5, ra.Height * 0.7, ra.Width * 0.5, ra.Height * 0.3))
                 .Any(o => o.Text.Contains("单人挑战"));
    }

    [StateDetector(StygianState.DomainEntrance, Order = 120)]
    private bool DetectDomainEntrance(ImageRegion ra)
    {
        // 秘境入口特征：屏幕右侧有"幽境危战"四个字
        // 坐标：左上角(1223, 510), 右下角(1376, 566)
        // 宽度=153, 高度=56
        return ra.FindMulti(RecognitionObject.Ocr(1223, 510, 153, 56))
                 .Any(o => o.Text.Contains("幽境危战"));
    }

    [StateDetector(StygianState.EventMenu, Order = 130)]
    private bool DetectEventMenu(ImageRegion ra)
    {
        // 活动一览位置：左上角(125, 142), 右下角(238, 170)
        // OCR 参数：(x, y, width, height)
        return ra.FindMulti(RecognitionObject.Ocr(125, 142, 238 - 125, 170 - 142))
                 .Any(o => o.Text.Contains("活动一览"));
    }

    [StateDetector(StygianState.StygianOnslaughtPage, Order = 140)]
    private bool DetectStygianOnslaughtPage(ImageRegion ra)
    {
        // 活动详情页右侧主标题：左上角(1135, 278)，右下角(1400, 353)
        return ra.FindMulti(RecognitionObject.Ocr(1135, 278, 1400 - 1135, 353 - 278))
                 .Any(o => o.Text.Contains("幽境危战"));
    }

    #endregion

    #region 状态处理器实现 - 每个 case 的行为

    [StateHandler(StygianState.MainWorld)]
    private async Task<StateHandlerResult> HandleMainWorldState(BvPage page)
    {
        Logger.LogInformation($"{TaskName}：打开活动菜单");
        InputHub.Foreground.SimulateAction(GIActions.OpenTheEventsMenu);
        await _runtime.Delay(500, _ct);
        return StateHandlerResult.Success; // 等待转换到 EventMenu 或 StygianOnslaughtPage
    }

    [StateHandler(StygianState.EventMenu, RetryTimeout = 30000)]
    private async Task<StateHandlerResult> HandleEventMenuState(BvPage page)
    {
        Logger.LogInformation($"{TaskName}：在活动菜单中查找幽境危战");

        // 列表区域：左上角(195, 201), 右下角(491, 855)，基于 1080P
        var listCenterX = (195 + 491) / 2;  // 343
        var listCenterY = (201 + 855) / 2;  // 528
        var listRegion = new Rect(195, 201, 491 - 195, 855 - 201);

        // 最多尝试两次（先往下滑动搜索，如果没找到再往上滑动搜索）
        for (int attempt = 0; attempt < 2; attempt++)
        {
            // 1. 拖动滑动列表
            page.Click(listCenterX, listCenterY - 200);
            await _runtime.Delay(100, _ct);
            InputHub.Foreground.Mouse.LeftButtonDown();
            await _runtime.Delay(100, _ct);

            // 从上往下拖动（内容往上滚动）
            for (int y = listCenterY - 200; y < listCenterY + 200; y += 50)
            {
                _runtime.GameRegion1080PPosMove(listCenterX, y);
                await _runtime.Delay(30, _ct);
            }

            InputHub.Foreground.Mouse.LeftButtonUp();
            await _runtime.Delay(500, _ct);

            // 2. 在列表区域内查找"幽境危战"并点击
            var target = page.GetByText("幽境危战").WithRoi(listRegion).FindAll().FirstOrDefault();
            if (target != null)
            {
                target.Click();
                await _runtime.Delay(300, _ct);
                return StateHandlerResult.Success; // 等待转换到 StygianOnslaughtPage
            }

            Logger.LogInformation($"{TaskName}：第 {attempt + 1} 次未找到幽境危战，尝试反向滑动");
        }

        // 如果两次都没找到，可能详情页已在右侧；交给状态机检测下一状态。
        // 若检测不到 StygianOnslaughtPage，基类会累计当前 EventMenu 状态的转场超时次数，并在超限后退出。
        Logger.LogWarning($"{TaskName}：未找到幽境危战，尝试检测 StygianOnslaughtPage");
        return StateHandlerResult.Success;
    }

    [StateHandler(StygianState.StygianOnslaughtPage, RetryTimes = 10)]
    private async Task<StateHandlerResult> HandleStygianOnslaughtPageState(BvPage page)
    {
        using (var ra = _runtime.CaptureToRectArea())
        {
            var ocrResult = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.32, ra.Height * 0.68, ra.Width * 0.16, ra.Height * 0.1));
            if (ocrResult.Any(o => o.Text.Contains("紊乱爆发期")) &&
                ocrResult.Any(o => o.Text.Contains("已结束")))
            {
                Logger.LogInformation($"{TaskName}：检测到紊乱爆发期已结束，按 Esc 返回主界面");
                InputHub.Foreground.Keyboard.KeyPress(Vanara.PInvoke.User32.VK.VK_ESCAPE);
                await _runtime.Delay(300, _ct);
                return StateHandlerResult.SuccessTo(StygianState.MainWorld);
            }
        }

        Logger.LogInformation($"{TaskName}：点击前往挑战");
        var challengeButton = page.GetByText("前往挑战").WithRoi(r => r.CutRight(0.5)).FindAll().FirstOrDefault();
        if (challengeButton == null)
        {
            Logger.LogWarning($"{TaskName}：未找到前往挑战按钮");
            return StateHandlerResult.Retry;
        }

        challengeButton.Click();
        await _runtime.Delay(300, _ct);
        return StateHandlerResult.SuccessTo(
            StygianState.TeleportMap,
            StygianState.DomainEntrance);
    }

    [StateHandler(StygianState.TeleportMap)]
    private async Task<StateHandlerResult> HandleTeleportMapState(BvPage page)
    {
        Logger.LogInformation($"{TaskName}：点击传送");
        var teleportButton = page.Locator(RecognitionAssets.Get("QuickTeleport", "TeleportButton", _runtime.SystemInfo)).FindAll().FirstOrDefault();
        if (teleportButton == null)
        {
            Logger.LogWarning($"{TaskName}：未找到传送按钮");
            return StateHandlerResult.Retry;
        }

        teleportButton.Click();
        await _runtime.Delay(300, _ct);
        return StateHandlerResult.Success; // 等待转换到 DomainEntrance
    }

    [StateHandler(StygianState.DomainEntrance, RetryTimeout = 15000)]
    private async Task<StateHandlerResult> HandleDomainEntranceState(BvPage page)
    {
        Logger.LogInformation($"{TaskName}：交互秘境入口");
        InputHub.Foreground.SimulateAction(GIActions.PickUpOrInteract);
        await _runtime.Delay(500, _ct);
        return StateHandlerResult.Success; // 等待转换到 DifficultySelect
    }

    [StateHandler(StygianState.DifficultySelect)]
    private async Task<StateHandlerResult> HandleDifficultySelectState(BvPage page)
    {
        Logger.LogInformation($"{TaskName}：选择困难难度并进入");

        // 切换到困难模式
        await SwitchToHardModeLoop(page);

        // 点击确认进入
        using var ra = _runtime.CaptureToRectArea();
        var btn = ra.Find(ElementRecognition.Get("BtnWhiteConfirm", ra));
        if (btn.IsEmpty())
        {
            Logger.LogWarning($"{TaskName}：未找到进入确认按钮");
            return StateHandlerResult.Retry;
        }

        btn.Click();
        await _runtime.Delay(300, _ct);
        return StateHandlerResult.Success; // 等待转换到 DomainLobby
    }

    [StateHandler(StygianState.DomainLobby)]
    private async Task<StateHandlerResult> HandleDomainLobbyState(BvPage page)
    {
        Logger.LogInformation($"{TaskName}：步行前往钥匙");
        await new WalkToFTask(_runtime, _config).Start(_ct);
        return StateHandlerResult.Success; // 等待转换到 BossSelect 或 LeylineFlowerPrompt
    }

    [StateHandler(StygianState.BossSelect)]
    private async Task<StateHandlerResult> HandleBossSelectState(BvPage page)
    {
        Logger.LogInformation($"{TaskName}：选择Boss并开始挑战");

        // 选择Boss
        SelectBoss(page);

        // 切换队伍
        await SwitchTeam(page);

        // 点击开始挑战
        using var ra = _runtime.CaptureToRectArea();
        Bv.ClickWhiteConfirmButton(ra);
        await _runtime.Delay(300, _ct);
        return StateHandlerResult.Success; // 等待转换到 BattleArena
    }

    [StateHandler(StygianState.BattleArena)]
    private Task<StateHandlerResult> HandleBattleArenaState(BvPage page)
    {
        // 战斗场地已准备就绪，无需额外操作
        // 状态机会检测到 BattleArena 是目标状态并退出
        Logger.LogInformation($"{TaskName}：战斗场地已准备就绪");
        return Task.FromResult(StateHandlerResult.Wait); // 状态机会检测到目标状态并退出
    }

    [StateHandler(StygianState.BattleResultWin)]
    private async Task<StateHandlerResult> HandleBattleResultWinState(BvPage page)
    {
        Logger.LogInformation($"{TaskName}：挑战成功，等待返回大厅");
        using var ra = _runtime.CaptureToRectArea();
        Bv.ClickWhiteCancelButton(ra);
        await _runtime.Delay(300, _ct);
        return StateHandlerResult.Success; // 等待转换到 DomainLobby
    }

    [StateHandler(StygianState.BattleResultLose)]
    private async Task<StateHandlerResult> HandleBattleResultLoseState(BvPage page)
    {
        Logger.LogWarning($"{TaskName}：挑战失败，等待返回Boss选择");
        using var ra = _runtime.CaptureToRectArea();
        Bv.ClickWhiteConfirmButton(ra);
        await _runtime.Delay(300, _ct);
        return StateHandlerResult.Success; // 等待转换到 BossSelect
    }

    [StateHandler(StygianState.LeylineFlowerPrompt)]
    private async Task<StateHandlerResult> HandleLeylineFlowerState(BvPage page)
    {
        Logger.LogInformation($"{TaskName}：交互地脉花");
        InputHub.Foreground.SimulateAction(GIActions.PickUpOrInteract);
        await _runtime.Delay(300, _ct);
        return StateHandlerResult.Success; // 等待转换到 ResinSelect
    }

    [StateHandler(StygianState.ResinSelect)]
    private async Task<StateHandlerResult> HandleResinSelectState(BvPage page)
    {
        Logger.LogInformation($"{TaskName}：选择树脂");
        using var ra = _runtime.CaptureToRectArea();
        _isLastRound = await UseResinAndCheckLast(ra);
        return StateHandlerResult.Success; // 等待转换到 ContinueOrExit 或 DomainLobby
    }

    [StateHandler(StygianState.ContinueOrExit)]
    private async Task<StateHandlerResult> HandleContinueOrExitState(BvPage page)
    {
        Logger.LogInformation($"{TaskName}：处理继续/退出选择");
        using var ra = _runtime.CaptureToRectArea();

        // 检查是否还有树脂
        var isLastTurn = _isLastRound || _selector.IsTargetReached;

        if (isLastTurn)
        {
            var exitBtn = ra.Find(RecognitionAssets.Get("AutoFight", "Exit", ra));
            if (!exitBtn.IsEmpty())
            {
                exitBtn.Click();
            }
        }
        else
        {
            var confirmBtn = ra.Find(RecognitionAssets.Get("AutoFight", "Confirm", ra));
            if (!confirmBtn.IsEmpty())
            {
                confirmBtn.Click();
                await _runtime.Delay(60, _ct);
                confirmBtn.Click();
            }
        }
        await _runtime.Delay(300, _ct);
        return StateHandlerResult.Success; // 等待转换到 BattleArena 或 MainWorld
    }

    [UnknownStateHandler]
    private async Task<StateHandlerResult> HandleUnknownState(BvPage page)
    {
        Logger.LogWarning("未知状态，尝试返回主界面");
        await new ReturnMainUiTask(_runtime).Start(_ct);
        return StateHandlerResult.Wait; // 返回主界面后，状态机会重新检测状态
    }

    #endregion

    #region 战斗循环

    /// <summary>
    /// 战斗循环状态机
    /// </summary>
    private async Task BattleLoopStateMachine(BvPage page)
    {
        Logger.LogInformation("========== 开始战斗循环 ==========");

        for (var round = 0; round < 9999 && !_selector.IsTargetReached && !_rewardStopped; round++)
        {
            _ct.ThrowIfCancellationRequested();
            Logger.LogInformation(">>> 第 {Round} 轮战斗 <<<", round + 1);

            // 执行战斗
            await ExecuteBattleRound(page);

            // 处理战斗结果（战斗结束后，等待 BattleArena 的邻接状态）
            CurrentState = StygianState.BattleArena;
            var resultState = await EnsureNextStateTransition(60000);

            if (resultState == StygianState.BattleResultLose)
            {
                // 内部使用闭环检测确保返回Boss选择
                await HandleBattleResultLoseState(page);
                continue;
            }

            // 胜利后处理（内部使用闭环检测确保返回大厅）
            await HandleBattleResultWinState(page);

            // 防止在地脉花上
            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            await _runtime.Delay(200, _ct);
            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            await _runtime.Delay(2000, _ct);

            // 寻找地脉花
            Logger.LogInformation($"{TaskName}：寻找地脉花");
            await FindAndInteractLeylineFlowerLoop();

            // 处理奖励
            var shouldContinue = await HandleRewardStateMachine();
            if (!shouldContinue)
            {
                Logger.LogInformation($"{TaskName}：体力耗尽或轮次达标，结束战斗");
                break;
            }

            Notify.Event(NotificationEvent.DomainReward).Success($"{TaskName}奖励领取");
            // 点击继续后会直接进入战斗场地（等待 ContinueOrExit 的邻接状态）
            CurrentState = StygianState.ContinueOrExit;
            await EnsureNextStateTransition(60000);
        }

        Logger.LogInformation("========== 战斗循环结束 ==========");
    }

    private async Task ExecuteBattleRound(BvPage page)
    {
        // 等待进入战斗场地（从 BossSelect 转换到 BattleArena）
        CurrentState = StygianState.BossSelect;
        await EnsureNextStateTransition(60000);

        if (_entryCombatStrategyPath != null)
        {
            Logger.LogInformation($"{TaskName}：执行战斗策略(JSON/键鼠)");
            await StartEntryFight();
        }
        else
        {
            // 初始化战斗
            var combatScenes = await InitializeCombatScenesLoop();
            var combatCommands = await PrepareForBattleLoop(combatScenes);

            Logger.LogInformation($"{TaskName}：执行战斗策略");
            await StartFight(combatScenes, combatCommands);
        }
    }

    /// <summary>
    /// 处理奖励状态机
    /// </summary>
    private async Task<bool> HandleRewardStateMachine()
    {
        await _runtime.Delay(300, _ct);

        // 等待奖励界面（从 LeylineFlowerPrompt 转换到 ResinSelect）
        CurrentState = StygianState.LeylineFlowerPrompt;
        var resinState = await EnsureNextStateTransition(10000);

        if (resinState == StygianState.Unknown)
        {
            Logger.LogWarning("未检测到奖励界面");
            return true;
        }

        using var ra = _runtime.CaptureToRectArea();
        var textList = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.25, ra.Height * 0.2, ra.Width * 0.5, ra.Height * 0.6));

        // 不因单独的“补充原粹树脂”提示退出，所有行都交给选择器判断。
        var isLastTurn = await UseResinAndCheckLast(ra);
        if (_rewardStopped) return false;

        await _runtime.Delay(1000, _ct);

        // 等待继续/退出界面（从 ResinSelect 转换到 ContinueOrExit 或 DomainLobby）
        CurrentState = StygianState.ResinSelect;
        var continueState = await EnsureNextStateTransition(10000);

        if (continueState == StygianState.ContinueOrExit)
        {
            if (isLastTurn)
            {
                using var ra2 = _runtime.CaptureToRectArea();
                var exitBtn = ra2.Find(RecognitionAssets.Get("AutoFight", "Exit", ra2));
                if (!exitBtn.IsEmpty())
                {
                    exitBtn.Click();
                    return false;
                }
            }
            else
            {
                using var ra2 = _runtime.CaptureToRectArea();
                var confirmBtn = ra2.Find(RecognitionAssets.Get("AutoFight", "Confirm", ra2));
                if (!confirmBtn.IsEmpty())
                {
                    confirmBtn.Click();
                    await _runtime.Delay(60, _ct);
                    confirmBtn.Click();
                }
            }
        }

        return !isLastTurn;
    }

    /// <summary>使用统一候选并在成功点击后记账，保存本轮的结束判断供状态机共用。</summary>
    private async Task<bool> UseResinAndCheckLast(ImageRegion ra)
    {
        // 设置了勋望目标时，领奖前先读取进度；读到已达标则不再领奖。未识别（null）按未知继续，不能当作 0
        if (_taskParam.DirePrestigeTarget > 0
            && await StygianDirePrestigeReader.ReadAsync(_runtime, _ct) is { } prestige
            && prestige.Current >= _taskParam.DirePrestigeTarget)
        {
            Logger.LogInformation("{TaskName}：危战勋望 {Current}/{Target} 已达到目标，停止领奖", TaskName,
                prestige.Current, _taskParam.DirePrestigeTarget);
            _stopReason = ResinStopReason.TargetReached;
            _rewardStopped = true;
            return _isLastRound = true;
        }

        var regions = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.25, ra.Height * 0.2, ra.Width * 0.5, ra.Height * 0.6));
        var offered = ResinRewardPrompt.DetectOffered(regions, ra.Width / 2d, _taskParam.GameCultureInfo);
        var candidates = _selector.Candidates(offered);
        if (candidates.Count == 0)
        {
            _stopReason = _selector.IsTargetReached ? ResinStopReason.TargetReached
                : ResinExecutionRules.NoCandidateReason(offered, ResinRewardPrompt.HasShortageHint(regions, _taskParam.GameCultureInfo));
            _rewardStopped = true;
            return _isLastRound = true;
        }
        ResinStatus? status = null;
        try
        {
            status = ResinStatus.RecogniseFromRegion(ra, _runtime.SystemInfo, OcrFactory.Paddle);
            status.Print(Logger);
        }
        catch (Exception e) when (e is not OperationCanceledException and not NormalEndException)
        {
            Logger.LogWarning("{TaskName}：顶栏识别失败，无法预测最后一轮，将继续：{Reason}", TaskName, e.Message);
        }
        foreach (var kind in candidates)
        {
            if (kind == ResinKind.Original && _taskParam.ResinUsage.OriginalTier != OriginalResinTier.Default)
            {
                // 幽境已确认可切 20/40，沿用秘境的切档模板；实际模板匹配仍需游戏内验收。
                var tier = _taskParam.ResinUsage.OriginalTier;
                var switched = ResinRewardPrompt.SwitchOriginalTier(_runtime, tier == OriginalResinTier.Only20 ? 20 : 40, Logger, TaskName, _ct);
                if (!switched && (tier == OriginalResinTier.Only20
                    || !ResinRewardPrompt.SwitchOriginalTier(_runtime, 20, Logger, TaskName, _ct))) continue;
                using var fresh = _runtime.CaptureToRectArea();
                regions = fresh.FindMulti(RecognitionObject.Ocr(fresh.Width * 0.25, fresh.Height * 0.2, fresh.Width * 0.5, fresh.Height * 0.6));
            }
            var (success, spent) = ResinRewardPrompt.PressUseResin(_runtime, regions, kind, Logger, TaskName, _ct, _taskParam.GameCultureInfo);
            if (!success) continue;
            _selector.RecordClaim(kind, spent);
            _isLastRound = ResinExecutionRules.IsLastRound(_taskParam.ResinUsage, _selector,
                status?.OriginalResinCount, status?.CondensedResinCount, kind, spent);
            if (_isLastRound) _stopReason = _selector.IsTargetReached ? ResinStopReason.TargetReached : ResinStopReason.ResinExhausted;
            return _isLastRound;
        }
        _rewardStopped = true;
        _stopReason = ResinStopReason.NoUsableOption;
        return _isLastRound = true;
    }

    #endregion

    #region 辅助方法

    private void Init()
    {
        LogScreenResolution();
        Logger.LogInformation("{TaskName}：{ResinUsage}", TaskName, ResinUsageText.Summary(_taskParam.ResinUsage, ResinCapability));
    }

    private void LogScreenResolution()
    {
        var gameScreenSize = SystemControl.GetGameScreenRect(_runtime.Window.Handle);
        if (gameScreenSize.Width * 9 != gameScreenSize.Height * 16)
        {
            Logger.LogError("游戏窗口分辨率不是 16:9 ！当前分辨率为 {Width}x{Height}",
                gameScreenSize.Width, gameScreenSize.Height);
            throw new Exception("游戏窗口分辨率不是 16:9");
        }

        if (gameScreenSize.Width < 1920 || gameScreenSize.Height < 1080)
        {
            Logger.LogWarning("游戏窗口分辨率小于 1920x1080 ！当前分辨率为 {Width}x{Height}",
                gameScreenSize.Width, gameScreenSize.Height);
        }
    }

    private async Task<CombatScenes> InitializeCombatScenesLoop()
    {
        CombatScenes? result = null;
        var found = await NewRetry.WaitForAction(_runtime, () =>
        {
            result = new CombatScenes(_runtime, _config).InitializeTeam(_runtime.CaptureToRectArea());
            return result.CheckTeamInitialized();
        }, _ct, 10, 500);

        if (!found || result == null)
        {
            throw new Exception("识别队伍角色失败！");
        }
        Logger.LogInformation($"{TaskName}：队伍初始化成功");
        return result;
    }

    private async Task<List<CombatCommand>> PrepareForBattleLoop(CombatScenes combatScenes)
    {
        var combatCommands = FindCombatScriptAndSwitchAvatar(combatScenes);
        await _runtime.Delay(1500, _ct);

        InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        await _runtime.Delay(1200, _ct);
        InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);

        return combatCommands;
    }

    private List<CombatCommand> FindCombatScriptAndSwitchAvatar(CombatScenes combatScenes)
    {
        var combatCommands = _combatScriptBag.FindCombatScript(combatScenes.GetAvatars());
        var avatar = combatScenes.SelectAvatar(combatCommands[0].Name);
        avatar?.SwitchWithoutCts();
        _runtime.Sleep(200, _ct);
        return combatCommands;
    }

    private async Task FindAndInteractLeylineFlowerLoop()
    {
        // 先看看当前身边是否有F，有的话，直接F
        using var ra1 = _runtime.CaptureToRectArea();
        var text = Bv.FindFKeyText(ra1);
        if (string.IsNullOrEmpty(text) || !text.Contains("激活"))
        {
            await _lowerHeadThenWalkToTask!.Start(_ct);
        }

        await NewRetry.WaitForAction(_runtime, () =>
        {
            InputHub.Foreground.SimulateAction(GIActions.PickUpOrInteract);
            _runtime.Sleep(300, _ct);

            using var ra = _runtime.CaptureToRectArea();
            var ocrList = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.25, ra.Height * 0.2, ra.Width * 0.5, ra.Height * 0.6));
            if (ocrList.Any(t => t.Text.Contains("地脉之花")))
            {
                Logger.LogInformation($"{TaskName}：成功交互地脉花");
                return true;
            }
            return false;
        }, _ct, 10, 300);
    }

    private async Task SwitchToHardModeLoop(BvPage page)
    {
        var found = await NewRetry.WaitForAction(_runtime, () =>
        {
            // 如果已经在困难模式，直接返回
            if (page.GetByText("困难").WithRoi(r => r.CutTop(0.2)).IsExist())
            {
                return true;
            }

            // 检测是否在至危挑战模式，点击切换到常规挑战
            var ultimateChallenge = page.GetByText("至危挑战").WithRoi(r => r.CutLeftTop(0.5, 0.2)).FindAll().FirstOrDefault();
            if (ultimateChallenge != null)
            {
                Logger.LogInformation($"{TaskName}：检测到至危挑战，点击切换到常规挑战");
                ultimateChallenge.Click();
                _runtime.Sleep(500, _ct);
                return false;
            }

            // 检测常规挑战模式，点击右侧打开难度选择菜单
            var normalChallenge = page.GetByText("常规挑战").WithRoi(r => r.CutLeftTop(0.5, 0.2)).FindAll().FirstOrDefault();
            if (normalChallenge != null)
            {
                Logger.LogInformation($"{TaskName}：检测到常规挑战，点击打开难度菜单");
                // 点击常规挑战右侧 400 像素处打开难度菜单
                page.Click(normalChallenge.X + normalChallenge.Width + 400, normalChallenge.Y + normalChallenge.Height / 2);
                _runtime.Sleep(500, _ct);
            }
            else
            {
                // 如果界面不显示“常规挑战/至危挑战”，直接点击顶部难度栏
                Logger.LogInformation($"{TaskName}：未检测到挑战模式选项，点击顶部难度栏");
                page.Click(1300, 190);
                _runtime.Sleep(500, _ct);
            }

            // 在难度菜单中查找并点击“困难”
            var hardMode = page.GetByText("困难").FindAll().FirstOrDefault();
            if (hardMode != null)
            {
                Logger.LogInformation($"{TaskName}：点击困难模式");
                hardMode.Click();
                _runtime.Sleep(300, _ct);
            }

            return false;
        }, _ct, 10, 500);

        if (found)
        {
            Logger.LogInformation($"{TaskName}：确认困难模式");
        }
        else
        {
            Logger.LogWarning("切换困难模式失败，继续执行");
        }
    }

    private void SelectBoss(BvPage page)
    {
        Logger.LogInformation($"{TaskName}：选择BOSS编号{{Text}}", _taskParam.BossNum);

        var bossPositions = new Dictionary<int, (int x, int y)>
        {
            { 1, (196, 346) },
            { 2, (237, 541) },
            { 3, (203, 728) }
        };

        if (!bossPositions.TryGetValue(_taskParam.BossNum, out var pos))
        {
            pos = bossPositions[1];
        }

        // 直接点击选择BOSS，无需重试或等待
        page.Click(pos.x, pos.y);
    }

    private Task StartFight(CombatScenes combatScenes, List<CombatCommand> combatCommands)
    {
        CancellationTokenSource cts = new();
        _ct.Register(cts.Cancel);
        combatScenes.BeforeTask(cts.Token);

        var combatTask = new Task(() =>
        {
            try
            {
                AutoFightTask.FightStatusFlag = true;
                while (!cts.Token.IsCancellationRequested)
                {
                    for (var i = 0; i < combatCommands.Count; i++)
                    {
                        var command = combatCommands[i];
                        var lastCommand = i == 0 ? command : combatCommands[i - 1];
                        command.Execute(combatScenes, lastCommand);
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
                InputHub.Foreground.Mouse.LeftButtonUp();
                AutoFightTask.FightStatusFlag = false;
            }
        }, cts.Token);

        var domainEndTask = DomainEndDetectionTask(cts);
        combatTask.Start();
        domainEndTask.Start();
        return Task.WhenAll(combatTask, domainEndTask);
    }

    /// <summary>
    /// JSON/键鼠策略战斗入口：委托给AutoFightEntryTask，抑制其自带的结束检测和拾取逻辑，
    /// 幽境危战的DomainEndDetectionTask通过CancellationToken控制战斗结束。
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

        var jsonTask = new AutoFightEntryTask(_runtime, _config);

        var domainEndTask = DomainEndDetectionTask(cts);

        var combatTask = Task.Run(async () =>
        {
            try
            {
                // 战斗结束由 DomainEndDetectionTask 取消 cts 控制，结果不参与判断
                await jsonTask.RunAsync(jsonParam, cts.Token);
            }
            catch (Exception e)
            {
                Logger.LogWarning("JSON/键鼠战斗任务异常：{Msg}", e.Message);
            }
        }, cts.Token);

        domainEndTask.Start();
        await Task.WhenAll(combatTask, domainEndTask);
    }

    private Task DomainEndDetectionTask(CancellationTokenSource cts)
    {
        return new Task(async void () =>
        {
            try
            {
                var captureRect = _runtime.SystemInfo.ScaleMax1080PCaptureRect;
                var assetScale = _runtime.SystemInfo.AssetScale;
                RecognitionObject whiteCancelRo = new RecognitionObject
                {
                    Name = "BtnWhiteCancel",
                    RecognitionType = RecognitionTypes.TemplateMatch,
                    TemplateImageMat = ElementRecognition.Get("BtnWhiteCancel", _runtime.SystemInfo).TemplateImageMat,
                    RegionOfInterest = new Rect(captureRect.Width / 3, captureRect.Height - (int)(captureRect.Height * 0.22), captureRect.Width / 3, (int)(captureRect.Height * 0.22)),
                    Use3Channels = true
                }.InitTemplate();

                await NewRetry.WaitForAction(_runtime, () =>
                {
                    using var ra = _runtime.CaptureToRectArea();
                    using var ret = ra.Find(whiteCancelRo);
                    if (ret.IsExist())
                    {
                        var list = ra.FindMulti(RecognitionObject.Ocr(ret.X + 40 * assetScale, ret.Y - 20 * assetScale, 270 * assetScale, ret.Height * 2));
                        if (list.Any(o => o.Text.Contains("返回")))
                        {
                            return true;
                        }
                    }
                    return false;
                }, cts.Token, 300, 1000);
                Logger.LogInformation("检测到战斗结束，结束战斗操作线程");
                await cts.CancelAsync();
            }
            catch (Exception e)
            {
                Logger.LogInformation("对局结束检测线程异常结束：{Msg}", e.Message);
                Logger.LogDebug(e, "对局结束检测线程异常结束");
            }
        }, cts.Token);
    }

    private async Task SwitchTeam(BvPage page)
    {
        var fightTeamName = _taskParam.FightTeamName;
        if (string.IsNullOrEmpty(fightTeamName))
        {
            Logger.LogInformation($"{TaskName}：不更换战斗队伍");
            return;
        }

        Logger.LogInformation($"{TaskName}：配置战斗队伍为：{fightTeamName}");
        await OpenTeamPanelLoop(page);
        await FindAndSelectTeamLoop(page, fightTeamName);
    }

    private async Task OpenTeamPanelLoop(BvPage page)
    {
        var found = await NewRetry.WaitForAction(_runtime, () =>
        {
            if (page.GetByText("预设队伍").WithRoi(r => r.CutLeftTop(0.15, 0.075)).IsExist())
            {
                return true;
            }

            var teamButton = page.GetByText("预设队伍").WithRoi(r => r.CutRightBottom(0.3, 0.1)).FindAll().FirstOrDefault();
            teamButton?.Click();
            _runtime.Sleep(300, _ct);
            return false;
        }, _ct);

        if (found)
        {
            Logger.LogInformation($"{TaskName}：预设队伍面板已打开");
        }
        else
        {
            Logger.LogWarning("未找到预设队伍按钮，不执行切换操作");
        }
    }

    private async Task FindAndSelectTeamLoop(BvPage page, string fightTeamName)
    {
        page.Click(936, 150);
        await _runtime.Delay(100, _ct);
        InputHub.Foreground.Mouse.LeftButtonDown();
        await _runtime.Delay(100, _ct);
        _runtime.GameRegion1080PPosMove(936, 140);
        await _runtime.Delay(100, _ct);

        int yOffset = 0;
        const int maxRetries = 30;
        const int scrollStep = 100;

        try
        {
            for (int retries = 0; retries < maxRetries; retries++)
            {
                var teamRegionList = page.GetByText(fightTeamName).WithRoi(r => r.CutLeft(0.18)).FindAll();
                var foundTeam = teamRegionList.FirstOrDefault();
                if (foundTeam != null)
                {
                    InputHub.Foreground.Mouse.LeftButtonUp();
                    await _runtime.Delay(200, _ct);

                    for (int j = 0; j < 5; j++)
                    {
                        // 点击队伍名称右侧区域，避免面板关闭后重复点击落到左侧 Boss 图标。
                        foundTeam.ClickTo(foundTeam.Width / 2 + 250, foundTeam.Height / 2);
                        await _runtime.Delay(200, _ct);
                    }
                    Logger.LogInformation($"{TaskName}：已选择队伍 {fightTeamName}");
                    return;
                }

                yOffset += scrollStep;
                if (130 + yOffset > 1080)
                {
                    Logger.LogWarning("未找到预设战斗队伍名称：{TeamName}，保持原有队伍", fightTeamName);
                    break;
                }

                _runtime.GameRegion1080PPosMove(936, 130 + yOffset);
                await _runtime.Delay(200, _ct);
            }
        }
        finally
        {
            InputHub.Foreground.Mouse.LeftButtonUp();
            await _runtime.Delay(100, _ct);
        }

        InputHub.Foreground.SimulateAction(GIActions.OpenPaimonMenu);
        await _runtime.Delay(300, _ct);
    }

    private async Task ExitDomain(BvPage page)
    {
        await OpenExitMenuAndClickLoop(page);
        await WaitExitCompleteLoop(page);
    }

    private async Task OpenExitMenuAndClickLoop(BvPage page)
    {
        var found = await NewRetry.WaitForElementAppear(_runtime,
            ElementRecognition.Get("BtnExitDoor", _runtime.SystemInfo),
            () => InputHub.Foreground.SimulateAction(GIActions.OpenPaimonMenu),
            _ct);

        if (found)
        {
            await page.Locator(ElementRecognition.Get("BtnExitDoor", _runtime.SystemInfo)).Click();
            Logger.LogInformation($"{TaskName}：点击退出秘境");
        }
        else
        {
            Logger.LogWarning("未能找到退出秘境按钮，可能已经退出秘境");
        }
    }

    private async Task WaitExitCompleteLoop(BvPage page)
    {
        var found = await Bv.WaitUntilFound(_runtime, ElementRecognition.Get("PaimonMenu", _runtime.SystemInfo), _ct, 200, 300);
        if (found)
        {
            Logger.LogInformation($"{TaskName}：退出秘境完成");
            await _runtime.Delay(1000, _ct);
        }
    }

    private async Task ArtifactSalvage()
    {
        if (!_taskParam.AutoArtifactSalvage)
        {
            return;
        }

        if (!int.TryParse(_taskParam.MaxArtifactStar ?? _config.AutoArtifactSalvageConfig.MaxArtifactStar, out var star))
        {
            star = 4;
        }

        await new AutoArtifactSalvageTask(_runtime).RunAsync(new AutoArtifactSalvageTaskParam(star, javaScript: null, artifactSetFilter: null, maxNumToCheck: null, recognitionFailurePolicy: null), _ct);
    }

    #endregion
}
