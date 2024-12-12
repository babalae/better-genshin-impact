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

    public AutoFightTask(AutoFightParam taskParam)
    {
        _taskParam = taskParam;
        _combatScriptBag = CombatScriptParser.ReadAndParse(_taskParam.CombatStrategyPath);

        if (_taskParam.FightFinishDetectEnabled)
        {
            _predictor = BgiYoloV8PredictorFactory.GetPredictor(@"Assets\Model\World\bgi_world.onnx");
        }
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

        // 战斗操作
        var fightTask = Task.Run(async () =>
        {
            try
            {
                while (!cts2.Token.IsCancellationRequested)
                {
                    // 通用化战斗策略
                    foreach (var command in combatCommands)
                    {
                        command.Execute(combatScenes);
                    }

                    if (_taskParam is { FightFinishDetectEnabled: true } && await CheckFightFinish())
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

    private async Task<bool> CheckFightFinish()
    {
        //  YOLO 判断血条和怪物位置
        if (HasFightFlagByYolo(CaptureToRectArea()))
        {
            _lastFightFlagTime = DateTime.Now;
            return false;
        }

        // 几秒内没有检测到血条和怪物位置，则开始旋转视角重新检测
        if ((DateTime.Now - _lastFightFlagTime).TotalSeconds > 3)
        {
            // 旋转完毕后都没有检测到血条和怪物位置，则按L键确认战斗结束
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

            // 最终方案确认战斗结束
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_L);
            await Delay(450, _ct);
            var ra = CaptureToRectArea();
            var b3 = ra.SrcMat.At<Vec3b>(50, 790);
            if (b3.Equals(new Vec3b(95, 235, 255)))
            {
                Logger.LogInformation("识别到战斗结束");
                Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_SPACE);
                return true;
            }
            else
            {
                return false;
            }
        }

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