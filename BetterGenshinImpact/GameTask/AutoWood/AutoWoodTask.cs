using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoWood.Assets;
using BetterGenshinImpact.GameTask.AutoWood.Utils;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using System;
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
            SystemControl.ActivateWindow();
            for (var i = 0; i < taskParam.WoodRoundNum; i++)
            {
                Logger.LogInformation("第{Cnt}次伐木", i + 1);
                if (taskParam.Cts.IsCancellationRequested)
                {
                    break;
                }

                _login3rdParty.RefreshAvailabled();
                if (_login3rdParty.Type == Login3rdParty.The3rdPartyType.Bilibili)
                {
                    Logger.LogInformation("自动伐木启用B服模式");
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
        if (_first)
        {
            var content = CaptureToContent(taskParam.Dispatcher.GameCapture);
            var ra = content.CaptureRectArea.Find(_assets.TheBoonOfTheElderTreeRo);
            if (ra.IsEmpty())
            {
                throw new NormalEndException("请先装备小道具「王树瑞佑」！");
            }
            else
            {
                Simulation.SendInput.Keyboard.KeyPress(VirtualKeyCode.VK_Z);
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
                    throw new RetryException("未找到「王树瑞佑」");
                }

                Simulation.SendInput.Keyboard.KeyPress(VirtualKeyCode.VK_Z);
            }, TimeSpan.FromSeconds(1), 120);
        }

        Sleep(300, taskParam.Cts);
    }

    private void PressEsc(WoodTaskParam taskParam)
    {
        Simulation.SendInput.Keyboard.KeyPress(VirtualKeyCode.ESCAPE);
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

        Sleep(500, taskParam.Cts);

        // 点击确认
        var content = CaptureToContent(taskParam.Dispatcher.GameCapture);
        content.CaptureRectArea.Find(_assets.ConfirmRo, ra => { ra.ClickCenter(); });
    }

    private void EnterGame(WoodTaskParam taskParam)
    {
        NewRetry.Do(() =>
        {
            Sleep(1, taskParam.Cts);

            if (_login3rdParty.IsAvailabled)
            {
                _login3rdParty.Login(taskParam.Cts);
                Sleep(2000, taskParam.Cts);
            }

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
            }
        }, TimeSpan.FromSeconds(1), 50);
    }
}
