using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.Common;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;
using BetterGenshinImpact.ViewModel.Pages;
using Fischless.WindowsInput;
using Microsoft.Extensions.Logging;

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
        var vk = KeyBindingsSettingsPageViewModel.MappingKey(ToVk(key));
        switch (key)
        {
            case "VK_LBUTTON":
                Simulation.SendInput.Mouse.LeftButtonDown();
                break;
            case "VK_RBUTTON":
                Simulation.SendInput.Mouse.RightButtonDown();
                break;
            case "VK_MBUTTON":
                Simulation.SendInput.Mouse.MiddleButtonDown();
                break;
            case "VK_XBUTTON1":
                Simulation.SendInput.Mouse.XButtonDown(0x0001);
                break;
            case "VK_XBUTTON2":
                Simulation.SendInput.Mouse.XButtonDown(0x0001);
                break;
            default:
                if (InputBuilder.IsExtendedKey(vk))
                {
                    Simulation.SendInput.Keyboard.KeyDown(false, vk);
                }
                else
                {
                    Simulation.SendInput.Keyboard.KeyDown(vk);
                }

                break;
        }
    }

    public static void KeyUp(string key)
    {
        var vk = KeyBindingsSettingsPageViewModel.MappingKey(ToVk(key));
        switch (key)
        {
            case "VK_LBUTTON":
                Simulation.SendInput.Mouse.LeftButtonUp();
                break;
            case "VK_RBUTTON":
                Simulation.SendInput.Mouse.RightButtonUp();
                break;
            case "VK_MBUTTON":
                Simulation.SendInput.Mouse.MiddleButtonUp();
                break;
            case "VK_XBUTTON1":
                Simulation.SendInput.Mouse.XButtonUp(0x0001);
                break;
            case "VK_XBUTTON2":
                Simulation.SendInput.Mouse.XButtonUp(0x0001);
                break;
            default:
                if (InputBuilder.IsExtendedKey(vk))
                {
                    Simulation.SendInput.Keyboard.KeyUp(false, vk);
                }
                else
                {
                    Simulation.SendInput.Keyboard.KeyUp(vk);
                }

                break;
        }
    }

    public static void KeyPress(string key)
    {
        var vk = KeyBindingsSettingsPageViewModel.MappingKey(ToVk(key));
        switch (key)
        {
            case "VK_LBUTTON":
                Simulation.SendInput.Mouse.LeftButtonClick();
                break;
            case "VK_RBUTTON":
                Simulation.SendInput.Mouse.RightButtonClick();
                break;
            case "VK_MBUTTON":
                Simulation.SendInput.Mouse.MiddleButtonClick();
                break;
            case "VK_XBUTTON1":
                Simulation.SendInput.Mouse.XButtonClick(0x0001);
                break;
            case "VK_XBUTTON2":
                Simulation.SendInput.Mouse.XButtonClick(0x0001);
                break;
            default:
                if (InputBuilder.IsExtendedKey(vk))
                {
                    Simulation.SendInput.Keyboard.KeyPress(false, vk);
                }
                else
                {
                    Simulation.SendInput.Keyboard.KeyPress(vk);
                }
                
                break;
        }
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

    public static void VerticalScroll(int scrollAmountInClicks)
    {
        Simulation.SendInput.Mouse.VerticalScroll(scrollAmountInClicks);
    }

    #endregion 鼠标操作

    #region 识图操作

    public static ImageRegion CaptureGameRegion()
    {
        return TaskControl.CaptureToRectArea();
    }

    public static string[] GetAvatars()
    {
        var combatScenes = new CombatScenes().InitializeTeam(CaptureGameRegion());
        ReadOnlyCollection<Avatar> avatars = combatScenes.GetAvatars();
        return avatars.Count > 0
            ? avatars.Select(avatar => avatar.Name).ToArray()
            : [];
    }
    #endregion 识图操作

    #region 文字输入操作

    public static void InputText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // 保存当前剪贴板内容 保存恢复的功能不太正常
        // string? originalClipboardText = null;
        // UIDispatcherHelper.Invoke(() => originalClipboardText = Clipboard.GetText());
        try
        {
            // 将要输入的文本复制到剪贴板
            UIDispatcherHelper.Invoke(() => Clipboard.SetDataObject(text));

            // 模拟Ctrl+V粘贴操作
            Simulation.SendInput.Keyboard.KeyDown(false, VK.VK_CONTROL);
            Sleep(20);
            Simulation.SendInput.Keyboard.KeyPress(VK.VK_V);
            Sleep(20);
            Simulation.SendInput.Keyboard.KeyUp(false, VK.VK_CONTROL);

            // 等待一小段时间确保粘贴完成
            Sleep(100);
        }
        catch (Exception ex)
        {
            TaskControl.Logger.LogDebug("输入文本时发生错误: {Msg}", ex.Message);
        }
        finally
        {
            // // 恢复原始剪贴板内容
            // if (!string.IsNullOrEmpty(originalClipboardText))
            // {
            //     UIDispatcherHelper.Invoke(() => Clipboard.SetDataObject(originalClipboardText));
            // }
        }
    }

    #endregion 文字输入操作
}