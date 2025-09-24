using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.GameTask.Common.Job;
using OpenCvSharp;
using BetterGenshinImpact.Helpers;
using Vanara;
using Microsoft.Extensions.DependencyInjection;

namespace BetterGenshinImpact.GameTask.AutoFight;

public class AutoFightTask : ISoloTask
{
    public string Name => "自动战斗";

    private readonly AutoFightParam _taskParam;

    private readonly CombatScriptBag _combatScriptBag;

    private CancellationToken _ct;

    private readonly BgiYoloPredictor _predictor;

    private DateTime _lastFightFlagTime = DateTime.Now; // 战斗标志最近一次出现的时间

    private readonly double _dpi = TaskContext.Instance().DpiScale;

    public static OtherConfig Config { get; set; } = TaskContext.Instance().Config.OtherConfig;
    
    public static bool FightStatusFlag { get; set; } = false;

    private class TaskFightFinishDetectConfig
    {
        public int DelayTime = 1500;
        public int DetectDelayTime = 450;
        public Dictionary<string, int> DelayTimes = new();
        public double CheckTime = 5;
        public List<string> CheckNames = new();
        public bool FastCheckEnabled;
        public bool RotateFindEnemyEnabled = false;

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

        public (int, int, int) BattleEndProgressBarColor { get; }
        public (int, int, int) BattleEndProgressBarColorTolerance { get; }

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


        static bool IsSingleNumber(string input, out int result)
        {
            return int.TryParse(input, out result);
        }

        static (int, int, int) ParseSingleOrCommaSeparated(string input, (int, int, int) defaultValue)
        {
            // 如果是单个数字
            if (IsSingleNumber(input, out var singleNumber))
            {
                return (singleNumber, singleNumber, singleNumber);
            }

            return ParseStringToTuple(input, defaultValue);
        }

