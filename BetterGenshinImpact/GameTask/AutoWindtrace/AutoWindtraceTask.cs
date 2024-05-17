using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.AutoWindtrace.Assets;
//using BetterGenshinImpact.GameTask.AutoWood.Utils;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Genshin.Settings;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.ViewModel.Pages;
using Fischless.GameCapture;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.User32;
using GC = System.GC;

namespace BetterGenshinImpact.GameTask.AutoWindtrace;

/// <summary>
/// 自动风行迷踪
/// </summary>
public class AutoWindtraceTask
{
    private readonly AutoWindtraceAssets _assets;

    public AutoWindtraceTask()
    {
        AutoWindtraceAssets.DestroyInstance();
        _assets = AutoWindtraceAssets.Instance;
    }

    public void Start(WindtraceTaskParam taskParam)
    {
        var hasLock = false;
        try
        {
            hasLock = TaskSemaphore.Wait(0);
            if (!hasLock)
            {
                Logger.LogError("启动自动风行迷踪功能失败：当前存在正在运行中的独立任务，请不要重复执行任务！");
                return;
            }

            TaskTriggerDispatcher.Instance().StopTimer();
            //Logger.LogInformation("→ {Text} 设置伐木总次数：{Cnt}", "自动伐木，启动！", taskParam.CoinNum);

            //SettingsContainer settingsContainer = new();

            //if (settingsContainer.OverrideController?.KeyboardMap?.ActionElementMap.Where(item => item.ActionId == ActionId.Gadget).FirstOrDefault()?.ElementIdentifierId is ElementIdentifierId key)
            //{
            //    if (key != ElementIdentifierId.Z)
            //    {
            //        _zKey = key.ToVK();
            //        Logger.LogInformation($"自动伐木检测到用户改键 {ElementIdentifierId.Z.ToName()} 改为 {key.ToName()}");
            //        if (key == ElementIdentifierId.LeftShift || key == ElementIdentifierId.RightShift)
            //        {
            //            Logger.LogInformation($"用户改键 {key.ToName()} 可能不受模拟支持，若使用正常则忽略");
            //        }
            //    }
            //}

            //应该不会有骨骼清奇的选手改F的键位吧？

            taskParam.CoinNum = 0;//TODO: 在EnterGame函数中OCR识别代币数量
            SystemControl.ActivateWindow();
            while (taskParam.CoinNum < 6000)
            {
                Logger.LogInformation("风行迷踪进度：{Cnt} / 6000", taskParam.CoinNum);
                if (taskParam.Cts.IsCancellationRequested)
                {
                    break;
                }

                Play(taskParam);
                VisionContext.Instance().DrawContent.ClearAll();
                Sleep(500, taskParam.Cts);
            }
        }
        catch (NormalEndException e)
        {
            Logger.LogInformation(e.Message);
        }
        catch (Exception e)
        {
            Logger.LogError(e.Message);
            Logger.LogDebug(e.StackTrace);
            System.Windows.MessageBox.Show("自动风行迷踪时异常：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
            TaskSettingsPageViewModel.SetSwitchAutoWoodButtonText(false);
            Logger.LogInformation("← {Text}", "退出自动风行迷踪");
            TaskTriggerDispatcher.Instance().StartTimer();

            if (hasLock)
            {
                TaskSemaphore.Release();
            }
        }
    }

    private void Play(WindtraceTaskParam taskParam)
    {
        // 1. 进入活动界面
        EnterPanel(taskParam);

        // 2. 开始游戏
        EnterGame(taskParam);

        // 3. 游戏中操作
        InGame(taskParam);

        // 4. 结算操作
        AfterGame(taskParam);

        // 手动 GC
        GC.Collect();
    }

    private void EnterPanel(WindtraceTaskParam taskParam)
    {
        NewRetry.Do(() =>
        {
            Sleep(5, taskParam.Cts);
            using var fRectArea = GetRectAreaFromDispatcher().Find(AutoPickAssets.Instance.FRo);
            if (fRectArea.IsEmpty())
            {
                throw new RetryException("未找到「风行迷踪」对话");
            }
            else
            {
                Logger.LogInformation("检测到交互键");
                Simulation.SendInput.Keyboard.KeyPress(VK.VK_F);
                Debug.WriteLine("[AutoWindtrace] Enter panel");
            }
            
            Sleep(500, taskParam.Cts);
        }, TimeSpan.FromSeconds(1), 120);


        Sleep(300, taskParam.Cts);
        //Sleep(TaskContext.Instance().Config.AutoWindtraceConfig.PanelSleepDelay, taskParam.Cts);
    }

    private void EnterGame(WindtraceTaskParam taskParam)
    {
        try
        {
            NewRetry.Do(() =>
            {
                Sleep(1, taskParam.Cts);
                using var contentRegion = CaptureToRectArea();
                using var ra = contentRegion.Find(_assets.EnterButton);
                //TODO: 此处检测并更新代币数量
                if (ra.IsEmpty())
                {
                    throw new NormalEndException("未找到「活动详情」按钮！");
                }
            }, TimeSpan.FromSeconds(1), 3);
        }
        catch (Exception e)
        {
            Logger.LogInformation(e.Message);
            Logger.LogInformation("仍旧点击进入游戏按钮");
        }

        NewRetry.Do(() =>
        {
            Sleep(1, taskParam.Cts);
            using var contentRegion = CaptureToRectArea();
            using var ra = contentRegion.Find(_assets.JoinBututon);
            if (ra.IsEmpty())
            {
                throw new RetryException("未找到「前往游戏」按钮！");
            }
            else
            {
                ra.Click();
                Debug.WriteLine("[AutoWindtrace] Click confirm join button");
            }
        }, TimeSpan.FromSeconds(1), 3);

        NewRetry.Do(() =>
        {
            Sleep(1, taskParam.Cts);
            using var contentRegion = CaptureToRectArea();
            using var ra = contentRegion.Find(_assets.ConfirmJoinBututon);
            if (ra.IsEmpty())
            {
                throw new RetryException("未找到「接受」按钮！");
            }
            else
            {
                ra.Click();
                Debug.WriteLine("[AutoWindtrace] Click confirm join button");
            }
        }, TimeSpan.FromSeconds(1), 3);

        NewRetry.Do(() =>
        {
            Sleep(1, taskParam.Cts);
            using var contentRegion = CaptureToRectArea();
            using var ra = contentRegion.Find(_assets.ConfirmStartBututon);
            if (ra.IsEmpty())
            {
                throw new RetryException("未找到「准备就绪」按钮！");
            }
            else
            {
                ra.Click();
                Debug.WriteLine("[AutoWindtrace] Click confirm join button");
            }
        }, TimeSpan.FromSeconds(1), 3);

    }

    private async void InGame(WindtraceTaskParam taskParam)
    {
        if (taskParam.Cts.Token.IsCancellationRequested)
        {
            return;
        }
        var moveCts = new CancellationTokenSource();
        
        Task.Run(() => { onGameFinished(taskParam.Cts,moveCts); });//vs提示我这里应该await？

        Move(VK.VK_W, 20, taskParam.Cts).Wait();//自动副本任务的移动代码用了async&await，但我不清楚其目的。如果此处不需要异步，请指正。
        while (true)
        {
            await Move(VK.VK_A, 10, moveCts);
            if (moveCts.Token.IsCancellationRequested) break;
            await Move(VK.VK_S, 10, moveCts);
            if (moveCts.Token.IsCancellationRequested) break;
            await Move(VK.VK_D, 10, moveCts);
            if (moveCts.Token.IsCancellationRequested) break;
            await Move(VK.VK_W, 10, moveCts);
            if (moveCts.Token.IsCancellationRequested) break;
        }
    }

    private void AfterGame(WindtraceTaskParam taskParam)
    {
        //理论上自动退出，什么都不需要做
    }

    private async Task Move(VK key,int seconds, CancellationTokenSource Cts)
    {
        await Task.Run(() =>
        {
            Simulation.SendInput.Keyboard.KeyDown(key);
            Sleep(20, Cts);
            Simulation.SendInput.Keyboard.KeyDown(VK.VK_SHIFT);
            Sleep(seconds * 1000, Cts);
            Simulation.SendInput.Keyboard.KeyUp(key);
            Sleep(20, Cts);
            Simulation.SendInput.Keyboard.KeyUp(VK.VK_SHIFT);
        });
    }

    private async Task onGameFinished(CancellationTokenSource paramCts, CancellationTokenSource moveCts)
    {
        while (!paramCts.Token.IsCancellationRequested && !moveCts.Token.IsCancellationRequested)
        {
            if (isGameFinished())
            {
                moveCts.Cancel();
                break;
            }

            try
            {
                await Task.Delay(1000, paramCts.Token); // 每秒检查一次游戏结束
            }
            catch (TaskCanceledException) { }
        }
    }

    private bool isGameFinished()
    {
        //TODO: OCR结束界面
        return true;
    }
}
