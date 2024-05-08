using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Model.Enum;
using BetterGenshinImpact.View.Drawable;
using BetterGenshinImpact.ViewModel.Pages;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Aruco;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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

    private readonly ConcurrentDictionary<User32.VK, int> _keyX = new()
    {
        [User32.VK.VK_A] = 417,
        [User32.VK.VK_S] = 632,
        [User32.VK.VK_D] = 846,
        [User32.VK.VK_J] = 1065,
        [User32.VK.VK_K] = 1282,
        [User32.VK.VK_L] = 1497
    };

    private readonly int _keyY = 916;

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

            var gameCaptureRegion = CaptureToRectArea();
            ConcurrentDictionary<User32.VK, Point> posDic = new();

            foreach (var keyValuePair in _keyX)
            {
                var (x, y) = gameCaptureRegion.ConvertPositionToGameCaptureRegion(keyValuePair.Value, _keyY);
                posDic[keyValuePair.Key] = new Point(x, y);
            }

            await Task.Run(() =>
            {
                try
                {
                    while (!_taskParam.Cts.Token.IsCancellationRequested)
                    {
                        NotWhiteWin32(posDic);
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

    private void NotWhiteWin32(ConcurrentDictionary<User32.VK, Point> posDic)
    {
        Parallel.ForEach(posDic, kvp => { WhitePressWin32(kvp.Key, kvp.Value); });

        // foreach (var keyValuePair in posDic)
        // {
        //     WhitePressWin32(keyValuePair.Key, keyValuePair.Value);
        // }
    }

    private void WhitePressWin32(User32.VK key, Point point)
    {
        Stopwatch sw = new();
        sw.Start();
        var hdc = User32.GetDC(TaskContext.Instance().GameHandle);
        var c = Gdi32.GetPixel(hdc, point.X, point.Y);

        if (c.B < 220)
        {
            KeyDown(key);
        }
        else
        {
            KeyUp(key);
        }
        Gdi32.DeleteDC(hdc);
        sw.Stop();
        Debug.WriteLine($"GetPixel 耗时：{sw.ElapsedMilliseconds} （{point.X},{point.Y}）颜色{c.R},{c.G},{c.B}");
    }

    private void NotWhite()
    {
        var gameCaptureRegion = CaptureToRectArea();
        var srcMat = gameCaptureRegion.SrcMat;
        Parallel.ForEach(_keyX, kvp => { WhitePress(srcMat, kvp.Key, kvp.Value); });
        gameCaptureRegion.Dispose();
    }

    private unsafe void WhitePress(Mat srcMat, User32.VK key, int x)
    {
        // 获取图像的指针
        byte* ptr = (byte*)srcMat.Data.ToPointer();

        // 计算特定坐标下的像素偏移量
        long offset = srcMat.Step() * _keyY + srcMat.Channels() * x;

        // 获取像素的 RGB 值
        // byte r = ptr[offset];
        byte g = ptr[offset + 1];
        // byte b = ptr[offset + 2];
        if (g < 240)
        {
            KeyDown(key);
        }
        else
        {
            KeyUp(key);
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
            if (srcMat.Height - rect.Height - rect.Y <= 150 * assetScale)
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
        Simulation.SendInputEx.Keyboard.KeyUp(key);
    }

    private void KeyDown(User32.VK key)
    {
        Simulation.SendInputEx.Keyboard.KeyDown(key);
    }

    private void KeyUpOnce(User32.VK key)
    {
        if (_keyStatus.TryGetValue(key, out var v))
        {
            if (v)
            {
                _keyStatus[key] = false;
                _simulator.KeyUp(key);
            }
        }
    }

    private void KeyDownOnce(User32.VK key)
    {
        if (_keyStatus.TryGetValue(key, out var v))
        {
            if (!v)
            {
                _keyStatus[key] = true;
                _simulator.KeyDown(key);
            }
        }
        else
        {
            _simulator.KeyDown(key);
        }
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
