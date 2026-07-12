using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.GameTask.Common.Job;
using OpenCvSharp;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoPathing.Model;

namespace BetterGenshinImpact.GameTask.AutoFight;

public class AutoFightJsonTask : ISoloTask
{
    public string Name => "自动战斗(JSON策略)";

    private readonly AutoFightParam _taskParam;
    private readonly JsonCombatStrategy _strategy;
    private CancellationToken _ct;

    /// <summary>
    /// YOLO目标检测器（BgiWorld模型），用于战斗结束检测
    /// 当前未使用（战斗结束检测已委托到 AutoFightEndDetection），保留声明以与 TXT 策略保持一致
    /// 初始化条件：_taskParam.FightFinishDetectEnabled == true
    /// </summary>
    private readonly BgiYoloPredictor? _predictor;
    private DateTime _lastFightFlagTime = DateTime.Now;

    private readonly ReturnMainUiTask _returnMainUiTask = new();
    private readonly double _assetScale = TaskContext.Instance().SystemInfo.AssetScale;
    private readonly double _dpi = TaskContext.Instance().DpiScale;

    private static readonly object PickLock = new object();

    /// <summary>
    /// 当前队伍中的角色名集合（用于过滤动作节点）
    /// </summary>
    private HashSet<string> _teamCharacterNames = new(StringComparer.OrdinalIgnoreCase);

    // 日志防刷：1秒内同一动作名至多输出一次日志
    private string _lastLoggedActionName = "";
    private DateTime _lastLogTime = DateTime.MinValue;

    /// <summary>
    /// 展开后的优先级动作条目
    /// 每个 JsonAction 展开为 1+N 个条目（1个主条件 + N个 morePriorities）
    /// </summary>
    private class PrioritizedAction
    {
        public JsonAction Action { get; set; }
        public string Expression { get; set; }
        public int Priority { get; set; }
    }

    // 战斗点位
    public static WaypointForTrack? FightWaypoint { get; set; } = null;

    private TaskFightFinishDetectConfig _finishDetectConfig;

    private class TaskFightFinishDetectConfig
    {
        public int DelayTime = 1500;
        public int DetectDelayTime = 450;
        public Dictionary<string, int> DelayTimes = new();
        public double CheckTime = 5;
        public List<string> CheckNames = new();
        public bool FastCheckEnabled;
        public bool RotateFindEnemyEnabled = false;

        public (int, int, int) BattleEndProgressBarColor { get; }
        public (int, int, int) BattleEndProgressBarColorTolerance { get; }

        public TaskFightFinishDetectConfig(AutoFightParam.FightFinishDetectConfig finishDetectConfig)
        {
            FastCheckEnabled = finishDetectConfig.FastCheckEnabled;
            ParseCheckTimeString(finishDetectConfig.FastCheckParams, out CheckTime, CheckNames);
            ParseFastCheckEndDelayString(finishDetectConfig.CheckEndDelay, out DelayTime, DelayTimes);
            BattleEndProgressBarColor =
                ParseStringToTuple(finishDetectConfig.BattleEndProgressBarColor, (95, 235, 255));
            BattleEndProgressBarColorTolerance =
                ParseSingleOrCommaSeparated(finishDetectConfig.BattleEndProgressBarColorTolerance, (6, 6, 6));
            DetectDelayTime =
                (int)((double.TryParse(finishDetectConfig.BeforeDetectDelay, out var result) ? result : 0.45) * 1000);
            RotateFindEnemyEnabled = finishDetectConfig.RotateFindEnemyEnabled;
        }

        public static void ParseCheckTimeString(
            string input,
            out double checkTime,
            List<string> names)
        {
            checkTime = 5;
            if (string.IsNullOrEmpty(input))
            {
                return;
            }

            var uniqueNames = new HashSet<string>();

            var segments = input.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
            {
                var trimmedSegment = segment.Trim();

                if (double.TryParse(trimmedSegment, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out double number))
                {
                    checkTime = number;
                }
                else if (!uniqueNames.Contains(trimmedSegment))
                {
                    uniqueNames.Add(trimmedSegment);
                }
            }

            names.AddRange(uniqueNames);
        }

