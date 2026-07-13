using BetterGenshinImpact.Core.Config;
using Fischless.WindowsInput;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace BetterGenshinImpact.Core.Simulator.Extensions;

/// <summary>
/// 为<seealso cref="PostMessageSimulator"/>提供扩展功能
/// </summary>
public static class PostMessageSimulatorExtension
{

    /// <summary>
    /// 模拟玩家操作
    /// </summary>
    /// <param name="action">动作</param>
    /// <param name="type">按键类型</param>
    public static PostMessageSimulator SimulateAction(this PostMessageSimulator self, GIActions action, KeyType type = KeyType.KeyPress)
    {
        var key = action.ToActionKey();
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
        return self;
    }

    private static void HoldKeyPress(PostMessageSimulator self, KeyId key)
    {
        self.LongKeyPress(key.ToVK());
    }

    private static void KeyPress(PostMessageSimulator self, KeyId key)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
                self.LeftButtonClick();
                break;
            case KeyId.MouseRightButton:
                self.RightButtonClick();
                break;
            case KeyId.MouseMiddleButton:
            case KeyId.MouseSideButton1:
            case KeyId.MouseSideButton2:
                break;
            default:
                self.KeyPress(key.ToVK());
                break;
        }
    }

    private static void KeyDown(PostMessageSimulator self, KeyId key)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
                self.LeftButtonDown();
                break;
            case KeyId.MouseRightButton:
                self.RightButtonDown();
                break;
            case KeyId.MouseMiddleButton:
            case KeyId.MouseSideButton1:
            case KeyId.MouseSideButton2:
                break;
            default:
                self.KeyDown(key.ToVK());
                break;
        }
    }

    private static void KeyUp(PostMessageSimulator self, KeyId key)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
                self.LeftButtonUp();
                break;
            case KeyId.MouseRightButton:
                self.RightButtonUp();
                break;
            case KeyId.MouseMiddleButton:
            case KeyId.MouseSideButton1:
            case KeyId.MouseSideButton2:
                break;
            default:
                self.KeyUp(key.ToVK());
                break;
        }
    }

    /// <summary>
    /// 模拟玩家操作（后台）
    /// </summary>
    /// <param name="action">动作</param>
    /// <param name="type">按键类型</param>
    public static PostMessageSimulator SimulateActionBackground(this PostMessageSimulator self, GIActions action, KeyType type = KeyType.KeyPress)
    {
        var key = action.ToActionKey();
        switch (type)
        {
            case KeyType.KeyPress:
                KeyPressBackground(self, key);
                break;
            case KeyType.KeyDown:
                KeyDownBackground(self, key);
                break;
            case KeyType.KeyUp:
                KeyUpBackground(self, key);
                break;
            case KeyType.Hold:
                break;
            default:
                break;
        }
        return self;
    }

    private static void KeyPressBackground(PostMessageSimulator self, KeyId key)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
                self.LeftButtonClickBackground();
                break;
            case KeyId.MouseRightButton:
            case KeyId.MouseMiddleButton:
            case KeyId.MouseSideButton1:
            case KeyId.MouseSideButton2:
                break;
            default:
                self.KeyPressBackground(key.ToVK());
                break;
        }
    }

    private static void KeyDownBackground(PostMessageSimulator self, KeyId key)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
            case KeyId.MouseRightButton:
            case KeyId.MouseMiddleButton:
            case KeyId.MouseSideButton1:
            case KeyId.MouseSideButton2:
                break;
            default:
                self.KeyDownBackground(key.ToVK());
                break;
        }
    }

    private static void KeyUpBackground(PostMessageSimulator self, KeyId key)
    {
        switch (key)
        {
            case KeyId.None:
            case KeyId.Unknown:
                break;
            case KeyId.MouseLeftButton:
            case KeyId.MouseRightButton:
            case KeyId.MouseMiddleButton:
            case KeyId.MouseSideButton1:
            case KeyId.MouseSideButton2:
                break;
            default:
                self.KeyUpBackground(key.ToVK());
                break;
        }
    }

}
