using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using System;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Core.Script.Dependence;

public class GlobalMethod
{
    public static async Task Sleep(int millisecondsTimeout)
    {
        await Task.Delay(millisecondsTimeout, CancellationContext.Instance.Cts.Token);
    }

    #region 键盘操作

    public static void KeyDown(string key)
    {
        Simulation.SendInput.Keyboard.KeyDown(ToVk(key));
    }

    public static void KeyUp(string key)
    {
        Simulation.SendInput.Keyboard.KeyUp(ToVk(key));
    }

    public static void KeyPress(string key)
    {
        Simulation.SendInput.Keyboard.KeyPress(ToVk(key));
    }

    private static User32.VK ToVk(string key)
    {
        try
        {
            return User32Helper.ToVk(key);
        }
        catch
        {
            throw new ArgumentException($"键盘编码必须是VirtualKeyCodes枚举中的值，当前传入的 {key} 不合法");
        }
    }

    #endregion 键盘操作

    #region 鼠标操作

    private static int _gameWidth = 1920;
    private static int _gameHeight = 1080;
    private static double _dpi = 1;

    public static void SetGameMetrics(int width, int height, double dpi = 1)
    {
        // 必须16:9 的分辨率
        if (width * 9 != height * 16)
        {
            throw new ArgumentException("游戏分辨率必须是16:9的分辨率");
        }

        _gameWidth = width;
        _gameHeight = height;
        _dpi = dpi;
    }

    public static void MoveMouseBy(int x, int y)
    {
        var realDpi = TaskContext.Instance().DpiScale;
        x = (int)(x * realDpi / _dpi);
        y = (int)(y * realDpi / _dpi);
        Simulation.SendInput.Mouse.MoveMouseBy(x, y);
    }

    public static void MoveMouseTo(int x, int y)
    {
        if (x < 0 || x > _gameWidth || y < 0 || y > _gameHeight)
        {
            throw new ArgumentException("鼠标坐标超出游戏窗口范围");
        }

        GameCaptureRegion.GameRegionMove((size, s2) =>
        {
            var scale = 1920.0 / _gameWidth;
            return (x * scale * s2, y * scale * s2);
        });
    }

    public static void Click(int x, int y)
    {
        MoveMouseTo(x, y);
        LeftButtonClick();
    }

    public static void LeftButtonClick()
    {
        Simulation.SendInput.Mouse.LeftButtonDown().Sleep(60).LeftButtonUp();
    }

    public static void LeftButtonDown()
    {
        Simulation.SendInput.Mouse.LeftButtonDown();
    }

    public static void LeftButtonUp()
    {
        Simulation.SendInput.Mouse.LeftButtonUp();
    }

    public static void RightButtonClick()
    {
        Simulation.SendInput.Mouse.RightButtonDown().Sleep(60).RightButtonUp();
    }

    public static void RightButtonDown()
    {
        Simulation.SendInput.Mouse.RightButtonDown();
    }

    public static void RightButtonUp()
    {
        Simulation.SendInput.Mouse.RightButtonUp();
    }

    public static void MiddleButtonClick()
    {
        Simulation.SendInput.Mouse.MiddleButtonClick();
    }

    public static void MiddleButtonDown()
    {
        Simulation.SendInput.Mouse.MiddleButtonDown();
    }

    public static void MiddleButtonUp()
    {
        Simulation.SendInput.Mouse.MiddleButtonUp();
    }

    #endregion 鼠标操作

    #region 识图操作

    public static ImageRegion CaptureGameRegion()
    {
        return TaskControl.CaptureToRectArea();
    }

    #endregion 识图操作
}
