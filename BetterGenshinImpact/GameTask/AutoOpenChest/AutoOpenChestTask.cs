using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoFishing.Assets;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static TorchSharp.torch.distributions.constraints;
namespace GameTask.AutoOpenChest;

/// <summary>
/// 识别宝箱图标，走向宝箱并开启。
/// </summary>
public class AutoOpenChestTask : ISoloTask
{
    public string Name => "识别并开启宝箱";

    private AutoOpenChestAssets assets = AutoOpenChestAssets.Instance;

    public async Task Start(CancellationToken ct)
    {
        var ra = CaptureToRectArea();

        if (ra.Find(assets.ChestFIconRo).IsExist())
        {
            CancellationTokenSource _ct = new();
            ct.Register(_ct.Cancel);
            bool isFlower = false; // 是否是地脉花
            // 限制寻找宝箱的时间
            var timeLimit = 60;
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeLimit), _ct.Token)
                .ContinueWith(_ => _ct.Cancel(), TaskContinuationOptions.OnlyOnRanToCompletion);
            try
            {
                while (!_ct.IsCancellationRequested)
                {
                    ra = CaptureToRectArea();
                    Region chestIcon = ra.Find(assets.ChestIconRo);
                    int limit = chestIcon.Width;
                    if (!chestIcon.IsExist())
                    {
                        Logger.LogInformation("未找到宝箱图标");
                        return;
                    }

                    if (ra.Find(assets.ChestFIconRo).IsExist() || ra.Find(assets.FlowerFIconRo).IsExist())
                    {
                        // 找到宝箱/ 地脉花
                        isFlower = ra.Find(assets.FlowerFIconRo).IsExist();
                        Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract, KeyType.KeyPress);
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                        break;
                    }

                    if (Math.Abs(chestIcon.Width / 2 - chestIcon.X) < limit)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                    }

                    if (chestIcon.Y > 600)
                    {
                        // 若宝箱图标在下方就表示宝箱在后面。
                        Simulation.SendInput.SimulateAction(GIActions.MoveBackward);
                        await Delay(30, _ct.Token);
                        Simulation.SendInput.Mouse.MiddleButtonClick();
                    }
                    else
                    {
                        var gap = (ra.Width / 2) - chestIcon.X;
                        int rate = 2;
                        Simulation.SendInput.Mouse.MoveMouseBy(gap / rate, 0);
                    }

                    await Delay(500, _ct.Token);
                }
            }
            finally
            {
                // 如果循环提前退出，取消计时任务
                Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                _ct.Cancel();
                await timeoutTask; // 等待超时任务结束（忽略可能的异常）
            }

            // TODO : 是否考虑兼容地脉花 以及地脉花的获取策略

            if (isFlower) {
                // 地脉花策略
                flowerHandle();
            }
        }
    }

    private async void flowerHandle()
    {
        Simulation.SendInput.SimulateAction(GIActions.OpenPaimonMenu);
    }


}
