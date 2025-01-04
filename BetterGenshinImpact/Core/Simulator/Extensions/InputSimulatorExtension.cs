using BetterGenshinImpact.Core.Config;
using System;
using System.Collections.Generic;
using System.Text;
using BetterGenshinImpact.Core.Simulator;
using System.Threading;
using Fischless.WindowsInput;

namespace BetterGenshinImpact.Core.Simulator.Extensions;

/// <summary>
/// 用于扩展<seealso cref="Fischless.WindowsInput.InputSimulator"/>的功能
/// </summary>
public static class InputSimulatorExtension
{

    /// <summary>
    /// 模拟玩家操作
    /// </summary>
    /// <param name="action">动作</param>
    /// <param name="type">按键类型</param>
    public static void SimulateAction(this InputSimulator self, GIActions action, KeyType type = KeyType.KeyPress)
    {
        var key = SimulateKeyHelper.GetActionKey(action);
        switch (type)
        {
            case KeyType.KeyPress:
                KeyPress(self, key);
                break;
            case KeyType.KeyDown:
                KeyDown(self, key);
                break;
            case KeyType.KeyUp:
                KeyUp(self, key);
                break;
            case KeyType.Hold:
                HoldKeyPress(self, key);
                break;
            default:
                break;
        }
    }

    private static void HoldKeyPress(InputSimulator self, KeyId key)
    {
        KeyDown(self, key);
        Thread.Sleep(1000);
        KeyUp(self, key);
    }

    private static void KeyPress(InputSimulator self, KeyId key)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
                self.Mouse.LeftButtonClick();
                break;
            case KeyId.MouseRightButton:
                self.Mouse.RightButtonClick();
                break;
            case KeyId.MouseMiddleButton:
                self.Mouse.MiddleButtonClick();
                break;
            case KeyId.MouseSideButton1:
                self.Mouse.XButtonClick(0x0001);
                break;
            case KeyId.MouseSideButton2:
                self.Mouse.XButtonClick(0x0001);
                break;
            default:
                self.Keyboard.KeyPress(key.ToVK());
                break;
        }
    }

    private static void KeyDown(InputSimulator self, KeyId key)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
                self.Mouse.LeftButtonDown();
                break;
            case KeyId.MouseRightButton:
                self.Mouse.RightButtonDown();
                break;
            case KeyId.MouseMiddleButton:
                self.Mouse.MiddleButtonDown();
                break;
            case KeyId.MouseSideButton1:
                self.Mouse.XButtonDown(0x0001);
                break;
            case KeyId.MouseSideButton2:
                self.Mouse.XButtonDown(0x0001);
                break;
            default:
                self.Keyboard.KeyDown(key.ToVK());
                break;
        }
    }

    private static void KeyUp(InputSimulator self, KeyId key)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
                self.Mouse.LeftButtonUp();
                break;
            case KeyId.MouseRightButton:
                self.Mouse.RightButtonUp();
                break;
            case KeyId.MouseMiddleButton:
                self.Mouse.MiddleButtonUp();
                break;
            case KeyId.MouseSideButton1:
                self.Mouse.XButtonUp(0x0001);
                break;
            case KeyId.MouseSideButton2:
                self.Mouse.XButtonUp(0x0001);
                break;
            default:
                self.Keyboard.KeyUp(key.ToVK());
                break;
        }
    }

}
