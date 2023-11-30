using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoWood.Assets;
using BetterGenshinImpact.GameTask.AutoWood.Utils;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using WindowsInput;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoWood;

/// <summary>
/// 自动伐木
/// </summary>
public class AutoWoodTask
{
    private readonly AutoWoodAssets _assets = new();

    private bool _first = true;

    private readonly ClickOffset _clickOffset;

    private readonly Login3rdParty _login3rdParty;

    public AutoWoodTask()
    {
        var captureArea = TaskContext.Instance().SystemInfo.CaptureAreaRect;
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        _clickOffset = new ClickOffset(captureArea.X, captureArea.Y, assetScale);
        _login3rdParty = new();
    }

    public void Start(WoodTaskParam taskParam)
    {
        try
        {
            Logger.LogInformation("→ {Text} 设置伐木总次数：{Cnt}", "自动伐木，启动！", taskParam.WoodRoundNum);

            _login3rdParty.RefreshAvailabled();
            if (_login3rdParty.Type == Login3rdParty.The3rdPartyType.Bilibili)
            {
                Logger.LogInformation("自动伐木启用B服模式");
            }
            SystemControl.ActivateWindow();
            for (var i = 0; i < taskParam.WoodRoundNum; i++)
            {
                Logger.LogInformation("第{Cnt}次伐木", i + 1);
                if (taskParam.Cts.IsCancellationRequested)
                {
                    break;
                }

                Felling(taskParam);
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
            Logger.LogInformation(e.Message);
            MessageBox.Show("自动伐木时异常：" + e.Source + "\r\n--" + Environment.NewLine + e.StackTrace + "\r\n---" + Environment.NewLine + e.Message);
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
            TaskSettingsPageViewModel.SetSwitchAutoWoodButtonText(false);
            Logger.LogInformation("← {Text}", "退出自动伐木");
            taskParam.Dispatcher.StartTimer();
        }
    }

    private void Felling(WoodTaskParam taskParam)
    {
        // 1. 按 z 触发「王树瑞佑」
        PressZ(taskParam);

        // 2. 按下 ESC 打开菜单 并退出游戏
        PressEsc(taskParam);

        // 3. 等待进入游戏
        EnterGame(taskParam);
    }

    private void PressZ(WoodTaskParam taskParam)
    {
        // IMPORTANT: MUST try focus before press Z
        SystemControl.Focus(TaskContext.Instance().GameHandle);

        if (_first)
        {
            var content = CaptureToContent(taskParam.Dispatcher.GameCapture);
            var ra = content.CaptureRectArea.Find(_assets.TheBoonOfTheElderTreeRo);
            if (ra.IsEmpty())
            {
#if !TEST_WITHOUT_Z_ITEM
                throw new NormalEndException("请先装备小道具「王树瑞佑」！");
#else
                Thread.Sleep(2000);
                Simulation.SendInput.Keyboard.KeyPress(VirtualKeyCode.VK_Z);
                Debug.WriteLine("[AutoWood] Z");
                _first = false;
#endif
            }
            else
            {
                Simulation.SendInput.Keyboard.KeyPress(VirtualKeyCode.VK_Z);
                Debug.WriteLine("[AutoWood] Z");
                _first = false;
            }
        }
        else
        {
            NewRetry.Do(() =>
            {
                Sleep(1, taskParam.Cts);
                var content = CaptureToContent(taskParam.Dispatcher.GameCapture);
                var ra = content.CaptureRectArea.Find(_assets.TheBoonOfTheElderTreeRo);
                if (ra.IsEmpty())
                {
#if !TEST_WITHOUT_Z_ITEM
                    throw new RetryException("未找到「王树瑞佑」");
#else
                    Thread.Sleep(15000);
#endif
                }

                Simulation.SendInput.Keyboard.KeyPress(VirtualKeyCode.VK_Z);
                Debug.WriteLine("[AutoWood] Z");
                Sleep(500, taskParam.Cts);
            }, TimeSpan.FromSeconds(1), 120);
        }

        Sleep(300, taskParam.Cts);
    }

    private void PressEsc(WoodTaskParam taskParam)
    {
        Simulation.SendInput.Keyboard.KeyPress(VirtualKeyCode.ESCAPE);
        Debug.WriteLine("[AutoWood] Esc");
        Sleep(800, taskParam.Cts);
        // 确认在菜单界面
        try
        {
            NewRetry.Do(() =>
            {
                Sleep(1, taskParam.Cts);
                var content = CaptureToContent(taskParam.Dispatcher.GameCapture);
                var ra = content.CaptureRectArea.Find(_assets.MenuBagRo);
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
        _clickOffset.Click((int)(50 * assetScale), captureArea.Height - (int)(50 * assetScale));
        Debug.WriteLine("[AutoWood] Click exit button");

        Sleep(500, taskParam.Cts);

        // 点击确认
        var content = CaptureToContent(taskParam.Dispatcher.GameCapture);
        content.CaptureRectArea.Find(_assets.ConfirmRo, ra =>
        {
            ra.ClickCenter();
            Debug.WriteLine("[AutoWood] Click confirm button");
        });
    }

    private void EnterGame(WoodTaskParam taskParam)
    {
        if (_login3rdParty.IsAvailabled)
        {
            Sleep(1, taskParam.Cts);
            _login3rdParty.Login(taskParam.Cts);
        }

        NewRetry.Do(() =>
        {
            Sleep(1, taskParam.Cts);

            var content = CaptureToContent(taskParam.Dispatcher.GameCapture);
            var ra = content.CaptureRectArea.Find(_assets.EnterGameRo);
            if (ra.IsEmpty())
            {
                throw new RetryException("未检测进入游戏字样");
            }
            else
            {
                Simulation.SendInput.Mouse.LeftButtonClick();
                Sleep(5000, taskParam.Cts);
                Debug.WriteLine("[AutoWood] Click entry text");
            }
        }, TimeSpan.FromSeconds(1), 50);
    }
}
