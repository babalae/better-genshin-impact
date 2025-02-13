using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Assets;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 队伍内的角色
/// </summary>
public class Avatar
{
    /// <summary>
    /// 角色名称 中文
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 角色名称 英文
    /// </summary>
    public string? NameEn { get; set; }

    /// <summary>
    /// 队伍内序号
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 武器类型
    /// </summary>
    public string Weapon { get; set; }

    /// <summary>
    /// 元素战技CD
    /// </summary>
    public double SkillCd { get; set; }

    /// <summary>
    /// 长按元素战技CD
    /// </summary>
    public double SkillHoldCd { get; set; }

    /// <summary>
    /// 最近一次使用元素战技的时间
    /// </summary>
    public DateTime LastSkillTime { get; set; }

    /// <summary>
    /// 元素爆发CD
    /// </summary>
    public double BurstCd { get; set; }

    /// <summary>
    /// 元素爆发是否就绪
    /// </summary>
    public bool IsBurstReady { get; set; }

    /// <summary>
    /// 名字所在矩形位置
    /// </summary>
    public Rect NameRect { get; set; }

    /// <summary>
    /// 名字右边的编号位置
    /// </summary>
    public Rect IndexRect { get; set; }

    /// <summary>
    /// 任务取消令牌
    /// </summary>
    public CancellationToken Ct { get; set; }

    /// <summary>
    /// 战斗场景
    /// </summary>
    public CombatScenes CombatScenes { get; set; }


    public Avatar(CombatScenes combatScenes, string name, int index, Rect nameRect)
    {
        CombatScenes = combatScenes;
        Name = name;
        Index = index;
        NameRect = nameRect;

        var ca = DefaultAutoFightConfig.CombatAvatarMap[name];
        NameEn = ca.NameEn;
        Weapon = ca.Weapon;
        SkillCd = ca.SkillCd;
        SkillHoldCd = ca.SkillHoldCd;
        BurstCd = ca.BurstCd;
    }

