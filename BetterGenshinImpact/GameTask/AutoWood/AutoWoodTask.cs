using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoWood.Assets;
using BetterGenshinImpact.GameTask.AutoWood.Utils;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Genshin.Settings;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.User32;
using GC = System.GC;

namespace BetterGenshinImpact.GameTask.AutoWood;

/// <summary>
/// 自动伐木
/// </summary>
public class AutoWoodTask
{
    private readonly AutoWoodAssets _assets;

    private bool _first = true;

    private readonly ClickOffset _clickOffset;

    private readonly Login3rdParty _login3rdParty;

    private VK _zKey = VK.VK_Z;

    public AutoWoodTask()
    {
        var captureArea = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        _clickOffset = new ClickOffset(captureArea.X, captureArea.Y, assetScale);
        _login3rdParty = new();
        _assets = AutoWoodAssets.Instance;
    }

    public void Start(WoodTaskParam taskParam)
    {
        var hasLock = false;
        try
        {
            hasLock = TaskSemaphore.Wait(0);
            if (!hasLock)
            {
                Logger.LogError("启动自动伐木功能失败：当前存在正在运行中的独立任务，请不要重复执行任务！");
                return;
            }

            TaskTriggerDispatcher.Instance().StopTimer();
            Logger.LogInformation("→ {Text} 设置伐木总次数：{Cnt}", "自动伐木，启动！", taskParam.WoodRoundNum);

            _login3rdParty.RefreshAvailabled();
            if (_login3rdParty.Type == Login3rdParty.The3rdPartyType.Bilibili)
            {
                Logger.LogInformation("自动伐木启用B服模式");
            }

            SettingsContainer settingsContainer = new();

            if (settingsContainer.OverrideController?.KeyboardMap?.ActionElementMap.Where(item => item.ActionId == ActionId.Gadget).FirstOrDefault()?.ElementIdentifierId is ElementIdentifierId key)
            {
                if (key != ElementIdentifierId.Z)
                {
                    _zKey = key.ToVK();
                    Logger.LogInformation($"自动伐木检测到用户改键 {ElementIdentifierId.Z.ToName()} 改为 {key.ToName()}");
                    if (key == ElementIdentifierId.LeftShift || key == ElementIdentifierId.RightShift)
                    {
                        Logger.LogInformation($"用户改键 {key.ToName()} 可能不受模拟支持，若使用正常则忽略");
                    }
                }
            }

            SystemControl.ActivateWindow();
            for (var i = 0; i < taskParam.WoodRoundNum; i++)
            {
                Logger.LogInformation("第{Cnt}次伐木", i + 1);
                if (taskParam.Cts.IsCancellationRequested)
                {
                    break;
                }

                Felling(taskParam, i + 1 == taskParam.WoodRoundNum);
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
            System.Windows.MessageBox.Show("自动伐木时异常：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
            TaskSettingsPageViewModel.SetSwitchAutoWoodButtonText(false);
            Logger.LogInformation("← {Text}", "退出自动伐木");
            taskParam.Dispatcher.StartTimer();

            if (hasLock)
            {
                TaskSemaphore.Release();
            }
        }
    }

    private void Felling(WoodTaskParam taskParam, bool isLast = false)
    {
        // 1. 按 z 触发「王树瑞佑」
        PressZ(taskParam);

        if (isLast)
        {
            return;
        }

        // 2. 按下 ESC 打开菜单 并退出游戏
        PressEsc(taskParam);

        // 3. 等待进入游戏
        EnterGame(taskParam);

        // 手动 GC
        GC.Collect();
    }

    private void PressZ(WoodTaskParam taskParam)
    {
        // IMPORTANT: MUST try focus before press Z
        SystemControl.Focus(TaskContext.Instance().GameHandle);

        if (_first)
        {
            using var content = CaptureToContent(taskParam.Dispatcher.GameCapture);
            using var ra = content.CaptureRectArea.Find(_assets.TheBoonOfTheElderTreeRo);
            if (ra.IsEmpty())
            {
#if !TEST_WITHOUT_Z_ITEM
                throw new NormalEndException("请先装备小道具「王树瑞佑」！");
#else
                Thread.Sleep(2000);
                Simulation.SendInputEx.Keyboard.KeyPress(_zKey);
                Debug.WriteLine("[AutoWood] Z");
                _first = false;
#endif
            }
            else
            {
                Simulation.SendInputEx.Keyboard.KeyPress(_zKey);
                Debug.WriteLine("[AutoWood] Z");
                _first = false;
            }
        }
        else
        {
            NewRetry.Do(() =>
            {
                Sleep(1, taskParam.Cts);
                using var content = CaptureToContent(taskParam.Dispatcher.GameCapture);
                using var ra = content.CaptureRectArea.Find(_assets.TheBoonOfTheElderTreeRo);
                if (ra.IsEmpty())
                {
#if !TEST_WITHOUT_Z_ITEM
                    throw new RetryException("未找到「王树瑞佑」");
#else
                    Thread.Sleep(15000);
#endif
                }

                Simulation.SendInputEx.Keyboard.KeyPress(_zKey);
                Debug.WriteLine("[AutoWood] Z");
                Sleep(500, taskParam.Cts);
            }, TimeSpan.FromSeconds(1), 120);
        }

        Sleep(300, taskParam.Cts);
        Sleep(TaskContext.Instance().Config.AutoWoodConfig.AfterZSleepDelay, taskParam.Cts);
    }

    private void PressEsc(WoodTaskParam taskParam)
    {
        SystemControl.Focus(TaskContext.Instance().GameHandle);
        Simulation.SendInputEx.Keyboard.KeyPress(VK.VK_ESCAPE);
        Debug.WriteLine("[AutoWood] Esc");
        Sleep(800, taskParam.Cts);
        // 确认在菜单界面
        try
        {
            NewRetry.Do(() =>
            {
                Sleep(1, taskParam.Cts);
                using var content = CaptureToContent(taskParam.Dispatcher.GameCapture);
                using var ra = content.CaptureRectArea.Find(_assets.MenuBagRo);
                if (ra.IsEmpty())
                {
                    throw new RetryException("未检测到弹出菜单");
                }
            }, TimeSpan.FromSeconds(1), 3);
        }
        catch (Exception e)
        {
            Logger.LogInformation(e.Message);
            Logger.LogInformation("仍旧点击退出按钮");
        }

        // 点击退出
        var captureArea = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        _clickOffset.ClickWithoutScale((int)(50 * assetScale), captureArea.Height - (int)(50 * assetScale));
        Debug.WriteLine("[AutoWood] Click exit button");

        Sleep(500, taskParam.Cts);

        // 点击确认
        using var content = CaptureToContent(taskParam.Dispatcher.GameCapture);
        content.CaptureRectArea.Find(_assets.ConfirmRo, ra =>
        {
            ra.ClickCenter();
            Debug.WriteLine("[AutoWood] Click confirm button");
            ra.Dispose();
        });
    }

    private void EnterGame(WoodTaskParam taskParam)
    {
        if (_login3rdParty.IsAvailabled)
        {
            Sleep(1, taskParam.Cts);
            _login3rdParty.Login(taskParam.Cts);
        }

        var clickCnt = 0;
        for (var i = 0; i < 50; i++)
        {
            Sleep(1, taskParam.Cts);

            using var content = CaptureToContent(taskParam.Dispatcher.GameCapture);
            using var ra = content.CaptureRectArea.Find(_assets.EnterGameRo);
            if (!ra.IsEmpty())
            {
                clickCnt++;
                _clickOffset.Click(955, 666);
                Debug.WriteLine("[AutoWood] Click entry");
            }
            else
            {
                if (clickCnt > 2)
                {
                    Sleep(5000, taskParam.Cts);
                    break;
                }
            }

            Sleep(1000, taskParam.Cts);
        }

        if (clickCnt == 0)
        {
            throw new RetryException("未检测进入游戏界面");
        }
    }
}