        static (int, int, int) ParseStringToTuple(string input, (int, int, int) defaultValue)
        {
            // 尝试按逗号分割字符串
            var parts = input.Split(',');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], out var num1) &&
                int.TryParse(parts[1], out var num2) &&
                int.TryParse(parts[2], out var num3))
            {
                return (num1, num2, num3);
            }

            // 如果解析失败，返回默认值
            return defaultValue;
        }
    }

    private TaskFightFinishDetectConfig _finishDetectConfig;

    public AutoFightTask(AutoFightParam taskParam)
    {
        _taskParam = taskParam;
        _combatScriptBag = CombatScriptParser.ReadAndParse(_taskParam.CombatStrategyPath);

        if (_taskParam.FightFinishDetectEnabled)
        {
            _predictor = App.ServiceProvider.GetRequiredService<BgiOnnxFactory>().CreateYoloPredictor(BgiOnnxModel.BgiWorld);
        }

        _finishDetectConfig = new TaskFightFinishDetectConfig(_taskParam.FinishDetectConfig);
    }
    public CombatScenes GetCombatScenesWithRetry()
    {
        const int maxRetries = 5;
        var retryDelayMs = 1000; // 可选：重试间隔，单位毫秒

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
            if (combatScenes.CheckTeamInitialized())
            {
                return combatScenes;
            }
        
            if (attempt < maxRetries)
            {
                Thread.Sleep(retryDelayMs); // 可选：延迟再试
            }
        }
        throw new Exception("识别队伍角色失败（已重试 5 次）");
    }
    // 方法1：判断是否是单个数字

    /*public int delayTime=1500;
    public Dictionary<string, int> delayTimes = new();
    public double checkTime = 5;
    public List<string> checkNames = new();*/
    public async Task Start(CancellationToken ct)
    {
        _ct = ct;

        LogScreenResolution();
        var combatScenes = GetCombatScenesWithRetry();
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
        
        //盾奶优先功能角色预处理
        var guardianAvatar = string.IsNullOrWhiteSpace(_taskParam.GuardianAvatar) ? null : combatScenes.SelectAvatar(int.Parse(_taskParam.GuardianAvatar));
        
        AutoFightSeek.RotationCount= 0; // 重置旋转次数
        
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
                            await Delay((int)Math.Ceiling(minCoolDown * 1000), ct);
                        }
                    }

                    var skipFightName = "";

                    #endregion
                    
                    for (var i = 0; i < combatCommands.Count; i++)
                    {
                        var command = combatCommands[i];
                        var lastCommand = i == 0 ? command : combatCommands[i - 1];
                        
                        #region 盾奶位技能优先功能
                        
                        var skipModel = _taskParam.SkipModel? (guardianAvatar != null) : (guardianAvatar != null && lastFightName != command.Name);
                        if (skipModel) await AutoFightSkill.EnsureGuardianSkill(guardianAvatar,lastCommand,lastFightName,_taskParam.GuardianAvatar,_taskParam.GuardianAvatarHold,5,ct);
                        var avatar = combatScenes.SelectAvatar(command.Name);
                        
                        #endregion
                        
                        if (avatar is null || (avatar.Name == guardianAvatar?.Name && _taskParam.GuardianCombatSkip))
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
                        
                        command.Execute(combatScenes);
                        //统计战斗人次
                        if (i == combatCommands.Count - 1 || command.Name != combatCommands[i + 1].Name)
                        {
                            countFight++;
                        }

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
                                var delayTime = _finishDetectConfig.DelayTime;
                                var detectDelayTime = _finishDetectConfig.DetectDelayTime;
                                if (_finishDetectConfig.DelayTimes.TryGetValue(command.Name, out var time))
                                {
                                    delayTime = time;
                                    Logger.LogInformation($"{command.Name}结束后，延时检查为{delayTime}毫秒");
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
                Simulation.ReleaseAllKey();
                FightStatusFlag = false;
            }
        }, cts2.Token);

        await fightTask;
        if (_taskParam.BattleThresholdForLoot>=2 && countFight < _taskParam.BattleThresholdForLoot)
        {
            Logger.LogInformation($"战斗人次（{countFight}）低于配置人次（{_taskParam.BattleThresholdForLoot}），跳过此次拾取！");
            return;
        }
        
        if (_taskParam.KazuhaPickupEnabled)
        {
            // 队伍中存在万叶的时候使用一次长E
            var kazuha = combatScenes.SelectAvatar("枫原万叶");
            
            var oldPartyName = RunnerContext.Instance.PartyName;
            var switchPartyFlag = false;
            if (kazuha == null && !timeOutFlag &&!string.IsNullOrEmpty(_taskParam.KazuhaPartyName) && oldPartyName != _taskParam.KazuhaPartyName)
            {
                try
                {
                    Logger.LogInformation($"切换为拾取队伍：{_taskParam.KazuhaPartyName}");
                    var success = await new SwitchPartyTask().Start(_taskParam.KazuhaPartyName, ct);
                    if (success)
                    {
                        Logger.LogInformation($"成功切换队伍为{_taskParam.KazuhaPartyName}");
                        switchPartyFlag = true;
                        RunnerContext.Instance.PartyName = _taskParam.KazuhaPartyName;
                        RunnerContext.Instance.ClearCombatScenes();
                        var cs = await RunnerContext.Instance.GetCombatScenes(ct);
                        kazuha = cs.SelectAvatar("枫原万叶");
                    }
                }
                catch (Exception e)
                {
                    Logger.LogInformation("切换队伍异常，跳过此步骤！");
                }

            }
          
            
            if (kazuha != null)
            {
                var time = TimeSpan.FromSeconds(kazuha.GetSkillCdSeconds());
                //当万叶cd大于3时，此时不再触发万叶拾取，
                if (!(lastFightName == "枫原万叶" && time.TotalSeconds > 3))
                {
                    Logger.LogInformation("使用枫原万叶长E拾取掉落物");
                    await Delay(300, ct);
                    if (kazuha.TrySwitch())
                    {
                        await kazuha.WaitSkillCd(ct);
                        kazuha.UseSkill(true);
                        await Task.Delay(100);
                        Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
                        await Delay(1500, ct);
                    }
                }
                else
                {
                    Logger.LogInformation("距最近一次万叶出招，时间过短，跳过此次万叶拾取！");
                }
            }
            //切换过队伍的，需要再切回来
            if (switchPartyFlag && !string.IsNullOrEmpty(oldPartyName))
            {
                try
                {
                    Logger.LogInformation($"切换为原队伍：{oldPartyName}");
                    var success = await new SwitchPartyTask().Start(oldPartyName, ct);
                    if (success)
                    {
                        Logger.LogInformation($"切换为原队伍{oldPartyName}");
                        switchPartyFlag = true;
                        RunnerContext.Instance.PartyName = oldPartyName;
                        RunnerContext.Instance.ClearCombatScenes();
                        await RunnerContext.Instance.GetCombatScenes(ct);
    
                    }
                }
                catch (Exception e)
                {
                    Logger.LogInformation("恢复原队伍失败，跳过此步骤！");
                }
                    
            }
        }

        if (_taskParam is { PickDropsAfterFightEnabled: true } )
        {
            // 执行自动拾取掉落物的功能
            await new ScanPickTask().Start(ct);
        }
    }

    private void LogScreenResolution()
    {
        AssertUtils.CheckGameResolution("自动战斗");
    }

    static bool AreDifferencesWithinBounds((int, int, int) a, (int, int, int) b, (int, int, int) c)
    {
        // 计算每个位置的差值绝对值并进行比较
        return Math.Abs(a.Item1 - b.Item1) < c.Item1 &&
               Math.Abs(a.Item2 - b.Item2) < c.Item2 &&
               Math.Abs(a.Item3 - b.Item3) < c.Item3;
    }

    public async Task<bool> CheckFightFinish(int delayTime = 1500, int detectDelayTime = 450)
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
            
            AutoFightSeek.RotationCount = (result == null) ? 
                AutoFightSeek.RotationCount + 1 :  0;
            
            if (result != null)
            {
                return result.Value;
            }
        }

        if (!_finishDetectConfig.RotateFindEnemyEnabled)await Delay(delayTime, _ct);
        
        Logger.LogInformation("打开编队界面检查战斗是否结束，延时{detectDelayTime}毫秒检查", detectDelayTime);
        // 最终方案确认战斗结束
        Simulation.SendInput.SimulateAction(GIActions.OpenPartySetupScreen);
        await Delay(detectDelayTime, _ct);
        
        var ra = CaptureToRectArea();
        //判断整个界面是否有红色色块，如果有，则战继续，否则战斗结束
        // 只提取橙色
        
        var b3 = ra.SrcMat.At<Vec3b>(50, 790); //进度条颜色
        var whiteTile = ra.SrcMat.At<Vec3b>(50, 768); //白块
        Simulation.SendInput.SimulateAction(GIActions.Drop);
        if (IsWhite(whiteTile.Item2, whiteTile.Item1, whiteTile.Item0) &&
            IsYellow(b3.Item2, b3.Item1,
                b3.Item0) /* AreDifferencesWithinBounds(_finishDetectConfig.BattleEndProgressBarColor, (b3.Item0, b3.Item1, b3.Item2), _finishDetectConfig.BattleEndProgressBarColorTolerance)*/
           )
        {
            Logger.LogInformation("识别到战斗结束");
            //取消正在进行的换队
            Simulation.SendInput.SimulateAction(GIActions.OpenPartySetupScreen);
            return true;
        }

        Logger.LogInformation($"未识别到战斗结束yellow{b3.Item0},{b3.Item1},{b3.Item2}");
        Logger.LogInformation($"未识别到战斗结束white{whiteTile.Item0},{whiteTile.Item1},{whiteTile.Item2}");

        if (_finishDetectConfig.RotateFindEnemyEnabled)
        {
            Task.Run(() =>
            {
                Scalar bloodLower = new Scalar(255, 90, 90);
                MoveForwardTask.MoveForwardAsync(bloodLower, bloodLower, Logger, _ct);
            } ,_ct);
        }
        
        _lastFightFlagTime = DateTime.Now;
        return false;
    }

    bool IsYellow(int r, int g, int b)
    {
        //Logger.LogInformation($"IsYellow({r},{g},{b})");
        // 黄色范围：R高，G高，B低
        return (r >= 200 && r <= 255) &&
               (g >= 200 && g <= 255) &&
               (b >= 0 && b <= 100);
    }

    bool IsWhite(int r, int g, int b)
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
        var dict = _predictor.Detect(imageRegion);
        return dict.ContainsKey("health_bar") || dict.ContainsKey("enemy_identify");
    }

    // 无用
    // [Obsolete]
    // private bool HasFightFlagByGadget(ImageRegion imageRegion)
    // {
    //     // 小道具位置 1920-133,800,60,50
    //     var gadgetMat = imageRegion.DeriveCrop(AutoFightAssets.Instance.GadgetRect).SrcMat;
    //     var list = ContoursHelper.FindSpecifyColorRects(gadgetMat, new Scalar(225, 220, 225), new Scalar(255, 255, 255));
    //     // 要大于 gadgetMat 的 1/2
    //     return list.Any(r => r.Width > gadgetMat.Width / 2 && r.Height > gadgetMat.Height / 2);
    // }
}
