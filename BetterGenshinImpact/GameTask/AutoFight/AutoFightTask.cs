using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using OpenCvSharp;
using BetterGenshinImpact.Helpers;
using Vanara;
using Microsoft.Extensions.DependencyInjection;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Handler;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.SoloTask;

namespace BetterGenshinImpact.GameTask.AutoFight;

/// <summary>
/// 自动战斗（txt 策略实现）。不直接出现在任务菜单中，由 <see cref="AutoFightEntryTask"/> 或其他任务按策略类型选择调用
/// </summary>
public class AutoFightTask : ISoloTask<AutoFightParam, NoData>
{
    /// <summary>
    /// 本次运行的战斗参数，在 RunAsync 开头赋值
    /// </summary>
    private AutoFightParam _taskParam = null!;

    /// <summary>
    /// 按参数中的策略路径解析得到的战斗脚本集合，在 RunAsync 开头赋值
    /// </summary>
    private CombatScriptBag _combatScriptBag = null!;

    private CancellationToken _ct;

    private BgiYoloPredictor? _predictor;

    private static DateTime _lastFightFlagTime = DateTime.Now; // 战斗标志最近一次出现的时间
    private static int _skipCheckCounter;

    /// <summary>
    /// 重置"敌人可见时跳过战斗结束检查"的连续跳过计数（每场战斗开始时调用，TXT 与 JSON 策略共用）
    /// </summary>
    public static void ResetSkipCheckCounter() => _skipCheckCounter = 0;

    /// <summary>
    /// 本次战斗所在的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 构造时取的 DPI 快照，运行环境生命周期内不变
    /// </summary>
    private readonly double _dpi;
    
    public static bool FightStatusFlag { get; set; } = false;
    
    private static readonly object PickLock = new object(); 
    
    /// <summary>
    /// 构造时取的素材缩放比例快照，运行环境生命周期内不变
    /// </summary>
    private readonly double _assetScale;
    
    private readonly ReturnMainUiTask _returnMainUiTask;

    // 战斗点位
    public static WaypointForTrack? FightWaypoint  {get; set;} = null;
    
    /// <summary>
    /// 最近一次战斗结束检查的时间（TXT 与 JSON 策略共用，供更快触发战斗结束检查判断间隔使用）
    /// </summary>
    public static DateTime LastFightFinishCheckTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 本次战斗的开战时间（TXT 与 JSON 策略共用，供"开战后一段时间阻断战斗结束检查"使用）
    /// </summary>
    public static DateTime FightStartTime { get; set; } = DateTime.Now;
    
    public class TaskFightFinishDetectConfig
    {
        public int DelayTime = 1500;
        public int DetectDelayTime = 450;
        public Dictionary<string, int> DelayTimes = new();
        public double CheckTime = 5;
        public List<string> CheckNames = new();
        public bool FastCheckEnabled;
        public bool CheckAfterSwitchAvatar = false;
        public bool RotateFindEnemyEnabled = false;
        public bool SkipFightEndCheckWhenEnemyVisible = false;
        public double BlockCheckBeforeBattleSeconds = 0;
        public bool PaimonEndCheckEnabled = true;
        public int PaimonEndCheckDelayMs = 75;
        public int RotaryFactor = 6;

        // 保留仅传入结束检测配置的构造方式，兼容不持有完整 AutoFightParam 的调用方。
        // 此路径沿用 RotaryFactor 的默认值；持有完整参数时由下方构造函数覆盖为用户配置值。
        public TaskFightFinishDetectConfig(AutoFightParam.FightFinishDetectConfig finishDetectConfig)
        {
            FastCheckEnabled = finishDetectConfig.FastCheckEnabled;
            CheckAfterSwitchAvatar = finishDetectConfig.CheckAfterSwitchAvatar;
            ParseCheckTimeString(finishDetectConfig.FastCheckParams, out CheckTime, CheckNames);
            ParseFastCheckEndDelayString(finishDetectConfig.CheckEndDelay, out DelayTime, DelayTimes);
            DetectDelayTime =
                (int)((double.TryParse(finishDetectConfig.BeforeDetectDelay, out var result) ? result : 0.45) * 1000);
            RotateFindEnemyEnabled = finishDetectConfig.RotateFindEnemyEnabled;
            SkipFightEndCheckWhenEnemyVisible = finishDetectConfig.SkipFightEndCheckWhenEnemyVisible;
            // 开战阻断时间（秒）限制在 0-10 之间，超出范围时修饰到对应上下限
            BlockCheckBeforeBattleSeconds = Math.Clamp(finishDetectConfig.BlockCheckBeforeBattleSeconds, 0, 10);
            PaimonEndCheckEnabled = finishDetectConfig.PaimonEndCheckEnabled;
            // 派蒙检测延时（秒）限制在 0.05-0.4 之间，超出范围时修饰到对应上下限
            PaimonEndCheckDelayMs = (int)(Math.Clamp(finishDetectConfig.PaimonEndCheckDelay, 0.05, 0.4) * 1000);
        }