    /// <summary>
    /// 是否存在角色被击败
    /// 通过判断确认按钮
    /// </summary>
    /// <param name="region"></param>
    /// <returns></returns>
    public void ThrowWhenDefeated(ImageRegion region)
    {
        if (Bv.IsInRevivePrompt(region))
        {
            Logger.LogWarning("检测到复苏界面，存在角色被击败，前往七天神像复活");
            // 先打开地图
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE); // NOTE: 此处按下Esc是为了关闭复苏界面，无需改键
            Sleep(600, Ct);
            Simulation.SendInput.SimulateAction(GIActions.OpenMap);
            // tp 到七天神像复活
            var tpTask = new TpTask(Ct);
            tpTask.TpToStatueOfTheSeven().Wait(Ct);

            throw new RetryException("检测到复苏界面，存在角色被击败，前往七天神像复活");
        }
    }

    /// <summary>
    /// 切换到本角色
    /// 切换cd是1秒，如果切换失败，会尝试再次切换，最多尝试5次
    /// </summary>
    public void Switch()
    {
        for (var i = 0; i < 30; i++)
        {
            if (Ct is { IsCancellationRequested: true })
            {
                return;
            }

            var region = CaptureToRectArea();
            ThrowWhenDefeated(region);

            var notActiveCount = CombatScenes.Avatars.Count(avatar => !avatar.IsActive(region));
            if (IsActive(region) && notActiveCount == CombatScenes.ExpectedTeamAvatarNum - 1)
            {
                return;
            }

            switch (Index)
            {
                case 1:
                    Simulation.SendInput.SimulateAction(GIActions.SwitchMember1);
                    break;
                case 2:
                    Simulation.SendInput.SimulateAction(GIActions.SwitchMember2);
                    break;
                case 3:
                    Simulation.SendInput.SimulateAction(GIActions.SwitchMember3);
                    break;
                case 4:
                    Simulation.SendInput.SimulateAction(GIActions.SwitchMember4);
                    break;
                case 5:
                    Simulation.SendInput.SimulateAction(GIActions.SwitchMember5);
                    break;
                default:
                    break;
            }

            // Debug.WriteLine($"切换到{Index}号位");
            // Cv2.ImWrite($"log/切换.png", region.SrcMat);
            Sleep(250, Ct);
        }
    }

    /// <summary>
    /// 尝试切换到本角色
    /// </summary>
    /// <param name="tryTimes"></param>
    /// <param name="needLog"></param>
    /// <returns></returns>
    public bool TrySwitch(int tryTimes = 4, bool needLog = true)
    {
        for (var i = 0; i < 3; i++)
        {
            if (Ct is { IsCancellationRequested: true })
            {
                return false;
            }

            var region = CaptureToRectArea();
            ThrowWhenDefeated(region);

            var notActiveCount = CombatScenes.Avatars.Count(avatar => !avatar.IsActive(region));
            if (IsActive(region) && notActiveCount == CombatScenes.ExpectedTeamAvatarNum - 1)
            {
                if (needLog && i > 0)
                {
                    Logger.LogInformation("成功切换角色:{Name}", Name);
                }

                return true;
            }

            switch (Index)
            {
                case 1:
                    Simulation.SendInput.SimulateAction(GIActions.SwitchMember1);
                    break;
                case 2:
                    Simulation.SendInput.SimulateAction(GIActions.SwitchMember2);
                    break;
                case 3:
                    Simulation.SendInput.SimulateAction(GIActions.SwitchMember3);
                    break;
                case 4:
                    Simulation.SendInput.SimulateAction(GIActions.SwitchMember4);
                    break;
                case 5:
                    Simulation.SendInput.SimulateAction(GIActions.SwitchMember5);
                    break;
                default:
                    break;
            }

            Sleep(250, Ct);
        }

        return false;
    }

    /// <summary>
    /// 切换到本角色
    /// 切换cd是1秒，如果切换失败，会尝试再次切换，最多尝试5次
    /// </summary>
    public void SwitchWithoutCts()
    {
        for (var i = 0; i < 10; i++)
        {
            var region = CaptureToRectArea();
            ThrowWhenDefeated(region);

            var notActiveCount = CombatScenes.Avatars.Count(avatar => !avatar.IsActive(region));
            if (IsActive(region) && notActiveCount == 3)
            {
                return;
            }

            switch (Index)
            {
                case 1:
                    Simulation.SendInput.SimulateAction(GIActions.SwitchMember1);
                    break;
                case 2:
                    Simulation.SendInput.SimulateAction(GIActions.SwitchMember2);
                    break;
                case 3:
                    Simulation.SendInput.SimulateAction(GIActions.SwitchMember3);
                    break;
                case 4:
                    Simulation.SendInput.SimulateAction(GIActions.SwitchMember4);
                    break;
                case 5:
                    Simulation.SendInput.SimulateAction(GIActions.SwitchMember5);
                    break;
                default:
                    break;
            }

            Sleep(250);
        }
    }

    /// <summary>
    /// 是否出战状态
    /// </summary>
    /// <returns></returns>
    public bool IsActive(ImageRegion region)
    {
        if (IndexRect == Rect.Empty)
        {
            throw new Exception("IndexRect为空");
        }
        else
        {
            // 剪裁出IndexRect区域
            var indexRa = region.DeriveCrop(IndexRect);
            // Cv2.ImWrite($"log/indexRa_{Name}.png", indexRa.SrcMat);
            var count = OpenCvCommonHelper.CountGrayMatColor(indexRa.SrcGreyMat, 251, 255);
            if (count * 1.0 / (IndexRect.Width * IndexRect.Height) > 0.5)
            {
                return false;
            }
            else
            {
                return true;
            }
        }
    }

    /// <summary>
    /// 是否出战状态
    /// </summary>
    /// <returns></returns>
    [Obsolete]
    public bool IsActiveNoIndexRect(ImageRegion region)
    {
        // 通过寻找右侧人物编号来判断是否出战
        if (IndexRect == Rect.Empty)
        {
            var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
            // 剪裁出队伍区域
            var teamRa = region.DeriveCrop(AutoFightAssets.Instance.TeamRect);
            var blockX = NameRect.X + NameRect.Width * 2 - 10;
            var block = teamRa.DeriveCrop(new Rect(blockX, NameRect.Y, teamRa.Width - blockX, NameRect.Height * 2));
            // Cv2.ImWrite($"block_{Name}.png", block.SrcMat);
            // 取白色区域
            var bMat = OpenCvCommonHelper.Threshold(block.SrcMat, new Scalar(255, 255, 255), new Scalar(255, 255, 255));
            // Cv2.ImWrite($"block_b_{Name}.png", bMat);
            // 矩形识别
            Cv2.FindContours(bMat, out var contours, out _, RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);
            if (contours.Length > 0)
            {
                var boxes = contours.Select(Cv2.BoundingRect).Where(w => w.Width >= 20 * assetScale && w.Height >= 18 * assetScale).OrderByDescending(w => w.Width).ToList();
                if (boxes.Count is not 0)
                {
                    IndexRect = boxes.First();
                    return false;
                }
            }
        }
        else
        {
            // 剪裁出IndexRect区域
            var teamRa = region.DeriveCrop(AutoFightAssets.Instance.TeamRect);
            var blockX = NameRect.X + NameRect.Width * 2 - 10;
            var indexBlock = teamRa.DeriveCrop(new Rect(blockX + IndexRect.X, NameRect.Y + IndexRect.Y, IndexRect.Width, IndexRect.Height));
            // Cv2.ImWrite($"indexBlock_{Name}.png", indexBlock.SrcMat);
            var count = OpenCvCommonHelper.CountGrayMatColor(indexBlock.SrcGreyMat, 255);
            if (count * 1.0 / (IndexRect.Width * IndexRect.Height) > 0.5)
            {
                return false;
            }
        }

        Logger.LogInformation("{Name} 当前出战", Name);
        return true;
    }

    /// <summary>
    /// 普通攻击
    /// </summary>
    /// <param name="ms">攻击时长，建议是200的倍数</param>
    public void Attack(int ms = 0)
    {
        while (ms >= 0)
        {
            if (Ct is { IsCancellationRequested: true })
            {
                return;
            }

            Simulation.SendInput.SimulateAction(GIActions.NormalAttack);
            ms -= 200;
            Sleep(200, Ct);
        }
    }

    /// <summary>
    /// 使用元素战技 E
    /// </summary>
    public void UseSkill(bool hold = false)
    {
        for (var i = 0; i < 1; i++)
        {
            if (Ct is { IsCancellationRequested: true })
            {
                return;
            }

            if (hold)
            {
                if (Name == "纳西妲")
                {
                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
                    Sleep(300, Ct);
                    for (int j = 0; j < 10; j++)
                    {
                        Simulation.SendInput.Mouse.MoveMouseBy(1000, 0);
                        Sleep(50); // 持续操作不应该被cts取消
                    }

                    Sleep(300); // 持续操作不应该被cts取消
                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                }
                else if (Name == "坎蒂丝")
                {
                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyDown);
                    Thread.Sleep(3000);
                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyUp);
                }
                else
                {
                    Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.Hold);
                }
            }
            else
            {
                Simulation.SendInput.SimulateAction(GIActions.ElementalSkill, KeyType.KeyPress);
            }

            Sleep(200, Ct);

            var region = CaptureToRectArea();
            ThrowWhenDefeated(region);
            var cd = GetSkillCurrentCd(region);
            if (cd > 0)
            {
                Logger.LogInformation(hold ? "{Name} 长按元素战技，cd:{Cd}" : "{Name} 点按元素战技，cd:{Cd}", Name, cd);
                // todo 把cd加入执行队列
                LastSkillTime = DateTime.UtcNow;
                return;
            }
        }
    }

    /// <summary>
    /// 元素战技是否正在CD中
    /// 右下 267x132
    /// 77x77
    /// </summary>
    public double GetSkillCurrentCd(ImageRegion imageRegion)
    {
        var eRa = imageRegion.DeriveCrop(AutoFightAssets.Instance.ECooldownRect);
        var eRaWhite = OpenCvCommonHelper.InRangeHsv(eRa.SrcMat, new Scalar(0, 0, 235), new Scalar(0, 25, 255));
        var text = OcrFactory.Paddle.OcrWithoutDetector(eRaWhite);
        return StringUtils.TryParseDouble(text);
    }

    /// <summary>
    /// 使用元素爆发 Q
    /// Q释放等待 2s 超时认为没有Q技能
    /// </summary>
    public void UseBurst()
    {
        // var isBurstReleased = false;
        for (var i = 0; i < 10; i++)
        {
            if (Ct is { IsCancellationRequested: true })
            {
                return;
            }

            Simulation.SendInput.SimulateAction(GIActions.ElementalBurst);
            Sleep(200, Ct);

            var region = CaptureToRectArea();
            ThrowWhenDefeated(region);
            var notActiveCount = CombatScenes.Avatars.Count(avatar => !avatar.IsActive(region));
            if (notActiveCount == 0)
            {
                // isBurstReleased = true;
                Sleep(1500, Ct);
                return;
            }
            // else
            // {
            //     if (!isBurstReleased)
            //     {
            //         var cd = GetBurstCurrentCd(content);
            //         if (cd > 0)
            //         {
            //             Logger.LogInformation("{Name} 释放元素爆发，cd:{Cd}", Name, cd);
            //             // todo  把cd加入执行队列
            //             return;
            //         }
            //     }
            // }
        }
    }

    // /// <summary>
    // /// 元素爆发是否正在CD中
    // /// 右下 157x165
    // /// 110x110
    // /// </summary>
    // public double GetBurstCurrentCd(CaptureContent content)
    // {
    //     var qRa = content.CaptureRectArea.Crop(AutoFightAssets.Instance.QRect);
    //     var text = OcrFactory.Paddle.Ocr(qRa.SrcGreyMat);
    //     return StringUtils.TryParseDouble(text);
    // }

    /// <summary>
    /// 冲刺
    /// </summary>
    public void Dash(int ms = 0)
    {
        if (Ct is { IsCancellationRequested: true })
        {
            return;
        }

        if (ms == 0)
        {
            ms = 200;
        }

        Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyDown);
        Sleep(ms); // 冲刺不能被cts取消
        Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyUp);
    }

    public void Walk(string key, int ms)
    {
        if (Ct is { IsCancellationRequested: true })
        {
            return;
        }

        User32.VK vk = User32.VK.VK_NONAME;
        if (key == "w")
        {
            vk = GIActions.MoveForward.ToActionKey().ToVK();
        }
        else if (key == "s")
        {
            vk = GIActions.MoveBackward.ToActionKey().ToVK();
        }
        else if (key == "a")
        {
            vk = GIActions.MoveLeft.ToActionKey().ToVK();
        }
        else if (key == "d")
        {
            vk = GIActions.MoveRight.ToActionKey().ToVK();
        }

        if (vk == User32.VK.VK_NONAME)
        {
            return;
        }

        Simulation.SendInput.Keyboard.KeyDown(vk);
        Sleep(ms); // 行走不能被cts取消
        Simulation.SendInput.Keyboard.KeyUp(vk);
    }

    /// <summary>
    /// 移动摄像机
    /// </summary>
    /// <param name="pixelDeltaX">负数是左移，正数是右移</param>
    /// <param name="pixelDeltaY"></param>
    public void MoveCamera(int pixelDeltaX, int pixelDeltaY)
    {
        Simulation.SendInput.Mouse.MoveMouseBy(pixelDeltaX, pixelDeltaY);
    }

    /// <summary>
    /// 等待
    /// </summary>
    /// <param name="ms"></param>
    public void Wait(int ms)
    {
        Sleep(ms); // 由于存在宏操作，等待不应被cts取消
    }

    /// <summary>
    /// 跳跃
    /// </summary>
    public void Jump()
    {
        Simulation.SendInput.SimulateAction(GIActions.Jump);
    }

    /// <summary>
    /// 重击
    /// </summary>
    public void Charge(int ms = 0)
    {
        if (ms == 0)
        {
            ms = 1000;
        }

        if (Name == "那维莱特")
        {
            var dpi = TaskContext.Instance().DpiScale;
            Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyDown);
            while (ms >= 0)
            {
                if (Ct is { IsCancellationRequested: true })
                {
                    return;
                }

                Simulation.SendInput.Mouse.MoveMouseBy((int)(1000 * dpi), 0);
                ms -= 50;
                Sleep(50); // 持续操作不应该被cts取消
            }

            Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyUp);
        }
        else
        {
            Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyDown);
            Sleep(ms); // 持续操作不应该被cts取消
            Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyUp);
        }
    }

    public void MouseDown(string key = "left")
    {
        key = key.ToLower();
        if (key == "left")
        {
            Simulation.SendInput.Mouse.LeftButtonDown();
        }
        else if (key == "right")
        {
            Simulation.SendInput.Mouse.RightButtonDown();
        }
        else if (key == "middle")
        {
            Simulation.SendInput.Mouse.MiddleButtonDown();
        }
    }

    public void MouseUp(string key = "left")
    {
        key = key.ToLower();
        if (key == "left")
        {
            Simulation.SendInput.Mouse.LeftButtonUp();
        }
        else if (key == "right")
        {
            Simulation.SendInput.Mouse.RightButtonUp();
        }
        else if (key == "middle")
        {
            Simulation.SendInput.Mouse.MiddleButtonUp();
        }
    }

    public void Click(string key = "left")
    {
        key = key.ToLower();
        if (key == "left")
        {
            Simulation.SendInput.Mouse.LeftButtonClick();
        }
        else if (key == "right")
        {
            Simulation.SendInput.Mouse.RightButtonClick();
        }
        else if (key == "middle")
        {
            Simulation.SendInput.Mouse.MiddleButtonClick();
        }
    }

    public void MoveBy(int x, int y)
    {
        Simulation.SendInput.Mouse.MoveMouseBy(x, y);
    }

    public void KeyDown(string key)
    {
        var vk = MappingKey(User32Helper.ToVk(key));
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

    public void KeyUp(string key)
    {
        var vk = MappingKey(User32Helper.ToVk(key));
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

    public void KeyPress(string key)
    {
        var vk = MappingKey(User32Helper.ToVk(key));
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

    /// <summary>
    /// 根据默认键位，将脚本中写死的按键映射为实际的按键
    /// </summary>
    /// <param name="source"></param>
    /// <returns></returns>
    private static User32.VK MappingKey(User32.VK source)
    {
        if (TaskContext.Instance().Config.KeyBindingsConfig.GlobalKeyMappingEnabled)
        {
            // NOTE: 普攻、鼠标的冲刺、元素视野、视角居中和派蒙菜单不支持改键，此处不会进行映射
            return source switch
            {
                User32.VK.VK_W => GIActions.MoveForward.ToActionKey().ToVK(),
                User32.VK.VK_S => GIActions.MoveBackward.ToActionKey().ToVK(),
                User32.VK.VK_A => GIActions.MoveLeft.ToActionKey().ToVK(),
                User32.VK.VK_D => GIActions.MoveRight.ToActionKey().ToVK(),
                User32.VK.VK_LCONTROL => GIActions.SwitchToWalkOrRun.ToActionKey().ToVK(),
                User32.VK.VK_E => GIActions.ElementalSkill.ToActionKey().ToVK(),
                User32.VK.VK_Q => GIActions.ElementalBurst.ToActionKey().ToVK(),
                User32.VK.VK_LSHIFT => GIActions.SprintKeyboard.ToActionKey().ToVK(),
                User32.VK.VK_R => GIActions.SwitchAimingMode.ToActionKey().ToVK(),
                User32.VK.VK_SPACE => GIActions.Jump.ToActionKey().ToVK(),
                User32.VK.VK_X => GIActions.Drop.ToActionKey().ToVK(),
                User32.VK.VK_F => GIActions.PickUpOrInteract.ToActionKey().ToVK(),
                User32.VK.VK_Z => GIActions.QuickUseGadget.ToActionKey().ToVK(),
                User32.VK.VK_T => GIActions.InteractionInSomeMode.ToActionKey().ToVK(),
                User32.VK.VK_V => GIActions.QuestNavigation.ToActionKey().ToVK(),
                User32.VK.VK_P => GIActions.AbandonChallenge.ToActionKey().ToVK(),
                User32.VK.VK_1 => GIActions.SwitchMember1.ToActionKey().ToVK(),
                User32.VK.VK_2 => GIActions.SwitchMember2.ToActionKey().ToVK(),
                User32.VK.VK_3 => GIActions.SwitchMember3.ToActionKey().ToVK(),
                User32.VK.VK_4 => GIActions.SwitchMember4.ToActionKey().ToVK(),
                User32.VK.VK_5 => GIActions.SwitchMember5.ToActionKey().ToVK(),
                User32.VK.VK_TAB => GIActions.ShortcutWheel.ToActionKey().ToVK(),
                User32.VK.VK_B => GIActions.OpenInventory.ToActionKey().ToVK(),
                User32.VK.VK_C => GIActions.OpenCharacterScreen.ToActionKey().ToVK(),
                User32.VK.VK_M => GIActions.OpenMap.ToActionKey().ToVK(),
                User32.VK.VK_F1 => GIActions.OpenAdventurerHandbook.ToActionKey().ToVK(),
                User32.VK.VK_F2 => GIActions.OpenCoOpScreen.ToActionKey().ToVK(),
                User32.VK.VK_F3 => GIActions.OpenWishScreen.ToActionKey().ToVK(),
                User32.VK.VK_F4 => GIActions.OpenBattlePassScreen.ToActionKey().ToVK(),
                User32.VK.VK_F5 => GIActions.OpenTheEventsMenu.ToActionKey().ToVK(),
                User32.VK.VK_F6 => GIActions.OpenTheSettingsMenu.ToActionKey().ToVK(),
                User32.VK.VK_F7 => GIActions.OpenTheFurnishingScreen.ToActionKey().ToVK(),
                User32.VK.VK_F8 => GIActions.OpenStellarReunion.ToActionKey().ToVK(),
                User32.VK.VK_J => GIActions.OpenQuestMenu.ToActionKey().ToVK(),
                User32.VK.VK_Y => GIActions.OpenNotificationDetails.ToActionKey().ToVK(),
                User32.VK.VK_RETURN => GIActions.OpenChatScreen.ToActionKey().ToVK(),
                User32.VK.VK_U => GIActions.OpenSpecialEnvironmentInformation.ToActionKey().ToVK(),
                User32.VK.VK_G => GIActions.CheckTutorialDetails.ToActionKey().ToVK(),
                User32.VK.VK_LMENU => GIActions.ShowCursor.ToActionKey().ToVK(),
                User32.VK.VK_L => GIActions.OpenPartySetupScreen.ToActionKey().ToVK(),
                User32.VK.VK_O => GIActions.OpenFriendsScreen.ToActionKey().ToVK(),
                User32.VK.VK_OEM_2 => GIActions.HideUI.ToActionKey().ToVK(),
                // 其他按键（保留的？）不作转换
                // NOTE: 是否可以在脚本中增加类似编译器预处理指令的语法，使全局按键映射功能在执行特定脚本（如用户自行编写且不推送到中央仓库的自用脚本）时禁用
                _ => source,
            };
        }
        return source;
    }
}