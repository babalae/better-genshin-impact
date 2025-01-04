using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Common.Job;
using OpenCvSharp;
using Vanara;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.AutoFight;

public class AutoFightTask : ISoloTask
{
    public string Name => "自动战斗";

    private readonly AutoFightParam _taskParam;

    private readonly CombatScriptBag _combatScriptBag;

    private CancellationToken _ct;

    private readonly BgiYoloV8Predictor _predictor;

    private DateTime _lastFightFlagTime = DateTime.Now; // 战斗标志最近一次出现的时间

    private readonly double _dpi = TaskContext.Instance().DpiScale;

    private User32.VK _partySetupKey = User32.VK.VK_L;

    private User32.VK _dropKey = User32.VK.VK_X;


    private class TaskFightFinishDetectConfig
    {
        public int DelayTime=1500;
        public Dictionary<string, int> DelayTimes = new();
        public double CheckTime = 5;
        public List<string> CheckNames = new();
        public bool FastCheckEnabled;
        public TaskFightFinishDetectConfig(AutoFightParam.FightFinishDetectConfig finishDetectConfig)
        {
            FastCheckEnabled=finishDetectConfig.FastCheckEnabled;
            ParseCheckTimeString(finishDetectConfig.FastCheckParams,out CheckTime,CheckNames);
            ParseFastCheckEndDelayString(finishDetectConfig.CheckEndDelay,out DelayTime,DelayTimes);
            BattleEndProgressBarColor = ParseStringToTuple(finishDetectConfig.BattleEndProgressBarColor, (95, 235, 255));
            BattleEndProgressBarColorTolerance = ParseSingleOrCommaSeparated(finishDetectConfig.BattleEndProgressBarColorTolerance, (6, 6, 6));
        }