        public TaskFightFinishDetectConfig(AutoFightParam taskParam)
            : this(taskParam.FinishDetectConfig)
        {
            RotaryFactor = Math.Clamp(taskParam.RotaryFactor, 1, 13);
        }

        public static void ParseCheckTimeString(
            string input,
            out double checkTime,
            List<string> names)
        {
            checkTime = 5;
            if (string.IsNullOrEmpty(input))
            {
                return; // 直接返回
            }

            var uniqueNames = new HashSet<string>(); // 用于临时去重的集合

            // 按分号分割字符串
            var segments = input.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
            {
                var trimmedSegment = segment.Trim();

                // 如果是纯数字部分
                if (double.TryParse(trimmedSegment, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out double number))
                {
                    checkTime = number; // 更新 CheckTime
                }
                else if (!uniqueNames.Contains(trimmedSegment)) // 如果是非数字且不重复
                {
                    uniqueNames.Add(trimmedSegment); // 添加到集合
                }
            }

            names.AddRange(uniqueNames); // 将集合转换为列表
        }

        public static void ParseFastCheckEndDelayString(
            string input,
            out int delayTime,
            Dictionary<string, int> nameDelayMap)
        {
            delayTime = 1500;

            if (string.IsNullOrEmpty(input))
            {
                return; // 直接返回
            }

            // 分割字符串，以分号为分隔符
            var segments = input.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
            {
                var parts = segment.Split(',');

                // 如果是纯数字部分
                if (parts.Length == 1)
                {
                    if (double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double number))
                    {
                        delayTime = (int)(number * 1000); // 更新 delayTime
                    }
                }
                // 如果是名字,数字格式
                else if (parts.Length == 2)
                {
                    string name = parts[0].Trim();
                    if (double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double value))
                    {
                        nameDelayMap[name] = (int)(value * 1000); // 更新字典，取最后一个值
                    }
                }
                // 其他格式，跳过不处理
            }
        }
    }

    /// <summary>
    /// 由本次战斗参数换算得到的战斗结束检测配置，在 RunAsync 开头赋值
    /// </summary>
    private TaskFightFinishDetectConfig _finishDetectConfig = null!;

    /// <summary>
    /// 创建自动战斗任务（txt 策略）
    /// </summary>
    /// <param name="runtime">本次战斗所在的运行环境</param>
    /// <param name="config">配置根</param>
    public AutoFightTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
        _dpi = runtime.SystemInfo.DpiScale;
        _assetScale = runtime.SystemInfo.AssetScale;
        _returnMainUiTask = new ReturnMainUiTask(runtime);
    }

    /// <summary>
    /// 依赖本次战斗参数的初始化：保存参数、读取并解析策略脚本、换算战斗结束检测配置
    /// </summary>
    /// <param name="param">本次战斗参数</param>
    private void Initialize(AutoFightParam param)
    {
        _taskParam = param;
        _combatScriptBag = CombatScriptParser.ReadAndParse(_taskParam.CombatStrategyPath);

        _finishDetectConfig = new TaskFightFinishDetectConfig(_taskParam);
    }
    // 方法1：判断是否是单个数字

    /*public int delayTime=1500;
    public Dictionary<string, int> delayTimes = new();
    public double checkTime = 5;
    public List<string> checkNames = new();*/

    /// <summary>
    /// 运行 txt 策略自动战斗：按策略循环出招直至检测到战斗结束或超时，随后按参数执行战后拾取。
    /// 战斗是否真正清场没有确认手段，正常结束时返回 Unverified；取消时抛出 OperationCanceledException
    /// </summary>
    /// <param name="param">本次战斗参数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>任务结果</returns>
    public async Task<SoloTaskResult<NoData>> RunAsync(AutoFightParam param, CancellationToken ct)
    {
        // 依赖参数的初始化必须在读取 _taskParam 之前完成
        Initialize(param);

        using var predictor = _taskParam.FightFinishDetectEnabled
            ? App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiWorld)
            : null;
        _predictor = predictor;
        _ct = ct;
        AvatarRecognition.SetCurrentAutoFightParam(_taskParam);
        AvatarRecognition.ClearLegendaryBarTracker();
        // 每场新战斗重置"敌人可见时跳过战斗结束检查"的连续跳过计数，保证拥有完整的跳过次数
        ResetSkipCheckCounter();
        try
        {
            LogScreenResolution();
        var combatScenes = CombatScenes.GetCombatScenesWithRetry(_runtime, _config);
        /*var combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
        if (!combatScenes.CheckTeamInitialized())
        {
            throw new Exception("识别队伍角色失败");
        }*/


        // var actionSchedulerByCd = ParseStringToDictionary(_taskParam.ActionSchedulerByCd);
        var combatCommands = _combatScriptBag.FindCombatScript(combatScenes.GetAvatars());
        // 命令用到的角色名 筛选交集
        var commandAvatarNames = combatCommands.Select(c => c.Name).Distinct()
            .Select(n => combatScenes.SelectAvatar(n)?.Name)
            .WhereNotNull().ToList();
        // 过滤不可执行的脚本，Task里并不支持"当前角色"。
        combatCommands = combatCommands
            .Where(c => commandAvatarNames.Contains(c.Name))
            .ToList();
        if (commandAvatarNames.Count <= 0)
        {
            throw new Exception("没有可用战斗脚本");
        }

        // 新的取消token
        var cts2 = new CancellationTokenSource();
        ct.Register(cts2.Cancel);

        combatScenes.BeforeTask(cts2.Token);
        TimeSpan fightTimeout = TimeSpan.FromSeconds(_taskParam.Timeout); // 战斗超时时间
        Stopwatch timeoutStopwatch = Stopwatch.StartNew();

        // 记录开战时间，供"开战前一段时间阻断战斗结束检查"使用
        FightStartTime = DateTime.Now;

        Stopwatch checkFightFinishStopwatch = Stopwatch.StartNew();
        TimeSpan checkFightFinishTime = TimeSpan.FromSeconds(_finishDetectConfig.CheckTime); //检查战斗超时时间的超时时间


        //战斗前检查，可做成配置
        // if (await CheckFightFinish()) {
        //     return;
        // }
        var fightEndFlag = false;
        var timeOutFlag = false;
        string lastFightName = "";

        //统计切换人打架次数
        var countFight = 0;
        
        // 可以跳过的角色名,配置中有的和命令中有的取交
        var canBeSkippedAvatarNames = combatScenes.UpdateActionSchedulerByCd(_taskParam.ActionSchedulerByCd)
            .Where(s => commandAvatarNames.Contains(s)).WhereNotNull().ToList();
        
        //所有角色是否都可被跳过
        var allCanBeSkipped = commandAvatarNames.All(a => canBeSkippedAvatarNames.Contains(a));
        
        var delayTime = _finishDetectConfig.DelayTime;
        var detectDelayTime = _finishDetectConfig.DetectDelayTime;
        
        //盾奶优先功能角色预处理
        var guardianAvatar = string.IsNullOrWhiteSpace(_taskParam.GuardianAvatar) ? null : combatScenes.SelectAvatar(int.Parse(_taskParam.GuardianAvatar));
        
        AutoFightSeek.RotationCount= 0; // 重置旋转次数
        
        // 基于经验值的战后拾取检测：在战斗过程中异步检测精英怪经验值图标
        // 仅在万叶拾取总开关开启时才启动经验值检测
        ExperienceDetector? expDetector = null;
        if (_taskParam.KazuhaPickupEnabled && _taskParam.ExpBasedPickupEnabled)
        {
            using var gameCaptureRegion = _runtime.CaptureToRectArea();
            var expRos = AutoFightAssets.Get(gameCaptureRegion).ExperienceRecognitionObjects;
            expDetector = new ExperienceDetector(_runtime, expRos, cts2.Token);
            expDetector.Start();
        }

        // 战斗操作
        var fightTask = Task.Run(async () =>
        {
            try
            {
                FightStatusFlag = true;
                
                while (!cts2.Token.IsCancellationRequested)
                {
                    // 所有战斗角色都可以被取消

                    #region 本次战斗的跳过战斗判定

                    //如果所有角色都可以被跳过，且没有任何一个cd大于0的(技能都还没好)
                    //则强制等待，因为不等待的话什么都不能做，而且会造成刷屏
                    if (allCanBeSkipped)
                    {
                        //获取最低cd
                        var minCoolDown = commandAvatarNames.Select(a => combatScenes.SelectAvatar(a)).WhereNotNull()
                            .Select(a => a.GetSkillCdSeconds()).Min();
                        if (minCoolDown > 0)
                        {
                            Logger.LogInformation("队伍中所有角色的技能都在冷却中,等待{MinCoolDown}秒后继续。", Math.Round(minCoolDown, 2));
                            await _runtime.Delay((int)Math.Ceiling(minCoolDown * 1000), ct);
                        }
                    }

                    var skipFightName = "";

                    #endregion
                    
                    for (var i = 0; i < combatCommands.Count; i++)
                    {
                        var command = combatCommands[i];
                        var lastCommand = i == 0 ? command : combatCommands[i - 1];
                        
                        #region 盾奶位技能优先功能
                        
                        var skipModel = guardianAvatar != null && lastFightName != command.Name;
                        if (skipModel) await AutoFightSkill.EnsureGuardianSkill(_runtime, guardianAvatar,lastCommand,lastFightName,
                            _taskParam.GuardianAvatar,_taskParam.GuardianAvatarHold,5,ct,_taskParam.GuardianCombatSkip,_taskParam.BurstEnabled);
                        var avatar = combatScenes.SelectAvatar(command.Name);
                        
                        #endregion
                        
                        #region 初始寻敌处理
                        
                        if ( _finishDetectConfig.RotateFindEnemyEnabled && i == 0 && _taskParam.IsFirstCheck)
                        {
                            using (AvatarRecognition.BeginExclusiveOperation())
                            {
                                await AutoFightSeek.SeekAndFightAsync(_runtime, Logger, detectDelayTime, delayTime, ct, true, _finishDetectConfig.RotaryFactor);
                            }
                        }
                        
                        #endregion
                        
                        if (avatar is null || (avatar.Name == guardianAvatar?.Name && (_taskParam.GuardianCombatSkip || _taskParam.BurstEnabled)))
                        {
                            continue;
                        }

                        #region 每个命令的跳过战斗判定

                        // 判断是否满足跳过条件:
                        // 1.上一次成功执行命令的最后执行角色不是这次的执行角色
                        // 2.这次执行的角色包含在可跳过的角色列表中
                        if (!
                                //上次命令的执行角色和这次相同
                                (lastFightName == command.Name &&
                                 // 且未跳过(成功执行)了,则不进行跳过判定
                                 skipFightName == "")
                            &&
                            // 且这次执行的角色包含在可跳过的角色列表中
                            (allCanBeSkipped || canBeSkippedAvatarNames.Contains(command.Name))
                           )
                        {
                            var cd = avatar.GetSkillCdSeconds();
                            if (cd > 0)
                            {
                                // 如果上一次该角色已经被跳过，则不进行log输出，以免刷屏
                                if (skipFightName != command.Name)
                                {
                                    var manualSkillCd = avatar.ManualSkillCd;
                                    if (manualSkillCd > 0)
                                    {
                                        Logger.LogInformation("{commandName}cd冷却为{skillCd}秒,剩余{Cd}秒,跳过此次行动",
                                            command.Name,
                                            manualSkillCd, Math.Round(cd, 2));
                                    }
                                    else
                                    {
                                        Logger.LogInformation("{CommandName}cd冷却剩余{Cd}秒,跳过此次行动", command.Name,
                                            Math.Round(cd, 2));
                                    }
                                }

                                // 避免重复log提示
                                skipFightName = command.Name;
                                continue;
                            }

                            // 表示这次执行命令没有跳过
                            skipFightName = "";
                        }

                        #endregion

                        if (timeoutStopwatch.Elapsed > fightTimeout || AutoFightSeek.RotationCount >= 6)
                        {
                            Logger.LogInformation(AutoFightSeek.RotationCount >= 6 ? "旋转次数达到上限，战斗结束" : "战斗超时结束");
                            fightEndFlag = true;
                            timeOutFlag = true;
                            break;
                        }

                        #region Q前寻敌处理
                        if (_finishDetectConfig.RotateFindEnemyEnabled && _taskParam.CheckBeforeBurst && (command.Method == Method.Burst || command.Args.Contains("q") || command.Args.Contains("Q")))
                        {
                            fightEndFlag = await CheckFightFinish(delayTime, detectDelayTime);
                        }
                        #endregion

                        command.Execute(combatScenes, lastCommand);
                        //统计战斗人次
                        if (i == combatCommands.Count - 1 || command.Name != combatCommands[i + 1].Name)
                        {
                            countFight++;
                        }

                        #region check动作触发战斗结束检测
                        if (command.Method == Method.Check && _taskParam.FightFinishDetectEnabled)
                        {
                            fightEndFlag = await CheckFightFinish(delayTime, detectDelayTime);
                        }
                        #endregion

                        lastFightName = command.Name;
                        if (!fightEndFlag && _taskParam is { FightFinishDetectEnabled: true })
                        {
                            //处于最后一个位置，或者当前执行人和下一个人名字不一样的情况，满足一定条件(开启快速检查，并且检查时间大于0或人名存在配置)检查战斗
                            if (i == combatCommands.Count - 1
                                || (
                                    _finishDetectConfig.FastCheckEnabled &&
                                    command.Name != combatCommands[i + 1].Name &&
                                    ((_finishDetectConfig.CheckTime > 0 &&
                                      checkFightFinishStopwatch.Elapsed > checkFightFinishTime)
                                     || _finishDetectConfig.CheckNames.Contains(command.Name))
                                ))
                            {
                                checkFightFinishStopwatch.Restart();

                                if (_finishDetectConfig.DelayTimes.TryGetValue(command.Name, out var time))
                                {
                                    delayTime = time;
                                    // Logger.LogInformation($"{command.Name}结束后，延时检查为{delayTime}毫秒");
                                }
                                else
                                {
                                    // Logger.LogInformation($"延时检查为{delayTime}毫秒");
                                }
                                
                                fightEndFlag = await CheckFightFinish(delayTime, detectDelayTime);
                            }
                        }

                        if (fightEndFlag)
                        {
                            break;
                        }
                    }


                    if (fightEndFlag)
                    {
                        break;
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
                InputHub.ReleaseAll();
                FightStatusFlag = false;
            }
        }, cts2.Token);

        // 在持续索敌循环启动前标记战斗进行中，避免索敌循环因 FightStatusFlag 仍为 false 而立即退出
        FightStatusFlag = true;

        // 启动持续索敌循环（异步后台运行，与战斗任务并发）
        // 使用独立的 CancellationTokenSource，以便在战后独立取消索敌循环，不影响 cts2 关联的其他组件（如 expDetector）
        using var targetingCts = CancellationTokenSource.CreateLinkedTokenSource(cts2.Token);
        Task? targetingTask = null;
        if (_taskParam.EnableCombatTargeting)
        {
            targetingTask = Task.Run(async () =>
            {
                try
                {
                    await AvatarRecognition.ContinuousTargetingLoopAsync(_runtime, targetingCts.Token, () => !AutoFightTask.FightStatusFlag);
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    Logger.LogError(e, "持续索敌循环异常");
                }
            }, targetingCts.Token);
        }

        try
        {
            await fightTask;
        }
        finally
        {
            // 战斗结束后（无论正常/异常），停止并等待索敌循环完成清理（ReleaseAllKey / MiddleButtonClick），
            // 避免其 finally 在拾取/切人过程中释放按键，干扰万叶E吸怪等操作
            if (targetingTask != null)
            {
                await targetingCts.CancelAsync();
                try { await targetingTask; } catch (OperationCanceledException) { }
            }
            FightStatusFlag = false;
        }

        try
        {
            // 基于经验值检测结果的拾取判断
            if (_taskParam.KazuhaPickupEnabled && _taskParam.ExpBasedPickupEnabled && expDetector != null)
            {
                // 战斗结束与怪物死亡可能几乎同时发生，检测器可能还没来得及捕获经验值图标
                // 保持检测器运行，每 100ms 轮询一次结果，最多等待 1.1 秒
                if (!expDetector.HasDetectedExperience)
                {
                    Logger.LogInformation("基于经验值判断：等待经验值检测结果");
                    var waitMs = 1100;
                    while (!expDetector.HasDetectedExperience && waitMs > 0)
                    {
                        await _runtime.Delay(100, ct);
                        waitMs -= 100;
                    }
                }

                await expDetector.StopAsync();
                var shouldPickup = expDetector.HasDetectedExperience;
                Logger.LogInformation("基于经验值判断：{Result} 战后拾取", shouldPickup ? "执行" : "不执行");

                if (!shouldPickup)
                {
                    // 经验值检测未通过，跳过拾取（但仍执行扫描拾取逻辑）
                    if (_taskParam is { PickDropsAfterFightEnabled: true })
                    {
                        await new ScanPickTask(_runtime, _config).Start(ct, _taskParam.PickDropsAfterFightSeconds);
                    }
                    return SoloTaskResult.Unverified("战斗结束，未检测到经验值，跳过万叶拾取");
                }
            }
        }
        finally
        {
            // 确保检测器在任何路径（异常/取消/正常）都被停止和释放
            if (expDetector != null)
            {
                await expDetector.StopAsync();
                expDetector.Dispose();
            }
        }

        if (_taskParam.BattleThresholdForLoot>=2 && countFight < _taskParam.BattleThresholdForLoot)
        {
            Logger.LogInformation($"战斗人次（{countFight}）低于配置人次（{_taskParam.BattleThresholdForLoot}），跳过此次拾取！");
            return SoloTaskResult.Unverified("战斗结束，战斗人次低于配置人次，跳过拾取");
        }
        
        await new FightPickupHelper(_runtime, _config, _taskParam)
            .RunAsync(combatScenes, timeOutFlag, lastFightName, ct);

        // 战斗与战后拾取流程正常走完；是否清场没有确认手段
        return SoloTaskResult.Unverified("战斗结束");
    }
        finally
        {
            AvatarRecognition.ClearCurrentAutoFightParam();
        }
    }

    private void LogScreenResolution()
    {
        AssertUtils.CheckGameResolution(_runtime, "自动战斗");
    }

    public async Task<bool> CheckFightFinish(int delayTime = 1500, int detectDelayTime = 450)
    {
        return await CheckFightFinish(_runtime, _finishDetectConfig, _ct, delayTime, detectDelayTime);
    }

    /// <summary>
    /// 战斗结束检测（统一实现，TXT 与 JSON 策略共用）
    /// </summary>
    /// <param name="runtime">截图、等待所用的运行环境</param>
    /// <param name="finishDetectConfig">战斗结束检测配置</param>
    /// <param name="ct">取消令牌</param>
    /// <param name="delayTime">打开编队界面前的延时（毫秒）</param>
    /// <param name="detectDelayTime">打开编队界面后到检测之间的延时（毫秒）</param>
    public static async Task<bool> CheckFightFinish(GameRuntime runtime, TaskFightFinishDetectConfig finishDetectConfig,
        CancellationToken ct, int delayTime = 1500, int detectDelayTime = 450)
    {
        // 开战后一段时间阻断战斗结束检查：距离开战时间小于配置值时，提前返回并视为战斗未结束
        if (finishDetectConfig.BlockCheckBeforeBattleSeconds > 0 &&
            (DateTime.Now - FightStartTime).TotalSeconds < finishDetectConfig.BlockCheckBeforeBattleSeconds)
        {
            // 阻断期内同样刷新最近检查时间：否则当 CheckTime 小于阻断期时，检查间隔条件
            // (DateTime.Now - LastFightFinishCheckTime) > CheckTime 会反复成立并重复进入该路径，
            // 直至阻断期结束 LastFightFinishCheckTime 一直得不到刷新
            LastFightFinishCheckTime = DateTime.Now;
            return false;
        }

        // 记录最近一次战斗结束检查的时间（供更快触发战斗结束检查判断间隔使用）
        LastFightFinishCheckTime = DateTime.Now;
        using (AvatarRecognition.BeginExclusiveOperation())
        {
            // 敌人可见时跳过战斗结束检查
            if (finishDetectConfig.SkipFightEndCheckWhenEnemyVisible)
            {
                if (_skipCheckCounter < 5)
                {
                    using var quickCapture = runtime.CaptureToRectArea();
                    var bars = AvatarRecognition.FindBloodBars(runtime, quickCapture);
                    // 不进行伤害数字识别。传奇血条（y<96或纵坐标连续出现5帧的y96-200血条）也会被 FindBloodBars 正常返回
                    // 过滤左侧 UI 区域 (x <= 200)，避免队伍头像等红色元素被误判为敌人血条
                    if (bars.Any(b => b.x > (int)(200 * runtime.SystemInfo.AssetScale)))
                    {
                        _skipCheckCounter++;
                        Logger.LogInformation("敌人可见，跳过战斗结束检查（已连续跳过{Count}次）", _skipCheckCounter);
                        return false;
                    }
                }
                _skipCheckCounter = 0;
            }
            else
            {
                _skipCheckCounter = 0;
            }

            if (finishDetectConfig.RotateFindEnemyEnabled)
            {
                bool? result = null;
                try
                {
                    result = await AutoFightSeek.SeekAndFightAsync(runtime, Logger, detectDelayTime, delayTime, ct,
                        rotaryFactor: finishDetectConfig.RotaryFactor);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "SeekAndFightAsync 方法发生异常");
                    result = false;
                }
                
                AutoFightSeek.RotationCount = (result == null) ? 
                    AutoFightSeek.RotationCount + 1 :  0;
                
                if (result != null)
                {
                    return result.Value;
                }
            }

            if (!finishDetectConfig.RotateFindEnemyEnabled)await runtime.Delay(delayTime, ct);
            
            // Logger.LogInformation("打开编队界面检查战斗是否结束，延时{detectDelayTime}毫秒检查", detectDelayTime);
            Logger.LogInformation("打开编队界面检查战斗是否结束");
            // 最终方案确认战斗结束
            InputHub.Foreground.SimulateAction(GIActions.OpenPartySetupScreen);

            if (finishDetectConfig.PaimonEndCheckEnabled)
            {
                // 派蒙辅助检测：按L后等待PaimonEndCheckDelayMs，检测左上角派蒙头像是否可见
                await runtime.Delay(finishDetectConfig.PaimonEndCheckDelayMs, ct);
                using var paimonRa = runtime.CaptureToRectArea();
                // 复用 Bv 的派蒙头像检测：左上四分之一 ROI 内模板匹配 PaimonMenu（派蒙头像），
                // 命中即视为派蒙可见 → 按L未生效、编队界面未打开、战斗未结束
                var paimonVisible = Bv.IsInMainUi(paimonRa);
                if (paimonVisible)
                {
                    // 派蒙头像可见 → 编队界面未打开（按L未生效），战斗未结束，按X取消后提前跳出战斗结束检查
                    Logger.LogInformation("派蒙头像可见，提前跳出战斗结束检查");
                    // 按X取消编队界面（走统一按键配置，默认X，支持用户改键）
                    InputHub.Foreground.SimulateAction(GIActions.Drop);
                    return false;
                }

                // 派蒙头像已消失 → 战斗可能结束，等待剩余时间后检查黄条
                await runtime.Delay(Math.Max(0, detectDelayTime - finishDetectConfig.PaimonEndCheckDelayMs), ct);
            }
            else
            {
                await runtime.Delay(detectDelayTime, ct);
            }

            using var ra = runtime.CaptureToRectArea();
            //判断整个界面是否有红色色块，如果有，则战继续，否则战斗结束
            // 只提取橙色
            
            var b3 = ra.SrcMat.At<Vec3b>(50, 790); //进度条颜色
            var whiteTile = ra.SrcMat.At<Vec3b>(50, 768); //白块
            InputHub.Foreground.SimulateAction(GIActions.Drop);
            if (IsWhite(whiteTile.Item2, whiteTile.Item1, whiteTile.Item0) &&
                IsYellow(b3.Item2, b3.Item1, b3.Item0))
            {
                Logger.LogInformation("识别到战斗结束");
                //取消正在进行的换队
                InputHub.Foreground.SimulateAction(GIActions.OpenPartySetupScreen);
                return true;
            }

            // Logger.LogInformation($"未识别到战斗结束yellow{b3.Item0},{b3.Item1},{b3.Item2}");
            // Logger.LogInformation($"未识别到战斗结束white{whiteTile.Item0},{whiteTile.Item1},{whiteTile.Item2}");
            Logger.LogInformation($"未识别到战斗结束: yellow{b3.Item0},{b3.Item1},{b3.Item2};white{whiteTile.Item0},{whiteTile.Item1},{whiteTile.Item2}");

            if (finishDetectConfig.RotateFindEnemyEnabled)
            {
                Task.Run(() =>
                {
                    Scalar bloodLower = new Scalar(255, 90, 90);
                    MoveForwardTask.MoveForwardAsync(runtime, bloodLower, bloodLower, Logger, ct);
                } ,ct);
            }
            
            _lastFightFlagTime = DateTime.Now;
            return false;
        }
    }

    static bool IsYellow(int r, int g, int b)
    {
        //Logger.LogInformation($"IsYellow({r},{g},{b})");
        // 黄色范围：R高，G高，B低
        return (r >= 200 && r <= 255) &&
               (g >= 200 && g <= 255) &&
               (b >= 0 && b <= 100);
    }

    static bool IsWhite(int r, int g, int b)
    {
        //Logger.LogInformation($"IsWhite({r},{g},{b})");
        // 白色范围：R高，G高，B低
        return (r >= 240 && r <= 255) &&
               (g >= 240 && g <= 255) &&
               (b >= 240 && b <= 255);
    }

    static double FindMax(double[] numbers)
    {
        if (numbers == null || numbers.Length == 0)
        {
            throw new ArgumentException("The array is empty or null.");
        }

        double max = numbers[0] > 10000 ? 0 : numbers[0];
        foreach (var num in numbers)
        {
            var cpnum = numbers[0] > 10000 ? 0 : num;
            max = Math.Max(max, num);
        }

        return max;
    }

    [Obsolete]
    private static Dictionary<string, double> ParseStringToDictionary(string input, double defaultValue = -1)
    {
        var dictionary = new Dictionary<string, double>();

        if (string.IsNullOrEmpty(input))
        {
            return dictionary; // 返回空字典
        }

        string[] pairs = input.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var pair in pairs)
        {
            var parts = pair.Split(',', StringSplitOptions.TrimEntries);

            if (parts.Length > 0)
            {
                string name = parts[0];
                double value = defaultValue;

                if (parts.Length > 1 && double.TryParse(parts[1], out var parsedValue))
                {
                    value = parsedValue;
                }

                dictionary[name] = value;
            }
        }

        return dictionary;
    }

    private bool HasFightFlagByYolo(ImageRegion imageRegion)
    {
        // if (RuntimeHelper.IsDebug)
        // {
        //     imageRegion.SrcMat.SaveImage(Global.Absolute(@"log\fight\" + $"{DateTime.Now:yyyyMMdd_HHmmss_ffff}.png"));
        // }
        var dict = _predictor!.Detect(imageRegion);
        return dict.ContainsKey("health_bar") || dict.ContainsKey("enemy_identify");
    }

    // 无用
    // [Obsolete]
    // private bool HasFightFlagByGadget(ImageRegion imageRegion)
    // {
    //     // 小道具位置 1920-133,800,60,50
    //     var gadgetMat = imageRegion.DeriveCrop(AutoFightAssets.Get(imageRegion).GadgetRect).SrcMat;
    //     var list = ContoursHelper.FindSpecifyColorRects(gadgetMat, new Scalar(225, 220, 225), new Scalar(255, 255, 255));
    //     // 要大于 gadgetMat 的 1/2
    //     return list.Any(r => r.Width > gadgetMat.Width / 2 && r.Height > gadgetMat.Height / 2);
    // }
}
