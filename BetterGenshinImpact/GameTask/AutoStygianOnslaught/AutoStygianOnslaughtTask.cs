using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Simulator;
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
public class AutoStygianOnslaughtTask : StateMachineBase<StygianState, BvPage>, ISoloTask
{
    public string Name => "自动幽境危战";

    /// <summary>
    /// 实现基类 Logger 抽象属性 - 复用 TaskControl.Logger
    /// </summary>
    protected override ILogger Logger => TaskControl.Logger;

    private readonly AutoStygianOnslaughtConfig _taskParam;
    private readonly CombatScriptBag _combatScriptBag;
    private List<ResinUseRecord> _resinPriorityListWhenSpecifyUse;
    private LowerHeadThenWalkToTask? _lowerHeadThenWalkToTask;

    public AutoStygianOnslaughtTask(AutoStygianOnslaughtConfig taskParam, string path)
    {
        AutoFightAssets.DestroyInstance();
        _taskParam = taskParam;
        _combatScriptBag = CombatScriptParser.ReadAndParse(path);
        _resinPriorityListWhenSpecifyUse = ResinUseRecord.BuildFromDomainParam(taskParam);

        // 注册所有状态处理器
        RegisterAllStateHandlers();
    }

    /// <summary>
    /// 注册所有状态处理器和检测器 - 子类核心职责
    /// 每个状态对应一个处理方法，消除 switch-case
    /// </summary>
    private void RegisterAllStateHandlers()
    {
        // ========== 注册状态检测器 ==========
        // 注意：检测顺序会影响性能，先放快速的模板匹配检测
        RegisterStateDetectors(
            // 第一优先级：快速模板匹配（不需要 OCR）
            (StygianState.ContinueOrExit, DetectContinueOrExit),
            (StygianState.TeleportMap, DetectTeleportMap),
            (StygianState.DomainLobby, DetectDomainLobby),
            (StygianState.BattleArena, DetectBattleArena),
            (StygianState.MainWorld, DetectMainWorld),

            // 第二优先级：模板匹配 + 局部 OCR
            (StygianState.BattleResultWin, DetectBattleResultWin),
            (StygianState.BattleResultLose, DetectBattleResultLose),

            // 第三优先级：OCR 检测
            (StygianState.ResinSelect, DetectResinSelect),
            (StygianState.LeylineFlowerPrompt, DetectLeylineFlowerPrompt),
            (StygianState.BossSelect, DetectBossSelect),
            (StygianState.DifficultySelect, DetectDifficultySelect),
            (StygianState.DomainEntrance, DetectDomainEntrance),
            (StygianState.EventMenu, DetectEventMenu),
            (StygianState.StygianOnslaughtPage, DetectStygianOnslaughtPage)
        );

        // ========== 注册状态处理器 ==========
        // 导航阶段处理器
        RegisterStateHandlers(
            (StygianState.MainWorld, HandleMainWorldState),
            (StygianState.EventMenu, HandleEventMenuState),
            (StygianState.StygianOnslaughtPage, HandleStygianOnslaughtPageState),
            (StygianState.TeleportMap, HandleTeleportMapState),
            (StygianState.DomainEntrance, HandleDomainEntranceState),
            (StygianState.DifficultySelect, HandleDifficultySelectState),
            (StygianState.DomainLobby, HandleDomainLobbyState),

            // 战斗阶段处理器
            (StygianState.BossSelect, HandleBossSelectState),
            (StygianState.BattleArena, HandleBattleArenaState),
            (StygianState.BattleResultWin, HandleBattleResultWinState),
            (StygianState.BattleResultLose, HandleBattleResultLoseState),
            (StygianState.LeylineFlowerPrompt, HandleLeylineFlowerState),
            (StygianState.ResinSelect, HandleResinSelectState),
            (StygianState.ContinueOrExit, HandleContinueOrExitState)
        );

        // 注册状态转换关系 - 有限状态机的核心
        // 每个状态只能转换到有限的下一个状态，用于优化检测
        // 注意：候选状态的顺序影响检测优先级，更具体的状态应该放前面
        RegisterStateTransitions(
            // 导航阶段
            (StygianState.MainWorld, [StygianState.EventMenu, StygianState.StygianOnslaughtPage]),
            (StygianState.EventMenu, [StygianState.StygianOnslaughtPage]),
            (StygianState.StygianOnslaughtPage, [StygianState.TeleportMap, StygianState.DomainEntrance]),
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

        // 未知状态处理器
        RegisterUnknownStateHandler(HandleUnknownState);
    }

    public async Task Start(CancellationToken ct)
    {
        _lowerHeadThenWalkToTask = new LowerHeadThenWalkToTask("chest_tip.png", 20000);
        Initialize(ct, StygianState.Unknown);

        Init();
        Notify.Event(NotificationEvent.DomainStart).Success($"{Name}启动");

        try
        {
            await DoDomain();
        }
        catch (TaskCanceledException)
        {
            // do nothing
        }
        catch (Exception e)
        {
            Logger.LogInformation(e.Message);
        }

        await Delay(3000, ct);
        await ArtifactSalvage();
        Notify.Event(NotificationEvent.DomainEnd).Success($"{Name}结束");
    }

    private async Task DoDomain()
    {
        var page = new BvPage(_ct);

        // 阶段1：导航到秘境 - 状态机自动驱动
        // MainWorld → EventMenu → StygianOnslaughtPage → TeleportMap → DomainEntrance → DifficultySelect → DomainLobby → BossSelect → BattleArena
        await new ReturnMainUiTask().Start(_ct);
        await RunStateMachineUntil(page, StygianState.BattleArena);

        // 阶段2：战斗循环
        await BattleLoopStateMachine(page);

        await ExitDomain(page);
    }

    #region 状态检测器 - 注册给基类使用（接收 ImageRegion 参数复用截图）

    // ========== 第一优先级：快速模板匹配 ==========

    private bool DetectContinueOrExit(ImageRegion ra)
    {
        return ra.Find(AutoFightAssets.Instance.ConfirmRa).IsExist() &&
               ra.Find(AutoFightAssets.Instance.ExitRa).IsExist();
    }

    private bool DetectTeleportMap(ImageRegion ra)
    {
        return ra.Find(QuickTeleportAssets.Instance.TeleportButtonRo).IsExist();
    }

    private bool DetectDomainLobby(ImageRegion ra)
    {
        return ra.Find(ElementAssets.Instance.LeylineDisorderIconRo).IsExist() &&
               ra.Find(ElementAssets.Instance.InventoryRo).IsExist();
    }

    private bool DetectBattleArena(ImageRegion ra)
    {
        return ra.Find(ElementAssets.Instance.LeylineDisorderIconRo).IsExist() &&
               !ra.Find(ElementAssets.Instance.InventoryRo).IsExist();
    }

    private bool DetectMainWorld(ImageRegion ra)
    {
        return ra.Find(ElementAssets.Instance.PaimonMenuRo).IsExist();
    }

    // ========== 第二优先级：模板匹配 + 局部 OCR ==========

    private bool DetectBattleResultWin(ImageRegion ra)
    {
        return ra.Find(ElementAssets.Instance.BtnWhiteCancel).IsExist() &&
               ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.35, ra.Height * 0.7, ra.Width * 0.3, ra.Height * 0.2))
                   .Any(o => o.Text.Contains("返回"));
    }

    private bool DetectBattleResultLose(ImageRegion ra)
    {
        return ra.Find(ElementAssets.Instance.BtnWhiteConfirm).IsExist() &&
               ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.2, ra.Height * 0.3, ra.Width * 0.6, ra.Height * 0.3))
                   .Any(o => o.Text.Contains("挑战失败") || o.Text.Contains("重新挑战"));
    }

    // ========== 第三优先级：OCR 检测 ==========

    private bool DetectResinSelect(ImageRegion ra)
    {
        var ocrResult = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.2, ra.Height * 0.2, ra.Width * 0.6, ra.Height * 0.6));
        return ocrResult.Any(t => t.Text.Contains("地脉之花")) &&
               ocrResult.Any(t => t.Text.Contains("浓缩树脂") || t.Text.Contains("原粹树脂"));
    }

    private bool DetectLeylineFlowerPrompt(ImageRegion ra)
    {
        var ocrResult = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.2, ra.Height * 0.2, ra.Width * 0.6, ra.Height * 0.6));
        var found = ocrResult.Any(t => t.Text.Contains("地脉之花"));

        // 调试日志
        var texts = ocrResult.Any()
            ? string.Join(", ", ocrResult.Select(o => $"'{o.Text}'"))
            : "（无结果）";
        Logger.LogInformation($"DetectLeylineFlowerPrompt: OCR结果=[{texts}], 地脉之花={found}");

        return found;
    }

    private bool DetectBossSelect(ImageRegion ra)
    {
        // "角色预览" 在右上角，"开始挑战" 在右下角
        // 检测右侧整个区域
        var ocrResult = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.5, 0, ra.Width * 0.5, ra.Height));
        var hasPreview = ocrResult.Any(o => o.Text.Contains("角色预览"));
        var hasStart = ocrResult.Any(o => o.Text.Contains("开始挑战"));
        var found = hasPreview && hasStart;
        
        // 调试日志
        var texts = ocrResult.Any()
            ? string.Join(", ", ocrResult.Select(o => $"'{o.Text}'"))
            : "（无结果）";
        Logger.LogInformation($"DetectBossSelect: 右侧OCR结果=[{texts}], 角色预览={hasPreview}, 开始挑战={hasStart}");
        
        return found;
    }

    private bool DetectDifficultySelect(ImageRegion ra)
    {
        // "单人挑战" 在右下角
        var ocrResult = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.5, ra.Height * 0.7, ra.Width * 0.5, ra.Height * 0.3));
        var found = ocrResult.Any(o => o.Text.Contains("单人挑战"));
        
        // 调试日志
        var texts = ocrResult.Any()
            ? string.Join(", ", ocrResult.Select(o => $"'{o.Text}'"))
            : "（无结果）";
        Logger.LogInformation($"DetectDifficultySelect: 右下角OCR结果=[{texts}], 包含单人挑战={found}");
        
        return found;
    }

    private bool DetectDomainEntrance(ImageRegion ra)
    {
        // 秘境入口特征：屏幕右侧有"幽境危战"四个字
        // 坐标：左上角(1223, 510), 右下角(1376, 566)
        // 宽度=153, 高度=56
        var ocrResult = ra.FindMulti(RecognitionObject.Ocr(1223, 510, 153, 56));
        var found = ocrResult.Any(o => o.Text.Contains("幽境危战"));
        
        // 始终输出日志，帮助调试
        var texts = ocrResult.Any() 
            ? string.Join(", ", ocrResult.Select(o => $"'{o.Text}'"))
            : "（无结果）";
        Logger.LogInformation($"DetectDomainEntrance: 区域(1223,510,153,56) OCR结果=[{texts}], 包含幽境危战={found}");
        
        return found;
    }

    private bool DetectEventMenu(ImageRegion ra)
    {
        // 活动一览位置：左上角(125, 142), 右下角(238, 170)
        // OCR 参数：(x, y, width, height)
        return ra.FindMulti(RecognitionObject.Ocr(125, 142, 238 - 125, 170 - 142))
                   .Any(o => o.Text.Contains("活动一览"));
    }

    private bool DetectStygianOnslaughtPage(ImageRegion ra)
    {
        return ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.55, ra.Height * 0.3, ra.Width * 0.4, ra.Height * 0.6))
                   .Any(o => o.Text.Contains("前往挑战"));
    }

    #endregion

    #region 状态处理器实现 - 每个 case 的行为

    private async Task<StateHandlerResult> HandleMainWorldState(BvPage page)
    {
        Logger.LogInformation($"{Name}：打开活动菜单");
        Simulation.SendInput.SimulateAction(GIActions.OpenTheEventsMenu);
        await Delay(500, _ct);
        return StateHandlerResult.Success; // 等待转换到 EventMenu 或 StygianOnslaughtPage
    }

    private async Task<StateHandlerResult> HandleEventMenuState(BvPage page)
    {
        Logger.LogInformation($"{Name}：在活动菜单中查找幽境危战");

        // 列表区域：左上角(195, 201), 右下角(491, 855)，基于 1080P
        var listCenterX = (195 + 491) / 2;  // 343
        var listCenterY = (201 + 855) / 2;  // 528
        var listRegion = new Rect(195, 201, 491 - 195, 855 - 201);

        // 最多尝试两次（先往下滑动搜索，如果没找到再往上滑动搜索）
        for (int attempt = 0; attempt < 2; attempt++)
        {
            // 1. 拖动滑动列表
            page.Click(listCenterX, listCenterY - 200);
            await Delay(100, _ct);
            Simulation.SendInput.Mouse.LeftButtonDown();
            await Delay(100, _ct);

            // 从上往下拖动（内容往上滚动）
            for (int y = listCenterY - 200; y < listCenterY + 200; y += 50)
            {
                GameCaptureRegion.GameRegion1080PPosMove(listCenterX, y);
                await Delay(30, _ct);
            }

            Simulation.SendInput.Mouse.LeftButtonUp();
            await Delay(500, _ct);

            // 2. 在列表区域内查找"幽境危战"并点击
            var listItem = page.GetByText("幽境危战").WithRoi(listRegion);
            if (listItem.IsExist())
            {
                listItem.FindAll().FirstOrDefault()?.Click();
                await Delay(300, _ct);
                return StateHandlerResult.Success; // 等待转换到 StygianOnslaughtPage
            }

            Logger.LogInformation($"{Name}：第 {attempt + 1} 次未找到幽境危战，尝试反向滑动");
        }

        // 如果两次都没找到，可能"幽境危战"已经被选中，直接尝试检测下一状态
        Logger.LogWarning($"{Name}：未找到幽境危战，可能已被选中，尝试检测 StygianOnslaughtPage");
        page.GetByText("幽境危战").WithRoi(listRegion).FindAll().FirstOrDefault()?.Click();
        await Delay(300, _ct);
        return StateHandlerResult.Success; // 等待转换到 StygianOnslaughtPage
    }

    private async Task<StateHandlerResult> HandleStygianOnslaughtPageState(BvPage page)
    {
        Logger.LogInformation($"{Name}：点击前往挑战");
        page.GetByText("前往挑战").WithRoi(r => r.CutRight(0.5)).FindAll().FirstOrDefault()?.Click();
        await Delay(300, _ct);
        return StateHandlerResult.Success; // 等待转换到 TeleportMap 或 DomainEntrance
    }

    private async Task<StateHandlerResult> HandleTeleportMapState(BvPage page)
    {
        Logger.LogInformation($"{Name}：点击传送");
        page.Locator(QuickTeleportAssets.Instance.TeleportButtonRo).FindAll().FirstOrDefault()?.Click();
        await Delay(300, _ct);
        return StateHandlerResult.Success; // 等待转换到 DomainEntrance
    }

    private async Task<StateHandlerResult> HandleDomainEntranceState(BvPage page)
    {
        Logger.LogInformation($"{Name}：交互秘境入口");
        Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract);
        await Delay(500, _ct);
        return StateHandlerResult.Success; // 等待转换到 DifficultySelect
    }

    private async Task<StateHandlerResult> HandleDifficultySelectState(BvPage page)
    {
        Logger.LogInformation($"{Name}：选择困难难度并进入");

        // 切换到困难模式
        await SwitchToHardModeLoop(page);

        // 点击确认进入
        using var ra = CaptureToRectArea();
        var btn = ra.Find(ElementAssets.Instance.BtnWhiteConfirm);
        btn.Click();
        await Delay(300, _ct);
        return StateHandlerResult.Success; // 等待转换到 DomainLobby
    }

    private async Task<StateHandlerResult> HandleDomainLobbyState(BvPage page)
    {
        Logger.LogInformation($"{Name}：步行前往钥匙");
        await new WalkToFTask().Start(_ct);
        return StateHandlerResult.Success; // 等待转换到 BossSelect 或 LeylineFlowerPrompt
    }

    private async Task<StateHandlerResult> HandleBossSelectState(BvPage page)
    {
        Logger.LogInformation($"{Name}：选择Boss并开始挑战");

        // 选择Boss
        SelectBoss(page);

        // 切换队伍
        await SwitchTeam(page);

        // 点击开始挑战
        using var ra = CaptureToRectArea();
        Bv.ClickWhiteConfirmButton(ra);
        await Delay(300, _ct);
        return StateHandlerResult.Success; // 等待转换到 BattleArena
    }

    private Task<StateHandlerResult> HandleBattleArenaState(BvPage page)
    {
        // 战斗场地已准备就绪，无需额外操作
        // 状态机会检测到 BattleArena 是目标状态并退出
        Logger.LogInformation($"{Name}：战斗场地已准备就绪");
        return Task.FromResult(StateHandlerResult.Wait); // 状态机会检测到目标状态并退出
    }

    private async Task<StateHandlerResult> HandleBattleResultWinState(BvPage page)
    {
        Logger.LogInformation($"{Name}：挑战成功，等待返回大厅");
        using var ra = CaptureToRectArea();
        Bv.ClickWhiteCancelButton(ra);
        await Delay(300, _ct);
        return StateHandlerResult.Success; // 等待转换到 DomainLobby
    }

    private async Task<StateHandlerResult> HandleBattleResultLoseState(BvPage page)
    {
        Logger.LogWarning($"{Name}：挑战失败，等待返回Boss选择");
        using var ra = CaptureToRectArea();
        Bv.ClickWhiteConfirmButton(ra);
        await Delay(300, _ct);
        return StateHandlerResult.Success; // 等待转换到 BossSelect
    }

    private async Task<StateHandlerResult> HandleLeylineFlowerState(BvPage page)
    {
        Logger.LogInformation($"{Name}：交互地脉花");
        Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract);
        await Delay(300, _ct);
        return StateHandlerResult.Success; // 等待转换到 ResinSelect
    }

    private async Task<StateHandlerResult> HandleResinSelectState(BvPage page)
    {
        Logger.LogInformation($"{Name}：选择树脂");
        using var ra = CaptureToRectArea();
        await UseResinAndCheckLast(ra);
        return StateHandlerResult.Success; // 等待转换到 ContinueOrExit 或 DomainLobby
    }

    private async Task<StateHandlerResult> HandleContinueOrExitState(BvPage page)
    {
        Logger.LogInformation($"{Name}：处理继续/退出选择");
        using var ra = CaptureToRectArea();

        // 检查是否还有树脂
        var isLastTurn = _resinPriorityListWhenSpecifyUse.Sum(o => o.RemainCount) <= 0;

        if (isLastTurn)
        {
            var exitBtn = ra.Find(AutoFightAssets.Instance.ExitRa);
            if (!exitBtn.IsEmpty())
            {
                exitBtn.Click();
            }
        }
        else
        {
            var confirmBtn = ra.Find(AutoFightAssets.Instance.ConfirmRa);
            if (!confirmBtn.IsEmpty())
            {
                confirmBtn.Click();
                await Delay(60, _ct);
                confirmBtn.Click();
            }
        }
        await Delay(300, _ct);
        return StateHandlerResult.Success; // 等待转换到 BattleArena 或 MainWorld
    }

    private async Task<StateHandlerResult> HandleUnknownState(BvPage page)
    {
        Logger.LogWarning("未知状态，尝试返回主界面");
        await new ReturnMainUiTask().Start(_ct);
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

        for (var round = 0; round < 9999; round++)
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
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            await Delay(200, _ct);
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            await Delay(2000, _ct);

            // 寻找地脉花
            Logger.LogInformation($"{Name}：寻找地脉花");
            await FindAndInteractLeylineFlowerLoop();

            // 处理奖励
            var shouldContinue = await HandleRewardStateMachine();
            if (!shouldContinue)
            {
                Logger.LogInformation($"{Name}：体力耗尽或轮次达标，结束战斗");
                break;
            }

            Notify.Event(NotificationEvent.DomainReward).Success($"{Name}奖励领取");
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

        // 初始化战斗
        var combatScenes = await InitializeCombatScenesLoop();
        var combatCommands = await PrepareForBattleLoop(combatScenes);

        Logger.LogInformation($"{Name}：执行战斗策略");
        await StartFight(combatScenes, combatCommands);
    }

    /// <summary>
    /// 处理奖励状态机
    /// </summary>
    private async Task<bool> HandleRewardStateMachine()
    {
        await Delay(300, _ct);

        // 等待奖励界面（从 LeylineFlowerPrompt 转换到 ResinSelect）
        CurrentState = StygianState.LeylineFlowerPrompt;
        var resinState = await EnsureNextStateTransition(10000);

        if (resinState == StygianState.Unknown)
        {
            Logger.LogWarning("未检测到奖励界面");
            return true;
        }

        using var ra = CaptureToRectArea();
        var textList = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.25, ra.Height * 0.2, ra.Width * 0.5, ra.Height * 0.6));

        // 检查是否无树脂
        if (textList.Any(t => t.Text.Contains("数量不足") || t.Text.Contains("补充原粹树脂")))
        {
            Logger.LogInformation("原粹树脂已用尽");
            return false;
        }

        // 使用树脂
        var isLastTurn = await UseResinAndCheckLast(ra);

        await Delay(1000, _ct);

        // 等待继续/退出界面（从 ResinSelect 转换到 ContinueOrExit 或 DomainLobby）
        CurrentState = StygianState.ResinSelect;
        var continueState = await EnsureNextStateTransition(10000);

        if (continueState == StygianState.ContinueOrExit)
        {
            if (isLastTurn)
            {
                using var ra2 = CaptureToRectArea();
                var exitBtn = ra2.Find(AutoFightAssets.Instance.ExitRa);
                if (!exitBtn.IsEmpty())
                {
                    exitBtn.Click();
                    return false;
                }
            }
            else
            {
                using var ra2 = CaptureToRectArea();
                var confirmBtn = ra2.Find(AutoFightAssets.Instance.ConfirmRa);
                if (!confirmBtn.IsEmpty())
                {
                    confirmBtn.Click();
                    await Delay(60, _ct);
                    confirmBtn.Click();
                }
            }
        }

        return !isLastTurn;
    }

    private async Task<bool> UseResinAndCheckLast(ImageRegion ra)
    {
        bool isLastTurn = false;

        if (!_taskParam.SpecifyResinUse)
        {
            // 自动刷干树脂
            // 识别树脂状况
            var resinStatus = ResinStatus.RecogniseFromRegion(ra, TaskContext.Instance().SystemInfo, OcrFactory.Paddle);
            resinStatus.Print(Logger);

            if (resinStatus is { CondensedResinCount: <= 0, OriginalResinCount: < 20 })
            {
                Logger.LogWarning("树脂不足");
                return true;
            }

            if (resinStatus.CondensedResinCount > 0)
            {
                AutoDomainTask.PressUseResin(ra, "浓缩树脂");
                resinStatus.CondensedResinCount -= 1;
            }
            else if (resinStatus.OriginalResinCount >= 20)
            {
                var (_, num) = AutoDomainTask.PressUseResin(ra, "原粹树脂");
                resinStatus.OriginalResinCount -= num;
            }

            isLastTurn = resinStatus is { CondensedResinCount: <= 0, OriginalResinCount: < 20 };
        }
        else
        {
            var textList = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.25, ra.Height * 0.2, ra.Width * 0.5, ra.Height * 0.6));
            int successCount = 0;

            foreach (var record in _resinPriorityListWhenSpecifyUse)
            {
                if (record.RemainCount > 0)
                {
                    var (success, _) = AutoDomainTask.PressUseResin(textList, record.Name);
                    if (success)
                    {
                        record.RemainCount -= 1;
                        Logger.LogInformation("自动秘境：{Name} 刷取 {Re}/{Max}",
                            record.Name, record.MaxCount - record.RemainCount, record.MaxCount);
                        successCount++;
                        break;
                    }
                }
            }

            isLastTurn = _resinPriorityListWhenSpecifyUse.Sum(o => o.RemainCount) <= 0;

            if (successCount == 0)
            {
                Logger.LogWarning("指定树脂领取次数时，当前可用树脂选项无法满足配置");
                return true;
            }
        }

        return isLastTurn;
    }

    #endregion

    #region 辅助方法

    private void Init()
    {
        LogScreenResolution();
        if (_taskParam.SpecifyResinUse)
        {
            Logger.LogInformation("→ {Text} 指定使用树脂", $"{Name}，");
        }
        else
        {
            Logger.LogInformation("→ {Text} 用尽所有浓缩树脂和原粹树脂后结束", $"{Name}，");
        }
    }

    private void LogScreenResolution()
    {
        var gameScreenSize = SystemControl.GetGameScreenRect(TaskContext.Instance().GameHandle);
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
        var found = await NewRetry.WaitForAction(() =>
        {
            result = new CombatScenes().InitializeTeam(CaptureToRectArea());
            return result.CheckTeamInitialized();
        }, _ct, 10, 500);

        if (!found || result == null)
        {
            throw new Exception("识别队伍角色失败！");
        }
        Logger.LogInformation($"{Name}：队伍初始化成功");
        return result;
    }

    private async Task<List<CombatCommand>> PrepareForBattleLoop(CombatScenes combatScenes)
    {
        var combatCommands = FindCombatScriptAndSwitchAvatar(combatScenes);
        await Delay(1500, _ct);

        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        await Delay(1200, _ct);
        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);

        return combatCommands;
    }

    private List<CombatCommand> FindCombatScriptAndSwitchAvatar(CombatScenes combatScenes)
    {
        var combatCommands = _combatScriptBag.FindCombatScript(combatScenes.GetAvatars());
        var avatar = combatScenes.SelectAvatar(combatCommands[0].Name);
        avatar?.SwitchWithoutCts();
        Sleep(200, _ct);
        return combatCommands;
    }

    private async Task FindAndInteractLeylineFlowerLoop()
    {
        await _lowerHeadThenWalkToTask!.Start(_ct);

        await NewRetry.WaitForAction(() =>
        {
            Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract);
            Sleep(300, _ct);

            using var ra = CaptureToRectArea();
            var ocrList = ra.FindMulti(RecognitionObject.Ocr(ra.Width * 0.25, ra.Height * 0.2, ra.Width * 0.5, ra.Height * 0.6));
            if (ocrList.Any(t => t.Text.Contains("地脉之花")))
            {
                Logger.LogInformation($"{Name}：成功交互地脉花");
                return true;
            }
            return false;
        }, _ct, 10, 300);
    }

    private async Task SwitchToHardModeLoop(BvPage page)
    {
        var found = await NewRetry.WaitForAction(() =>
        {
            // 如果已经在困难模式，直接返回
            if (page.GetByText("困难").WithRoi(r => r.CutRightTop(0.5, 0.2)).IsExist())
            {
                return true;
            }

            // 检测是否在至危挑战模式，点击切换到常规挑战
            var ultimateChallenge = page.GetByText("至危挑战").WithRoi(r => r.CutLeftTop(0.5, 0.2)).FindAll().FirstOrDefault();
            if (ultimateChallenge != null)
            {
                Logger.LogInformation($"{Name}：检测到至危挑战，点击切换到常规挑战");
                ultimateChallenge.Click();
                Sleep(500, _ct);
                return false;
            }

            // 检测常规挑战模式，点击右侧打开难度选择菜单
            var normalChallenge = page.GetByText("常规挑战").WithRoi(r => r.CutLeftTop(0.5, 0.2)).FindAll().FirstOrDefault();
            if (normalChallenge != null)
            {
                Logger.LogInformation($"{Name}：检测到常规挑战，点击打开难度菜单");
                // 点击常规挑战右侧 400 像素处打开难度菜单
                page.Click(normalChallenge.X + normalChallenge.Width + 400, normalChallenge.Y + normalChallenge.Height / 2);
                Sleep(500, _ct);

                // 在难度菜单中查找并点击"困难"
                var hardMode = page.GetByText("困难").FindAll().FirstOrDefault();
                if (hardMode != null)
                {
                    Logger.LogInformation($"{Name}：点击困难模式");
                    hardMode.Click();
                    Sleep(300, _ct);
                }
                return false;
            }

            Sleep(300, _ct);
            return false;
        }, _ct, 10, 500);

        if (found)
        {
            Logger.LogInformation($"{Name}：确认困难模式");
        }
        else
        {
            Logger.LogWarning("切换困难模式失败，继续执行");
        }
    }

    private void SelectBoss(BvPage page)
    {
        Logger.LogInformation($"{Name}：选择BOSS编号{{Text}}", _taskParam.BossNum);

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
                Simulation.ReleaseAllKey();
                Simulation.SendInput.Mouse.LeftButtonUp();
                AutoFightTask.FightStatusFlag = false;
            }
        }, cts.Token);

        var domainEndTask = DomainEndDetectionTask(cts);
        combatTask.Start();
        domainEndTask.Start();
        return Task.WhenAll(combatTask, domainEndTask);
    }

    private Task DomainEndDetectionTask(CancellationTokenSource cts)
    {
        return new Task(async void () =>
        {
            try
            {
                var captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
                var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
                RecognitionObject whiteCancelRo = new RecognitionObject
                {
                    Name = "BtnWhiteCancel",
                    RecognitionType = RecognitionTypes.TemplateMatch,
                    TemplateImageMat = ElementAssets.Instance.BtnWhiteCancel.TemplateImageMat,
                    RegionOfInterest = new Rect(captureRect.Width / 3, captureRect.Height - (int)(captureRect.Height * 0.22), captureRect.Width / 3, (int)(captureRect.Height * 0.22)),
                    Use3Channels = true
                }.InitTemplate();

                await NewRetry.WaitForAction(() =>
                {
                    using var ra = CaptureToRectArea();
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
            Logger.LogInformation($"{Name}：不更换战斗队伍");
            return;
        }

        Logger.LogInformation($"{Name}：配置战斗队伍为：{fightTeamName}");
        await OpenTeamPanelLoop(page);
        await FindAndSelectTeamLoop(page, fightTeamName);
    }

    private async Task OpenTeamPanelLoop(BvPage page)
    {
        var found = await NewRetry.WaitForAction(() =>
        {
            if (page.GetByText("预设队伍").WithRoi(r => r.CutLeftTop(0.15, 0.075)).IsExist())
            {
                return true;
            }

            var teamButton = page.GetByText("预设队伍").WithRoi(r => r.CutRightBottom(0.3, 0.1)).FindAll().FirstOrDefault();
            teamButton?.Click();
            Sleep(300, _ct);
            return false;
        }, _ct);

        if (found)
        {
            Logger.LogInformation($"{Name}：预设队伍面板已打开");
        }
        else
        {
            Logger.LogWarning("未找到预设队伍按钮，不执行切换操作");
        }
    }

    private async Task FindAndSelectTeamLoop(BvPage page, string fightTeamName)
    {
        page.Click(936, 150);
        await Delay(100, _ct);
        Simulation.SendInput.Mouse.LeftButtonDown();
        await Delay(100, _ct);
        GameCaptureRegion.GameRegion1080PPosMove(936, 140);
        await Delay(100, _ct);

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
                    Simulation.SendInput.Mouse.LeftButtonUp();
                    await Delay(200, _ct);

                    for (int j = 0; j < 5; j++)
                    {
                        foundTeam.Click();
                        await Delay(200, _ct);
                    }
                    Logger.LogInformation($"{Name}：已选择队伍 {fightTeamName}");
                    return;
                }

                yOffset += scrollStep;
                if (130 + yOffset > 1080)
                {
                    Logger.LogWarning("未找到预设战斗队伍名称：{TeamName}，保持原有队伍", fightTeamName);
                    break;
                }

                GameCaptureRegion.GameRegion1080PPosMove(936, 130 + yOffset);
                await Delay(200, _ct);
            }
        }
        finally
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
            await Delay(100, _ct);
        }

        Simulation.SendInput.SimulateAction(GIActions.OpenPaimonMenu);
        await Delay(300, _ct);
    }

    private async Task ExitDomain(BvPage page)
    {
        await OpenExitMenuAndClickLoop(page);
        await WaitExitCompleteLoop(page);
    }

    private async Task OpenExitMenuAndClickLoop(BvPage page)
    {
        var found = await NewRetry.WaitForElementAppear(
            ElementAssets.Instance.BtnExitDoor.Value,
            () => Simulation.SendInput.SimulateAction(GIActions.OpenPaimonMenu),
            _ct);

        if (found)
        {
            await page.Locator(ElementAssets.Instance.BtnExitDoor.Value).Click();
            Logger.LogInformation($"{Name}：点击退出秘境");
        }
        else
        {
            Logger.LogWarning("未能找到退出秘境按钮，可能已经退出秘境");
        }
    }

    private async Task WaitExitCompleteLoop(BvPage page)
    {
        var found = await Bv.WaitUntilFound(ElementAssets.Instance.PaimonMenuRo, _ct, 200, 300);
        if (found)
        {
            Logger.LogInformation($"{Name}：退出秘境完成");
            await Delay(1000, _ct);
        }
    }

    private async Task ArtifactSalvage()
    {
        if (!_taskParam.AutoArtifactSalvage)
        {
            return;
        }

        if (!int.TryParse(TaskContext.Instance().Config.AutoArtifactSalvageConfig.MaxArtifactStar, out var star))
        {
            star = 4;
        }

        await new AutoArtifactSalvageTask(new AutoArtifactSalvageTaskParam(star, javaScript: null, artifactSetFilter: null, maxNumToCheck: null, recognitionFailurePolicy: null)).Start(_ct);
    }

    #endregion
}
