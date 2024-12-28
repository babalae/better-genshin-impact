using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
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
    private (int, int, int) _battleEndProgressBarColor;
    private (int, int, int) _battleEndProgressBarColorTolerance;

    public AutoFightTask(AutoFightParam taskParam)
    {
        _taskParam = taskParam;
        _combatScriptBag = CombatScriptParser.ReadAndParse(_taskParam.CombatStrategyPath);

        if (_taskParam.FightFinishDetectEnabled)
        {
            _predictor = BgiYoloV8PredictorFactory.GetPredictor(@"Assets\Model\World\bgi_world.onnx");
        }

        _battleEndProgressBarColor = ParseStringToTuple(taskParam.BattleEndProgressBarColor, (95, 235, 255));
        _battleEndProgressBarColorTolerance = ParseSingleOrCommaSeparated(taskParam.BattleEndProgressBarColorTolerance, (6, 6, 6));
    }

    // 方法1：判断是否是单个数字
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

    public async Task Start(CancellationToken ct)
    {
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
        TimeSpan fightTimeout = TimeSpan.FromSeconds(_taskParam.Timeout); // 默认战斗超时时间
        Stopwatch stopwatch = Stopwatch.StartNew();

        //战斗前检查，可做成配置
/*        if (await CheckFightFinish()) {
            return;
        }*/
        // 战斗操作
        var fightTask = Task.Run(async () =>
        {
            try
            {
                while (!cts2.Token.IsCancellationRequested)
                {
                    var timeoutFlag = false;
                    // 通用化战斗策略
                    foreach (var command in combatCommands)
                    {
                        if (stopwatch.Elapsed > fightTimeout)
                        {
                            Logger.LogInformation("战斗超时结束");
                            timeoutFlag = true;
                            break;
                        }

                        command.Execute(combatScenes);
                    }

                    if (timeoutFlag || _taskParam is { FightFinishDetectEnabled: true } && await CheckFightFinish())
                    {
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
                Debug.WriteLine(e.StackTrace);
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

        if (_taskParam is { FightFinishDetectEnabled: true, PickDropsAfterFightEnabled: true })
        {
            //

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

    private async Task<bool> CheckFightFinish()
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
        //检查延时，根据队伍不同可以进行优化，可做成配置
        await Delay(1500, _ct);
        Logger.LogInformation("按L检查战斗是否结束");
        // 最终方案确认战斗结束
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_L);
        await Delay(450, _ct);
        var ra = CaptureToRectArea();
        var b3 = ra.SrcMat.At<Vec3b>(50, 790);

        if (AreDifferencesWithinBounds(_battleEndProgressBarColor, (b3.Item0, b3.Item1, b3.Item2), _battleEndProgressBarColorTolerance))
        {
            Logger.LogInformation("识别到战斗结束");
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
            return true;
        }

        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
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