        public (int, int, int) BattleEndProgressBarColor{ get; }
        public (int, int, int) BattleEndProgressBarColorTolerance{ get; }
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
                if (double.TryParse(trimmedSegment, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
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
                    if (double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                    {
                        delayTime = (int)(number * 1000); // 更新 delayTime
                    }
                }
                // 如果是名字,数字格式
                else if (parts.Length == 2)
                {
                    string name = parts[0].Trim();
                    if (double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
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
            _predictor = BgiYoloV8PredictorFactory.GetPredictor(@"Assets\Model\World\bgi_world.onnx");
        }

        _finishDetectConfig=new TaskFightFinishDetectConfig(_taskParam.FinishDetectConfig);
    }

    // 方法1：判断是否是单个数字
 
    /*public int delayTime=1500;
    public Dictionary<string, int> delayTimes = new();
    public double checkTime = 5;
    public List<string> checkNames = new();*/
    public async Task Start(CancellationToken ct)
    {
        // 从KeyBindingsConfig中读取配置
        var keyConfig = TaskContext.Instance().Config.KeyBindingsConfig;
        _partySetupKey = keyConfig.OpenPartySetupScreen;
        _dropKey = keyConfig.Drop;

        _ct = ct;

        LogScreenResolution();
        var combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
        if (!combatScenes.CheckTeamInitialized())
        {
            throw new Exception("识别队伍角色失败");
        }

        var combatCommands = _combatScriptBag.FindCombatScript(combatScenes.Avatars);

        // 新的取消token
        var cts2 = new CancellationTokenSource();
        ct.Register(cts2.Cancel);

        combatScenes.BeforeTask(cts2.Token);
        TimeSpan fightTimeout = TimeSpan.FromSeconds(_taskParam.Timeout); // 战斗超时时间
        Stopwatch timeoutStopwatch = Stopwatch.StartNew();
        
        Stopwatch checkFightFinishStopwatch = Stopwatch.StartNew();
        TimeSpan checkFightFinishTime = TimeSpan.FromSeconds(_finishDetectConfig.CheckTime); //检查战斗超时时间的超时时间

        
        //战斗前检查，可做成配置
/*        if (await CheckFightFinish()) {
            return;
        }*/
        var fightEndFlag = false;
        string lastFighttName = "";
        // 战斗操作
        var fightTask = Task.Run(async () =>
        {
            try
            {
                while (!cts2.Token.IsCancellationRequested)
                {
              
                    // 通用化战斗策略
                    for (var i = 0; i < combatCommands.Count; i++)
                    {
                        var command = combatCommands[i];
                        if (timeoutStopwatch.Elapsed > fightTimeout)
                        {
                            Logger.LogInformation("战斗超时结束");
                            fightEndFlag = true;
                            break;
                        }

                        command.Execute(combatScenes);


                        lastFighttName = command.Name;
                        if (!fightEndFlag  && _taskParam is { FightFinishDetectEnabled: true } )
                        {

                            //处于最后一个位置，或者当前执行人和下一个人名字不一样的情况，满足一定条件(开启快速检查，并且检查时间大于0或人名存在配置)检查战斗
                            if (i==combatCommands.Count - 1 
                                || (
                                    _finishDetectConfig.FastCheckEnabled  && command.Name!=combatCommands[i+1].Name &&
                                        ((_finishDetectConfig.CheckTime>0 && checkFightFinishStopwatch.Elapsed>checkFightFinishTime)
                                     ||  _finishDetectConfig.CheckNames.Contains(command.Name))   
                                     ))
                            {
                                
                                checkFightFinishStopwatch.Restart();
                                var delayTime = _finishDetectConfig.DelayTime;
                                if (_finishDetectConfig.DelayTimes.TryGetValue(command.Name, out var time))
                                {
                                    delayTime = time;
                                    Logger.LogInformation($"{command.Name}结束后，延时检查为{delayTime}毫秒");
                                }
                                else
                                {
                                    Logger.LogInformation($"延时检查为{delayTime}毫秒");
                                }

                                /*if (i<combatCommands.Count - 1)
                                {
                                    Logger.LogInformation($"{command.Name}下一个人为{combatCommands[i+1].Name}毫秒");
                                }*/
                                fightEndFlag = await CheckFightFinish(delayTime);
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
        }, cts2.Token);

        await fightTask;

        // 战斗结束检测线程
        // var endTask = Task.Run(async () =>
        // {
        //     if (!_taskParam.FightFinishDetectEnabled)
        //     {
        //         return;
        //     }
        //
        //     try
        //     {
        //         while (!cts2.IsCancellationRequested)
        //         {
        //             var finish = await CheckFightFinish();
        //             if (finish)
        //             {
        //                 await cts2.CancelAsync();
        //                 break;
        //             }
        //
        //             Sleep(1000, cts2.Token);
        //         }
        //     }
        //     catch (Exception e)
        //     {
        //         Debug.WriteLine(e.Message);
        //         Debug.WriteLine(e.StackTrace);
        //     }
        // }, cts2.Token);
        //
        // await Task.WhenAll(fightTask, endTask);
        if (_taskParam.KazuhaPickupEnabled)
        {
            // 队伍中存在万叶的时候使用一次长E
            var kazuha = combatScenes.Avatars.FirstOrDefault(a => a.Name == "枫原万叶");
            if (kazuha != null)
            {
                var time = DateTime.UtcNow -  kazuha.LastSkillTime;
                //当万叶最后一个出招，并且cd大于3时，此时不再触发万叶拾取
                if (!(lastFighttName== "枫原万叶" && time.TotalSeconds>3))
                {
                
                    Logger.LogInformation("使用枫原万叶长E拾取掉落物");
                    await Delay(300, ct);
                    if (kazuha.TrySwitch())
                    {
                            if (time.TotalMilliseconds > 0 && time.TotalSeconds <= kazuha.SkillHoldCd)
                            {
                                Logger.LogInformation("枫原万叶长E技能可能处于冷却中，等待 {Time} s",time.TotalSeconds);
                                await Delay((int)Math.Ceiling(time.TotalMilliseconds), ct);
                            }
                            kazuha.UseSkill(true);
                            await Task.Delay(100);
                            Simulation.SendInput.Mouse.LeftButtonClick();
                            await Delay(1500, ct);
                       
                    }
                }
                else
                {
                    Logger.LogInformation("最后一次由万叶出招，不再重复拾取！");
                }


            }
        }

        if (_taskParam is { PickDropsAfterFightEnabled: true })
        {
            // 执行自动拾取掉落物的功能
            await new ScanPickTask().Start(ct);
        }
    }

    private void LogScreenResolution()
    {
        var gameScreenSize = SystemControl.GetGameScreenRect(TaskContext.Instance().GameHandle);
        if (gameScreenSize.Width * 9 != gameScreenSize.Height * 16)
        {
            Logger.LogWarning("游戏窗口分辨率不是 16:9 ！当前分辨率为 {Width}x{Height} , 非 16:9 分辨率的游戏可能无法正常使用自动战斗功能 !", gameScreenSize.Width, gameScreenSize.Height);
        }
    }

    static bool AreDifferencesWithinBounds((int, int, int) a, (int, int, int) b, (int, int, int) c)
    {
        // 计算每个位置的差值绝对值并进行比较
        return Math.Abs(a.Item1 - b.Item1) < c.Item1 &&
               Math.Abs(a.Item2 - b.Item2) < c.Item2 &&
               Math.Abs(a.Item3 - b.Item3) < c.Item3;
    }

    private async Task<bool> CheckFightFinish(int delayTime=1500)
    {
        //  YOLO 判断血条和怪物位置
        // if (HasFightFlagByYolo(CaptureToRectArea()))
        //  {
        //    _lastFightFlagTime = DateTime.Now;
        //  return false;
        //   }
        //

        //Random random = new Random();
        //double randomFraction = random.NextDouble();  // 生成 0 到 1 之间的随机小数
        //此处随机数，防止固定招式下，使按L正好处于招式下，导致无法准确判断战斗结束
        // double randomNumber = 1 + (randomFraction * (3 - 1));

        // 几秒内没有检测到血条和怪物位置，则开始旋转视角重新检测
        //if ((DateTime.Now - _lastFightFlagTime).TotalSeconds > randomNumber)
        //{
        // 旋转完毕后都没有检测到血条和怪物位置，则按L键确认战斗结束
        /**
        Simulation.SendInput.Mouse.MiddleButtonClick();
        await Delay(300, _ct);
        for (var i = 0; i < 8; i++)
        {
            Simulation.SendInput.Mouse.MoveMouseBy((int)(500 * _dpi), 0);
            await Delay(800, _ct); // 等待视角稳定
            if (HasFightFlagByYolo(CaptureToRectArea()))
            {
                _lastFightFlagTime = DateTime.Now;
                return false;
            }
        }
        **/
  
        await Delay(delayTime, _ct);
        Logger.LogInformation("打开编队界面检查战斗是否结束");
        // 最终方案确认战斗结束
        Simulation.SendInput.Keyboard.KeyPress(_partySetupKey);
        await Delay(450, _ct);
        var ra = CaptureToRectArea();
        var b3 = ra.SrcMat.At<Vec3b>(50, 790);

        if (AreDifferencesWithinBounds(_finishDetectConfig.BattleEndProgressBarColor, (b3.Item0, b3.Item1, b3.Item2), _finishDetectConfig.BattleEndProgressBarColorTolerance))
        {
            Logger.LogInformation("识别到战斗结束");
            Simulation.SendInput.Keyboard.KeyPress(_dropKey);
            return true;
        }

        Simulation.SendInput.Keyboard.KeyPress(_dropKey);
        Logger.LogInformation($"未识别到战斗结束{b3.Item0},{b3.Item1},{b3.Item2}");
        _lastFightFlagTime = DateTime.Now;
        return false;

        //  }

        return false;
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