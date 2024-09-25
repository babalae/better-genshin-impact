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
using BetterGenshinImpact.GameTask.AutoFight;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using System.IO;
using Wpf.Ui.Violeta.Controls;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

internal class AutoFightHandler : IActionHandler
{
    public async Task RunAsync(CancellationTokenSource cts)
    {
        await StartFight(cts);
    }

    private Task StartFight(CancellationTokenSource cts)
    {
        // 新的取消token
        var cts2 = new CancellationTokenSource();
        cts2.Token.Register(cts.Cancel);

        // 战斗线程
        var fightSoloTask = new AutoFightTask(new AutoFightParam(GetFightStrategy()));
        var fightTask = Task.Run(() =>
        {
            fightSoloTask.Start(cts2);
        }, cts2.Token);

        // 战斗结束检测线程
        var endTask = Task.Run(() =>
        {
            while (!cts2.IsCancellationRequested)
            {
                if (CheckFightFinish())
                {
                    cts2.Cancel();
                    break;
                }
                Sleep(1000, cts2);
            }
        }, cts2.Token);

        // 等待战斗结束
        return Task.WhenAll(fightTask, endTask);
    }

    private string GetFightStrategy()
    {
        var path = Global.Absolute(@"User\AutoFight\" + TaskContext.Instance().Config.AutoFightConfig.StrategyName + ".txt");
        if ("根据队伍自动选择".Equals(TaskContext.Instance().Config.AutoFightConfig.StrategyName))
        {
            path = Global.Absolute(@"User\AutoFight\");
        }
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new Exception("战斗策略文件不存在");
        }

        return path;
    }

    // 战斗结束检测
    private bool CheckFightFinish()
    {
        // TODO 添加战斗结束检测 YOLO 判断血条和怪物位置

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
