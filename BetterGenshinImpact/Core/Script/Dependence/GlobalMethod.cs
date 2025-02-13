using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using System;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.Core.Config;

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
        var vk = MappingKey(ToVk(key));
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
                Simulation.SendInput.Keyboard.KeyDown(vk);
                break;
        }
    }

    public static void KeyUp(string key)
    {
        var vk = MappingKey(ToVk(key));
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
                Simulation.SendInput.Keyboard.KeyUp(vk);
                break;
        }
    }

    public static void KeyPress(string key)
    {
        var vk = MappingKey(ToVk(key));
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
                Simulation.SendInput.Keyboard.KeyPress(vk);
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

    /// <summary>
    /// 根据默认键位，将脚本中写死的按键映射为实际的按键
    /// </summary>
    /// <param name="source"></param>
    /// <returns></returns>
    private static VK MappingKey(VK source)
    {
        if (TaskContext.Instance().Config.KeyBindingsConfig.GlobalKeyMappingEnabled)
        {
            // NOTE: 普攻、鼠标的冲刺、元素视野、视角居中和派蒙菜单不支持改键，此处不会进行映射
            return source switch
            {
                VK.VK_W => GIActions.MoveForward.ToActionKey().ToVK(),
                VK.VK_S => GIActions.MoveBackward.ToActionKey().ToVK(),
                VK.VK_A => GIActions.MoveLeft.ToActionKey().ToVK(),
                VK.VK_D => GIActions.MoveRight.ToActionKey().ToVK(),
                VK.VK_LCONTROL => GIActions.SwitchToWalkOrRun.ToActionKey().ToVK(),
                VK.VK_E => GIActions.ElementalSkill.ToActionKey().ToVK(),
                VK.VK_Q => GIActions.ElementalBurst.ToActionKey().ToVK(),
                VK.VK_LSHIFT => GIActions.SprintKeyboard.ToActionKey().ToVK(),
                VK.VK_R => GIActions.SwitchAimingMode.ToActionKey().ToVK(),
                VK.VK_SPACE => GIActions.Jump.ToActionKey().ToVK(),
                VK.VK_X => GIActions.Drop.ToActionKey().ToVK(),
                VK.VK_F => GIActions.PickUpOrInteract.ToActionKey().ToVK(),
                VK.VK_Z => GIActions.QuickUseGadget.ToActionKey().ToVK(),
                VK.VK_T => GIActions.InteractionInSomeMode.ToActionKey().ToVK(),
                VK.VK_V => GIActions.QuestNavigation.ToActionKey().ToVK(),
                VK.VK_P => GIActions.AbandonChallenge.ToActionKey().ToVK(),
                VK.VK_1 => GIActions.SwitchMember1.ToActionKey().ToVK(),
                VK.VK_2 => GIActions.SwitchMember2.ToActionKey().ToVK(),
                VK.VK_3 => GIActions.SwitchMember3.ToActionKey().ToVK(),
                VK.VK_4 => GIActions.SwitchMember4.ToActionKey().ToVK(),
                VK.VK_5 => GIActions.SwitchMember5.ToActionKey().ToVK(),
                VK.VK_TAB => GIActions.ShortcutWheel.ToActionKey().ToVK(),
                VK.VK_B => GIActions.OpenInventory.ToActionKey().ToVK(),
                VK.VK_C => GIActions.OpenCharacterScreen.ToActionKey().ToVK(),
                VK.VK_M => GIActions.OpenMap.ToActionKey().ToVK(),
                VK.VK_F1 => GIActions.OpenAdventurerHandbook.ToActionKey().ToVK(),
                VK.VK_F2 => GIActions.OpenCoOpScreen.ToActionKey().ToVK(),
                VK.VK_F3 => GIActions.OpenWishScreen.ToActionKey().ToVK(),
                VK.VK_F4 => GIActions.OpenBattlePassScreen.ToActionKey().ToVK(),
                VK.VK_F5 => GIActions.OpenTheEventsMenu.ToActionKey().ToVK(),
                VK.VK_F6 => GIActions.OpenTheSettingsMenu.ToActionKey().ToVK(),
                VK.VK_F7 => GIActions.OpenTheFurnishingScreen.ToActionKey().ToVK(),
                VK.VK_F8 => GIActions.OpenStellarReunion.ToActionKey().ToVK(),
                VK.VK_J => GIActions.OpenQuestMenu.ToActionKey().ToVK(),
                VK.VK_Y => GIActions.OpenNotificationDetails.ToActionKey().ToVK(),
                VK.VK_RETURN => GIActions.OpenChatScreen.ToActionKey().ToVK(),
                VK.VK_U => GIActions.OpenSpecialEnvironmentInformation.ToActionKey().ToVK(),
                VK.VK_G => GIActions.CheckTutorialDetails.ToActionKey().ToVK(),
                VK.VK_LMENU => GIActions.ShowCursor.ToActionKey().ToVK(),
                VK.VK_L => GIActions.OpenPartySetupScreen.ToActionKey().ToVK(),
                VK.VK_O => GIActions.OpenFriendsScreen.ToActionKey().ToVK(),
                VK.VK_OEM_2 => GIActions.HideUI.ToActionKey().ToVK(),
                // 其他按键（保留的？）不作转换
                _ => source,
            };
        }
        return source;
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