        public static void ParseFastCheckEndDelayString(
            string input,
            out int delayTime,
            Dictionary<string, int> nameDelayMap)
        {
            delayTime = 1500;

            if (string.IsNullOrEmpty(input))
            {
                return;
            }

            var segments = input.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
            {
                var parts = segment.Split(',');

                if (parts.Length == 1)
                {
                    if (double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double number))
                    {
                        delayTime = (int)(number * 1000);
                    }
                }
                else if (parts.Length == 2)
                {
                    string name = parts[0].Trim();
                    if (double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double value))
                    {
                        nameDelayMap[name] = (int)(value * 1000);
                    }
                }
            }
        }

        static bool IsSingleNumber(string input, out int result)
        {
            return int.TryParse(input, out result);
        }

        static (int, int, int) ParseSingleOrCommaSeparated(string input, (int, int, int) defaultValue)
        {
            if (IsSingleNumber(input, out var singleNumber))
            {
                return (singleNumber, singleNumber, singleNumber);
            }

            return ParseStringToTuple(input, defaultValue);
        }

        static (int, int, int) ParseStringToTuple(string input, (int, int, int) defaultValue)
        {
            var parts = input.Split(',');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], out var num1) &&
                int.TryParse(parts[1], out var num2) &&
                int.TryParse(parts[2], out var num3))
            {
                return (num1, num2, num3);
            }

            return defaultValue;
        }
    }

    public AutoFightJsonTask(AutoFightParam taskParam)
    {
        _taskParam = taskParam;
        _strategy = JsonCombatStrategyParser.ParseFile(_taskParam.CombatStrategyPath);

        if (_taskParam.FightFinishDetectEnabled)
        {
            _predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiWorld);
        }

        _finishDetectConfig = new TaskFightFinishDetectConfig(_taskParam.FinishDetectConfig);
    }

    /// <summary>
    /// 获取战斗场景，带重试机制
    /// 最多重试 5 次，每次间隔 1 秒
    /// </summary>
    /// <returns>初始化完成的战斗场景</returns>
    public CombatScenes GetCombatScenesWithRetry()
    {
        const int maxRetries = 5;
        var retryDelayMs = 1000;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
            if (combatScenes.CheckTeamInitialized())
            {
                return combatScenes;
            }

            if (attempt < maxRetries)
            {
                Thread.Sleep(retryDelayMs);
            }
        }
        throw new Exception("识别队伍角色失败（已重试 5 次）");
    }

    /// <summary>
    /// 启动自动战斗（JSON策略模式）
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task Start(CancellationToken ct)
    {
        _ct = ct;

        LogScreenResolution();
        var combatScenes = GetCombatScenesWithRetry();

        // 收集当前队伍角色名
        foreach (var avatar in combatScenes.GetAvatars())
        {
            _teamCharacterNames.Add(avatar.Name);
        }
        Logger.LogInformation("JSON 策略：当前队伍角色：{Names}", string.Join(", ", _teamCharacterNames));

        // 过滤可用动作：Character 为空（通用）或在当前队伍中
        var filteredActions = _strategy.Actions
            .Where(a => string.IsNullOrEmpty(a.Character) || _teamCharacterNames.Contains(a.Character))
            .ToList();

        // 展开为优先级条目：每个动作产生 1个主条目 + N个 morePriorities 条目
        var validActions = new List<PrioritizedAction>();
        foreach (var action in filteredActions)
        {
            validActions.Add(new PrioritizedAction
            {
                Action = action,
                Expression = action.Condition.Expression,
                Priority = action.Index
            });

            foreach (var morePriority in action.MorePriorities)
            {
                validActions.Add(new PrioritizedAction
                {
                    Action = action,
                    Expression = morePriority.Expression,
                    Priority = morePriority.Priority
                });
            }
        }

        // 按优先级排序，相同优先级时原动作排在 morePriorities 之前（通过索引辅助排序）
        validActions = validActions
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.Expression == p.Action.Condition.Expression ? 0 : 1)
            .ToList();

        Logger.LogInformation("JSON 策略：共 {Total} 个动作，展开为 {Expanded} 个优先级条目",
            _strategy.Actions.Count, validActions.Count);

        if (validActions.Count == 0)
        {
            Logger.LogWarning("JSON 策略：没有可用的动作节点，跳过战斗");
            return;
        }

        // 新的取消token
        var cts2 = new CancellationTokenSource();
        ct.Register(cts2.Cancel);

        combatScenes.BeforeTask(cts2.Token);
        // 设置初始当前角色名（用于无 Character 字段的通用 action 回退）
        CombatScriptParser.CurrentAvatarName = combatScenes.GetAvatars().FirstOrDefault()?.Name ?? CombatScriptParser.CurrentAvatarName;
        TimeSpan fightTimeout = TimeSpan.FromSeconds(_taskParam.Timeout);
        Stopwatch timeoutStopwatch = Stopwatch.StartNew();

        AutoFightSeek.RotationCount = 0;
        AutoFightTask.FightStatusFlag = true;

        var fightEndFlag = false;
        var timeOutFlag = false;
        string lastFightName = "";

        // 初始化条件求值器
        var evaluator = new ConditionEvaluator(combatScenes, () => CaptureToRectArea());

        // 基于经验值的战后拾取检测
        ExperienceDetector? expDetector = null;
        if (_taskParam.KazuhaPickupEnabled && _taskParam.ExpBasedPickupEnabled)
        {
            using var gameCaptureRegion = CaptureToRectArea();
            var expRos = AutoFightAssets.Get(gameCaptureRegion).ExperienceRecognitionObjects;
            expDetector = new ExperienceDetector(expRos, cts2.Token);
            expDetector.Start();
        }

        // 战斗前动作
        await RunPreActions(combatScenes, evaluator);

        // 战斗操作
        var fightTask = Task.Run(async () =>
        {
            try
            {
                JsonAction? lastExecutedAction = null;

                while (!cts2.Token.IsCancellationRequested)
                {
                    if (timeoutStopwatch.Elapsed > fightTimeout)
                    {
                        Logger.LogInformation("战斗超时结束");
                        fightEndFlag = true;
                        timeOutFlag = true;
                        break;
                    }

                    // 每次循环开始：截图一次，供所有条件求值复用
                    using var capture = CaptureToRectArea();
                    evaluator.SetCachedCapture(capture);

                    var anyExecuted = false;

                    foreach (var prioritizedAction in validActions)
                        {
                            if (cts2.Token.IsCancellationRequested) break;

                            var action = prioritizedAction.Action;

                            // 求值条件表达式（使用展开后的表达式和优先级）
                            var conditionMet = evaluator.Evaluate(
                                prioritizedAction.Expression,
                                prioritizedAction.Priority,
                                action.Character);

                            if (!conditionMet)
                            {
                                continue;
                            }

                            // 指定角色的动作：执行前确保切换到该角色
                            if (!string.IsNullOrEmpty(action.Character))
                            {
                                var avatar = combatScenes.SelectAvatar(action.Character);
                                if (avatar == null) continue;

                                avatar.Switch();
                                CombatScriptParser.CurrentAvatarName = action.Character;
                            }

                            // 执行动作
                            await ExecuteAction(combatScenes, action);

                            // 确保E技能释放成功
                            if (action.EnsureCast)
                            {
                                var characterName = string.IsNullOrEmpty(action.Character)
                                    ? CombatScriptParser.CurrentAvatarName
                                    : action.Character;
                                var avatar = combatScenes.SelectAvatar(characterName);
                                if (avatar != null)
                                {
                                    var imageAfterAction = CaptureToRectArea();
                                    var retry = 5;
                                    while (!(await AutoFightSkill.AvatarSkillAsync(Logger, avatar, false, 1, _ct, imageAfterAction)) && retry > 0)
                                    {
                                        Logger.LogWarning("{Name} 未检测到技能冷却，重新执行", action.Name);
                                        // 防止在纳塔飞天或爬墙
                                        Simulation.ReleaseAllKey();
                                        Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
                                        Simulation.SendInput.SimulateAction(GIActions.Drop);
                                        await Delay(200, _ct);
                                        // 重新执行整个动作
                                        await ExecuteAction(combatScenes, action);
                                        imageAfterAction = CaptureToRectArea();
                                        await Task.Delay(30, _ct);
                                        retry--;
                                    }
                                    imageAfterAction.Dispose();
                                }
                            }

                            evaluator.UpdateLastExecTime(prioritizedAction.Priority);
                            lastExecutedAction = action;
                            anyExecuted = true;
                            lastFightName = action.Character ?? "";

                            if (_fightEndFlag) break;

                            // 执行完第一个满足条件的动作后重新判断
                            break;
                        }

                    if (fightEndFlag || _fightEndFlag) break;

                    if (!anyExecuted)
                    {
                        await Delay(200, _ct);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                Debug.WriteLine(e.StackTrace);
                throw;
            }
            finally
            {
                Simulation.ReleaseAllKey();
                AutoFightTask.FightStatusFlag = false;
            }
        }, cts2.Token);

        await fightTask;

        try
        {
            // 基于经验值检测结果的拾取判断
            if (_taskParam.KazuhaPickupEnabled && _taskParam.ExpBasedPickupEnabled && expDetector != null)
            {
                if (!expDetector.HasDetectedExperience)
                {
                    Logger.LogInformation("基于经验值判断：等待经验值检测结果");
                    var waitMs = 1100;
                    while (!expDetector.HasDetectedExperience && waitMs > 0)
                    {
                        await Delay(100, _ct);
                        waitMs -= 100;
                    }
                }

                var shouldPickup = expDetector.HasDetectedExperience;
                Logger.LogInformation("基于经验值判断：{Result} 战后拾取", shouldPickup ? "执行" : "不执行");

                if (!shouldPickup)
                {
                    if (_taskParam is { PickDropsAfterFightEnabled: true })
                    {
                        await new ScanPickTask().Start(_ct);
                    }
                    return;
                }
            }
        }
        finally
        {
            if (expDetector != null)
            {
                await expDetector.StopAsync();
                expDetector.Dispose();
            }
        }

        // 战后拾取（完全参照 AutoFightTask）
        await PostFightPickup(combatScenes, timeOutFlag, lastFightName);
    }

    private bool _fightEndFlag;

    /// <summary>执行单个 JSON 动作节点</summary>
    private async Task ExecuteAction(CombatScenes combatScenes, JsonAction action)
    {
        try
        {
            var character = string.IsNullOrEmpty(action.Character)
                ? CombatScriptParser.CurrentAvatarName
                : action.Character;

            var commands = CombatScriptParser.ParseLinePart(action.Action, character);

            // 执行前输出日志
            LogActionOnce(action.Name);

            CombatCommand? lastSubCmd = null;
            foreach (var cmd in commands)
            {
                if (_ct.IsCancellationRequested) break;

                cmd.Execute(combatScenes, lastSubCmd);
                lastSubCmd = cmd;

                if (_fightEndFlag) break;

                // 仅由 check 指令触发战斗结束检测
                if (cmd.Method == Method.Check && _taskParam.FightFinishDetectEnabled)
                {
                    _fightEndFlag = await CheckFightFinish(_finishDetectConfig.DelayTime, _finishDetectConfig.DetectDelayTime);
                    if (_fightEndFlag)
                    {
                        Logger.LogInformation("{Name} 检测到战斗结束", action.Name);
                        break;
                    }
                }
            }

            // 更新当前角色名，供后续无指定角色动作使用
            CombatScriptParser.CurrentAvatarName = character;
        }
        catch (Exception e)
        {
            Logger.LogError("自动战斗：{Name} 执行失败：{Msg}", action.Name, e.Message);
        }
        finally
        {
            Simulation.ReleaseAllKey();
        }
    }

    /// <summary>战斗结束检测</summary>
    private async Task<bool> CheckFightFinish(int delayTime = 1500, int detectDelayTime = 450)
    {
        if (_finishDetectConfig.RotateFindEnemyEnabled)
        {
            bool? result = null;
            try
            {
                result = await AutoFightSeek.SeekAndFightAsync(Logger, detectDelayTime, delayTime, _ct);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "SeekAndFightAsync 方法发生异常");
                result = false;
            }

            AutoFightSeek.RotationCount = (result == null) ? AutoFightSeek.RotationCount + 1 : 0;

            if (result != null)
            {
                return result.Value;
            }
        }

        if (!_finishDetectConfig.RotateFindEnemyEnabled) await Delay(delayTime, _ct);

        Logger.LogInformation("打开编队界面检查战斗是否结束");
        Simulation.SendInput.SimulateAction(GIActions.OpenPartySetupScreen);
        await Delay(detectDelayTime, _ct);

        using var ra = CaptureToRectArea();
        // 注意：像素坐标 (50, 790) 和 (50, 768) 是硬编码的，未做分辨率缩放
        // 与 TXT 版本逻辑保持一致，不进行缩放
        var b3 = ra.SrcMat.At<Vec3b>(50, 790); //进度条颜色
        var whiteTile = ra.SrcMat.At<Vec3b>(50, 768); //白块
        Simulation.SendInput.SimulateAction(GIActions.Drop);

        if (IsWhite(whiteTile.Item2, whiteTile.Item1, whiteTile.Item0) &&
            IsYellow(b3.Item2, b3.Item1, b3.Item0))
        {
            Logger.LogInformation("识别到战斗结束");
            Simulation.SendInput.SimulateAction(GIActions.OpenPartySetupScreen);
            return true;
        }

        Logger.LogInformation($"未识别到战斗结束: yellow{b3.Item0},{b3.Item1},{b3.Item2};white{whiteTile.Item0},{whiteTile.Item1},{whiteTile.Item2}");

        if (_finishDetectConfig.RotateFindEnemyEnabled)
        {
            // 注意：此处使用 await 确保异常能被正确捕获
            // TXT 版本的 AutoFightTask.CheckFightFinish 中未使用 await，异常可能被吞掉
            Task.Run(async () =>
            {
                try
                {
                    var bloodLower = new Scalar(255, 90, 90);
                    await MoveForwardTask.MoveForwardAsync(bloodLower, bloodLower, Logger, _ct);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("MoveForwardAsync 异常：{Msg}", ex.Message);
                }
            }, _ct);
        }

        _lastFightFlagTime = DateTime.Now;
        return false;
    }

    private bool IsYellow(int r, int g, int b)
    {
        return (r >= 200 && r <= 255) &&
               (g >= 200 && g <= 255) &&
               (b >= 0 && b <= 100);
    }

    private bool IsWhite(int r, int g, int b)
    {
        return (r >= 240 && r <= 255) &&
               (g >= 240 && g <= 255) &&
               (b >= 240 && b <= 255);
    }

    /// <summary>日志防刷：同一动作名在1秒内至多输出一次日志</summary>
    private void LogActionOnce(string actionName)
    {
        if (actionName == _lastLoggedActionName && (DateTime.Now - _lastLogTime).TotalSeconds < 1)
        {
            return;
        }
        _lastLoggedActionName = actionName;
        _lastLogTime = DateTime.Now;
        Logger.LogInformation("自动战斗：{Name}", actionName);
    }

    /// <summary>执行战斗前动作</summary>
    private async Task RunPreActions(CombatScenes combatScenes, ConditionEvaluator evaluator)
    {
        if (_strategy.Info.PreActions == null || _strategy.Info.PreActions.Count == 0)
            return;

        Logger.LogInformation("JSON 策略：执行战斗前动作");
        using var capture = CaptureToRectArea();
        evaluator.SetCachedCapture(capture);

        foreach (var preAction in _strategy.Info.PreActions)
        {
            if (_ct.IsCancellationRequested) break;

            try
            {
                var firstSpaceIndex = preAction.IndexOf(' ');
                var character = CombatScriptParser.CurrentAvatarName;
                var commands = preAction;
                if (firstSpaceIndex > 0)
                {
                    character = preAction[..firstSpaceIndex];
                    commands = preAction[(firstSpaceIndex + 1)..];
                }

                var cmdList = CombatScriptParser.ParseLineCommands(commands, character);
                foreach (var cmd in cmdList)
                {
                    if (_ct.IsCancellationRequested) break;
                    cmd.Execute(combatScenes);
                    await Delay(300, _ct);
                }

                Logger.LogInformation("战斗前动作：{Action}", preAction);
            }
            catch (Exception e)
            {
                Logger.LogWarning("战斗前动作执行失败：{Action}，{Msg}", preAction, e.Message);
            }
        }
    }

    /// <summary>战后拾取</summary>
    private async Task PostFightPickup(CombatScenes combatScenes, bool timeOutFlag, string lastFightName)
    {
        if (_taskParam.KazuhaPickupEnabled)
        {
            var picker = combatScenes.SelectAvatar("枫原万叶") ?? combatScenes.SelectAvatar("琴");

            string? oldPartyName = null;
            if (RunnerContext.Instance.PartyName is not null)
            {
                oldPartyName = RunnerContext.Instance.PartyName;
            }
            else if (picker is null && !string.IsNullOrEmpty(_taskParam.KazuhaPartyName))
            {
                Logger.LogWarning("换队拾取：当前队伍名称为空，尝试读取！");
                await Delay(1000, _ct);
                await _returnMainUiTask.Start(_ct);

                for (int attempt = 0; attempt < 6; attempt++)
                {
                    Simulation.SendInput.SimulateAction(GIActions.OpenPartySetupScreen);
                    var enterGameAppear = await NewRetry.WaitForElementAppear(
                        ElementRecognition.Get("PartyBtnChooseView"),
                        () => { },
                        _ct,
                        15,
                        500
                    );
                    if (attempt == 5 && !enterGameAppear)
                    {
                        Logger.LogWarning("换队拾取：读取队伍名称失败，跳过换队拾取步骤");
                        return;
                    }
                }
            }

            if (!string.IsNullOrEmpty(_taskParam.KazuhaPartyName))
            {
                await Delay(1000, _ct);

                var timeWaitStart = 0;
                while (timeWaitStart < 6000)
                {
                    using var ra = CaptureToRectArea();
                    var partyViewBtn = ra.Find(ElementRecognition.Get("PartyBtnChooseView"));
                    if (partyViewBtn.IsExist())
                    {
                        var rawPartyName = ra.Find(new RecognitionObject
                        {
                            RecognitionType = RecognitionTypes.Ocr,
                            RegionOfInterest = new Rect(partyViewBtn.Right, partyViewBtn.Top, (int)(350 * _assetScale),
                                partyViewBtn.Height)
                        }).Text;

                        if (string.IsNullOrWhiteSpace(rawPartyName))
                        {
                            oldPartyName = string.Empty;
                        }
                        else
                        {
                            var tempName = rawPartyName
                                .Replace("\"", "")
                                .Replace("\r\n", "")
                                .Replace("\r", "");

                            int firstNewLineIndex = tempName.IndexOf('\n');
                            if (firstNewLineIndex != -1)
                            {
                                tempName = tempName.Substring(0, firstNewLineIndex);
                            }

                            oldPartyName = tempName.Trim();
                        }

                        Logger.LogInformation("换队拾取：当前队伍名称读取为：{oldPartyName}", oldPartyName);
                        Logger.LogDebug("OCR原始识别文本（含转义）：{rawPartyName}", rawPartyName);
                        RunnerContext.Instance.PartyName = oldPartyName;
                        break;
                    }
                    await Delay(200, _ct);
                    timeWaitStart += 200;
                }
            }

            var switchPartyFlag = false;
            if (picker == null && !timeOutFlag && !string.IsNullOrEmpty(_taskParam.KazuhaPartyName) && oldPartyName != _taskParam.KazuhaPartyName)
            {
                try
                {
                    Logger.LogInformation($"切换为拾取队伍：{_taskParam.KazuhaPartyName}");
                    var success = await new SwitchPartyTask().Start(_taskParam.KazuhaPartyName, _ct);
                    if (success)
                    {
                        Logger.LogInformation($"成功切换队伍为{_taskParam.KazuhaPartyName}");
                        switchPartyFlag = true;
                        RunnerContext.Instance.PartyName = _taskParam.KazuhaPartyName;
                        RunnerContext.Instance.ClearCombatScenes();
                        var cs = await RunnerContext.Instance.GetCombatScenes(_ct);
                        picker = cs.SelectAvatar("枫原万叶") ?? cs.SelectAvatar("琴");
                    }
                }
                catch (Exception e)
                {
                    Logger.LogWarning("切换队伍异常，跳过此步骤！{Msg}", e.Message);
                }
            }

            if (picker != null)
            {
                if (picker.Name == "枫原万叶")
                {
                    var time = TimeSpan.FromSeconds(picker.GetSkillCdSeconds());

                    bool shouldSkip = lastFightName == picker.Name && time.TotalSeconds > 3;
                    bool forcePickup = _taskParam.QinDoublePickUp;

                    if (forcePickup || !shouldSkip)
                    {
                        Logger.LogInformation("使用 枫原万叶-长E 拾取掉落物");
                        await Delay(200, _ct);
                        if (picker.TrySwitch(10))
                        {
                            await picker.WaitSkillCd(_ct);
                            await SimulateHoldElementalSkillAsync(800, _ct);
                            await SimulateMouseLeftClickLoopAsync(6, _ct);
                            await Delay(1500, _ct);
                            picker.AfterUseSkill();
                        }
                    }
                    else
                    {
                        Logger.LogInformation("距最近一次万叶出招，时间过短，跳过此次万叶拾取！");
                    }
                }
                else if (picker.Name == "琴")
                {
                    Logger.LogInformation("使用 琴-长E 拾取掉落物");

                    var actionsToUse = PickUpCollectHandler.PickUpActions
                        .Where(action => action.StartsWith("琴-长E" + " ", StringComparison.OrdinalIgnoreCase))
                        .Select(action => action.Replace("琴-长E", "琴", StringComparison.OrdinalIgnoreCase))
                        .ToArray();

                    var find = _taskParam.QinDoublePickUp;
                    await Delay(150, _ct);
                    if (picker.TrySwitch(10))
                    {
                        foreach (var miningActionStr in actionsToUse)
                        {
                            var pickUpAction = CombatScriptParser.ParseContext(miningActionStr);

                            for (int i = 0; i < 2; i++)
                            {
                                await picker.WaitSkillCd(_ct);
                                foreach (var command in pickUpAction.CombatCommands)
                                {
                                    command.Execute(combatScenes);
                                    Task.Run(() =>
                                    {
                                        if (Monitor.TryEnter(PickLock))
                                        {
                                            try
                                            {
                                                if (find)
                                                {
                                                    using (var imagePick = CaptureToRectArea())
                                                    {
                                                        if (imagePick.Find(AutoPickAssets.Get(imagePick, TaskContext.Instance().Config.AutoPickConfig.PickKey).PickRo).IsExist())
                                                        {
                                                            find = false;
                                                        }
                                                    }
                                                }
                                            }
                                            finally
                                            {
                                                Monitor.Exit(PickLock);
                                            }
                                        }
                                    });
                                }

                                if (!find)
                                {
                                    break;
                                }

                                if (i == 0)
                                {
                                    Logger.LogInformation("自动拾取；尝试再次执行 琴-长E 拾取");
                                    picker.AfterUseSkill();
                                }
                                else
                                {
                                    break;
                                }
                            }

                            Simulation.ReleaseAllKey();
                        }
                    }
                }
            }

            if (switchPartyFlag && !string.IsNullOrEmpty(oldPartyName))
            {
                try
                {
                    Logger.LogInformation($"切换为原队伍：{oldPartyName}");
                    var success = await new SwitchPartyTask().Start(oldPartyName, _ct);
                    if (success)
                    {
                        Logger.LogInformation($"切换为原队伍{oldPartyName}");
                        switchPartyFlag = true;
                        RunnerContext.Instance.PartyName = oldPartyName;
                        RunnerContext.Instance.ClearCombatScenes();
                        await RunnerContext.Instance.GetCombatScenes(_ct);
                    }
                }
                catch (Exception e)
                {
                    Logger.LogWarning("恢复原队伍失败，跳过此步骤！{Msg}", e.Message);
                }
            }
        }

        if (_taskParam is { PickDropsAfterFightEnabled: true })
        {
            await new ScanPickTask().Start(_ct);
        }
    }

    /// <summary>
    /// 检查并记录屏幕分辨率
    /// </summary>
    private void LogScreenResolution()
    {
        AssertUtils.CheckGameResolution("自动战斗");
    }
}
