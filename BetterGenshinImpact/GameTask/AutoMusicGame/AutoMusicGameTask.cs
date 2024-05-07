using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model.Enum;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoMusicGame;

public class AutoMusicGameTask
{
    private readonly AutoMusicGameParam _taskParam;

    private readonly int _judgeLineDisY; // 判定线的Y相对底部坐标
    private readonly int[] _judgeLineX;
    private readonly User32.VK[] _keyMap;
    private readonly Dictionary<User32.VK, bool> _keyStatus = new Dictionary<User32.VK, bool>();
    private readonly double assetScale;

    private PostMessageSimulator _simulator;

    public AutoMusicGameTask(AutoMusicGameParam taskParam)
    {
        _taskParam = taskParam;

        assetScale = TaskContext.Instance().SystemInfo.AssetScale;
        var rect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        var w = rect.Width;
        _judgeLineDisY = (int)(130 * assetScale); // 127 是线的位置
        int k1 = (int)(520 * assetScale);
        int k2 = (int)(745 * assetScale);
        _judgeLineX = [0, k1, k2, w / 2, w - k2, w - k1, w];
        _keyMap = [User32.VK.VK_A, User32.VK.VK_S, User32.VK.VK_D, User32.VK.VK_J, User32.VK.VK_K, User32.VK.VK_L];

        _simulator = Simulation.PostMessage(TaskContext.Instance().GameHandle);
    }

    public async void Start()
    {
        var hasLock = false;
        try
        {
            hasLock = await TaskSemaphore.WaitAsync(0);
            if (!hasLock)
            {
                Logger.LogError("启动自动战斗功能失败：当前存在正在运行中的独立任务，请不要重复执行任务！");
                return;
            }

            Init();

            await Task.Run(() =>
            {
                try
                {
                    while (!_taskParam.Cts.Token.IsCancellationRequested)
                    {
                        AllPress();
                    }
                }
                catch (Exception e)
                {
                    Logger.LogWarning(e.Message);
                    throw;
                }
            });
        }
        catch (NormalEndException)
        {
            Logger.LogInformation("手动中断自动活动音游");
        }
        catch (Exception e)
        {
            Logger.LogError(e.Message);
            Logger.LogDebug(e.StackTrace);
        }
        finally
        {
            VisionContext.Instance().DrawContent.ClearAll();
            TaskTriggerDispatcher.Instance().StartTimer();
            TaskSettingsPageViewModel.SetSwitchAutoFightButtonText(false);
            Logger.LogInformation("→ {Text}", "自动活动音游结束");

            if (hasLock)
            {
                TaskSemaphore.Release();
            }
        }
    }

    private void AllPress()
    {
        using var gameCaptureRegion = CaptureToRectArea();
        var srcMat = gameCaptureRegion.SrcMat[new Rect(0, gameCaptureRegion.Height - gameCaptureRegion.Height / 4, gameCaptureRegion.Width, gameCaptureRegion.Height / 4)];
        Parallel.Invoke(() => { PressPurple(srcMat); }, () => { PressYellow(srcMat); });
    }

    private void PressYellow(Mat srcMat)
    {
        ContoursHelper.FindSpecifyColorRects(srcMat, new Scalar(230, 176, 60), new Scalar(244, 191, 75), 50, 50).ForEach(rect =>
        {
            if (srcMat.Height - rect.Height - rect.Y < _judgeLineDisY)
            {
                var x = rect.X + rect.Width / 2;
                var key = GetKey(x);
                if (key != User32.VK.VK_0)
                {
                    KeyPress(key);
                }
                else
                {
                    Logger.LogWarning("X轴位找到对应值");
                }
            }
        });
    }

    private void PressPurple(Mat srcMat)
    {
        ContoursHelper.FindSpecifyColorRects(srcMat, new Scalar(143, 119, 238), new Scalar(163, 140, 245), 50, 50).ForEach(rect =>
        {
            if (srcMat.Height - rect.Height - rect.Y <= 135 * assetScale)
            {
                var x = rect.X + rect.Width / 2;
                var key = GetKey(x);
                if (key != User32.VK.VK_0)
                {
                    PurpleKeyPress(key);
                }
                else
                {
                    Logger.LogWarning("X轴位找到对应值");
                }
            }
        });
    }

    private void PurplePress()
    {
        using var gameCaptureRegion = CaptureToRectArea();
        var srcMat = gameCaptureRegion.SrcMat[new Rect(0, gameCaptureRegion.Height - gameCaptureRegion.Height / 4, gameCaptureRegion.Width, gameCaptureRegion.Height / 4)];
    }

    private User32.VK GetKey(int i)
    {
        for (int j = 0; j < _judgeLineX.Length - 1; j++)
        {
            if (_judgeLineX[j] <= i && i < _judgeLineX[j + 1])
            {
                return _keyMap[j];
            }
        }

        return User32.VK.VK_0;
    }

    private void KeyPress(User32.VK key)
    {
        _simulator.KeyPress(key);
    }

    private void PurpleKeyPress(User32.VK key)
    {
        if (_keyStatus.TryGetValue(key, out var v))
        {
            if (v)
            {
                KeyUp(key);
                _keyStatus[key] = false;
            }
            else
            {
                _keyStatus[key] = true;
                KeyDown(key);
            }
        }
        else
        {
            _keyStatus[key] = true;
            KeyDown(key);
        }
    }

    private void KeyUp(User32.VK key)
    {
        _simulator.KeyUp(key);
    }

    private void KeyDown(User32.VK key)
    {
        _simulator.KeyDown(key);
    }

    private void Init()
    {
        LogScreenResolution();
        Logger.LogInformation("→ {Text}", "活动音游，启动！");
        SystemControl.ActivateWindow();
        TaskTriggerDispatcher.Instance().StopTimer();
        Sleep(TaskContext.Instance().Config.TriggerInterval * 5, _taskParam.Cts); // 等待缓存图像
    }

    private void LogScreenResolution()
    {
        var gameScreenSize = SystemControl.GetGameScreenRect(TaskContext.Instance().GameHandle);
        if (gameScreenSize.Width * 9 != gameScreenSize.Height * 16)
        {
            Logger.LogWarning("游戏窗口分辨率不是 16:9 ！当前分辨率为 {Width}x{Height} , 非 16:9 分辨率的游戏可能无法正常使用自动活动音游功能 !", gameScreenSize.Width, gameScreenSize.Height);
        }
    }
}
