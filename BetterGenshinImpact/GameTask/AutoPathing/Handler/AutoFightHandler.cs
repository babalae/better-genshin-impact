using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using System.Drawing;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

// TODO 自动战斗的action
internal class AutoFightHandler : IActionHandler
{
    private readonly CombatScriptBag _combatScriptBag;

    // 780,50  778,50

    public AutoFightHandler()
    {
        _combatScriptBag = CombatScriptParser.ReadAndParse(Global.Absolute(@"User\AutoFight\"));
    }

    public async Task RunAsync(CancellationTokenSource cts)
    {
        await StartFight(cts);
    }

    private Task StartFight(CancellationTokenSource cts)
    {
        var combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
        var combatCommands = _combatScriptBag.FindCombatScript(combatScenes.Avatars);
        CancellationTokenSource cts2 = new();
        cts.Token.Register(cts2.Cancel);
        combatScenes.BeforeTask(cts2);
        // 战斗操作
        var combatTask = new Task(() =>
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
            }
        }, cts2.Token);

        // 视角操作

        // 战斗结束检测
        var domainEndTask = new Task(() =>
        {
            // TODO
            while (!cts.Token.IsCancellationRequested)
            {
                if (checkFightFinish())
                {
                    cts2.Cancel();
                    break;
                }
                Sleep(500);
            }
        });
        combatTask.Start();
        domainEndTask.Start();
        return Task.WhenAll(combatTask, domainEndTask);
    }

    // 战斗结束检测
    private bool checkFightFinish()
    {
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_L);
        Sleep(50);
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_W);
        Sleep(500);
        // 截图
        Bitmap bitmap = TaskControl.CaptureGameBitmap(TaskTriggerDispatcher.GlobalGameCapture);
        var pixelColor = bitmap.GetPixel(778, 50);
        // 255,90,90
        // 判断颜色是否是 (255, 90, 90)
        //Logger.LogInformation("抓取的颜色{R},{G},{B}", pixelColor.R, pixelColor.G, pixelColor.B);
        //string filePath = "C:\\Users\\iris\\Desktop\\autoFight.png";
        //bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);
        if (pixelColor.R == 255 && pixelColor.G == 90 && pixelColor.B == 90)
        {
            return true;
        }
        return false;
    }
}
