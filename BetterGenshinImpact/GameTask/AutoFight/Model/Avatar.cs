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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.ViewModel.Pages;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 队伍内的角色
/// </summary>
public class Avatar
{
    /// <summary>
    /// 配置文件中的角色信息
    /// </summary>
    public readonly CombatAvatar CombatAvatar;

    /// <summary>
    /// 角色名称 中文
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 队伍内序号
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 最近一次OCR识别出的CD到期时间
    /// </summary>
    private DateTime OcrSkillCd { get; set; }

    /// <summary>
    /// 手动配置的技能CD，有它就不使用OCR,小于0为自动
    /// </summary>
    public double ManualSkillCd { get; set; }

    /// <summary>
    /// 最近一次使用元素战技的时间
    /// </summary>
    public DateTime LastSkillTime { get; set; }

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

    public static string? LastActiveAvatar { get; internal set; } = null;


    public Avatar(CombatScenes combatScenes, string name, int index, Rect nameRect, double manualSkillCd = -1)
    {
        CombatScenes = combatScenes;
        Name = name;
        Index = index;
        NameRect = nameRect;
        CombatAvatar = DefaultAutoFightConfig.CombatAvatarMap[name];
        ManualSkillCd = manualSkillCd;
    }


    /// <summary>
    /// 是否存在角色被击败
    /// 通过判断确认按钮
    /// </summary>
    /// <param name="region"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public static void ThrowWhenDefeated(ImageRegion region, CancellationToken ct)
    {
        if (Bv.IsInRevivePrompt(region))
        {
            Logger.LogWarning("检测到复苏界面，存在角色被击败，前往七天神像复活");
            // 先打开地图
            Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE); // NOTE: 此处按下Esc是为了关闭复苏界面，无需改键
            Sleep(600, ct);
            TpForRecover(ct, new RetryException("检测到复苏界面，存在角色被击败，前往七天神像复活"));
        }
    }

    /// <summary>
    /// tp 到七天神像恢复
    /// </summary>
    /// <param name="ct"></param>
    /// <param name="ex"></param>
    /// <exception cref="RetryException"></exception>
    public static void TpForRecover(CancellationToken ct, Exception ex)
    {
        // tp 到七天神像复活
        var tpTask = new TpTask(ct);
        tpTask.TpToStatueOfTheSeven().Wait(ct);
        Logger.LogInformation("血量恢复完成。【设置】-【七天神像设置】可以修改回血相关配置。");
        throw ex;
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
            ThrowWhenDefeated(region, Ct);

            var notActiveCount = CombatScenes.GetAvatars().Count(avatar => !avatar.IsActive(region));
            if (IsActive(region) && notActiveCount == CombatScenes.ExpectedTeamAvatarNum - 1)
            {
                return;
            }

            Simulation.SendInput.SimulateAction(GIActions.Drop);
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
        for (var i = 0; i < tryTimes; i++)
        {
            if (Ct is { IsCancellationRequested: true })
            {
                return false;
            }

            var region = CaptureToRectArea();
            ThrowWhenDefeated(region, Ct);

            var notActiveCount = CombatScenes.GetAvatars().Count(avatar => !avatar.IsActive(region));
            if (IsActive(region) && notActiveCount == CombatScenes.ExpectedTeamAvatarNum - 1)
            {
                if (needLog && i > 0)
                {
                    LastActiveAvatar = Name;
                    Logger.LogInformation("成功切换角色:{Name}", Name);
                }

                return true;
            }

            Simulation.SendInput.SimulateAction(GIActions.Drop); //反正会重试就不等落地了
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
            ThrowWhenDefeated(region, Ct);

            var notActiveCount = CombatScenes.GetAvatars().Count(avatar => !avatar.IsActive(region));
            if (IsActive(region) && notActiveCount == 3)
            {
                return;
            }

            Simulation.SendInput.SimulateAction(GIActions.Drop);
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
        if (IndexRect == default)
        {
            throw new Exception("IndexRect为空");
        }
        else
        {
            // 剪裁出IndexRect区域
            var indexRa = region.DeriveCrop(IndexRect);
            // Cv2.ImWrite($"log/indexRa_{Name}.png", indexRa.SrcMat);
            var count = OpenCvCommonHelper.CountGrayMatColor(indexRa.CacheGreyMat, 251, 255);
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
        if (IndexRect == default)
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
                var boxes = contours.Select(Cv2.BoundingRect)
                    .Where(w => w.Width >= 20 * assetScale && w.Height >= 18 * assetScale)
                    .OrderByDescending(w => w.Width).ToList();
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
            var indexBlock = teamRa.DeriveCrop(new Rect(blockX + IndexRect.X, NameRect.Y + IndexRect.Y, IndexRect.Width,
                IndexRect.Height));
            // Cv2.ImWrite($"indexBlock_{Name}.png", indexBlock.SrcMat);
            var count = OpenCvCommonHelper.CountGrayMatColor(indexBlock.CacheGreyMat, 255);
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
                Simulation.SendInput.SimulateAction(GIActions.ElementalSkill);
            }

            Sleep(200, Ct);

            var region = CaptureToRectArea();
            ThrowWhenDefeated(region, Ct); // 检测是不是要跑神像
            var cd = AfterUseSkill(region);
            if (cd > 0)
            {
                Logger.LogInformation(hold ? "{Name} 长按元素战技，cd:{Cd} 秒" : "{Name} 点按元素战技，cd:{Cd} 秒", Name,
                    Math.Round(cd, 2));
                return;
            }
        }
    }

    /// <summary>
    /// 使用完元素战技的回调,注意,不会在这里检测是不是需要跑七天神像 <br/>
    /// UseSkill 方法内会调用，如果没有使用UseSkill但是释放了技能之后记得调用一下这个方法
    /// </summary>
    /// <returns>当前技能CD</returns>
    public double AfterUseSkill(ImageRegion? givenRegion = null)
    {
        LastSkillTime = DateTime.UtcNow;
        if (ManualSkillCd > 0)
        {
            return GetSkillCdSeconds();
        }

        var region = givenRegion ?? CaptureToRectArea();
        return GetSkillCurrentCd(region);
    }

    /// <summary>
    /// 元素战技是否正在CD中
    /// 右下 267x132
    /// 77x77
    /// </summary>
    private double GetSkillCurrentCd(ImageRegion imageRegion)
    {
        var eRa = imageRegion.DeriveCrop(AutoFightAssets.Instance.ECooldownRect);
        var eRaWhite = OpenCvCommonHelper.InRangeHsv(eRa.SrcMat, new Scalar(0, 0, 235), new Scalar(0, 25, 255));
        var text = OcrFactory.Paddle.OcrWithoutDetector(eRaWhite);
        var cd = StringUtils.TryParseDouble(text);
        if (cd > 0 && cd <= CombatAvatar.SkillCd)
        {
            OcrSkillCd = DateTime.UtcNow.AddSeconds(cd);
        }

        return cd;
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
            ThrowWhenDefeated(region, Ct);
            var notActiveCount = CombatScenes.GetAvatars().Count(avatar => !avatar.IsActive(region));
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
    ///
    /// 根据cd推算E技能是否好了
    /// </summary>
    /// <param name="skillCd">强制指定技能CD</param>
    /// <param name="printLog">log是否输出</param>
    /// <returns>是否好了</returns>
    public bool IsSkillReady(bool printLog = false)
    {
        var cd = GetSkillCdSeconds();
        if (cd > 0)
        {
            if (printLog)
            {
                Logger.LogInformation("{Name}的E技能未准备好,CD还有{Seconds}秒", Name, Math.Round(cd, 2));
            }

            return false;
        }

        return true;
    }

    /// <summary>
    ///  计算上一次使用技能到现在还剩下多长时间的cd
    /// </summary>
    /// <returns></returns>
    public double GetSkillCdSeconds()
    {
        switch (ManualSkillCd)
        {
            case < 0:
            {
                var now = DateTime.UtcNow;
                // 若未经过OCR的技能释放,上次时间加上最长的技能时间
                var maxCd = Math.Max(CombatAvatar.SkillHoldCd, CombatAvatar.SkillCd);
                var target =
                    LastSkillTime >= OcrSkillCd
                        ? LastSkillTime.AddSeconds(Math.Max(CombatAvatar.SkillHoldCd, CombatAvatar.SkillCd))
                        : OcrSkillCd;
                var result = now > target ? 0d : (target - now).TotalSeconds;
                if (!(result > maxCd)) return result;
                Logger.LogWarning("{Name}的当前技能CD大于其最大技能CD{MaxCd}。如果你没有调整系统时间的话，这是一个bug。", Name, maxCd);
                return maxCd;
            }
            case > 0:
            {
                // 用户设置，所以直接通过上次释放技能的时间计算
                var dif = DateTime.UtcNow - LastSkillTime;
                if (ManualSkillCd > dif.TotalSeconds)
                {
                    return ManualSkillCd - dif.TotalSeconds;
                }

                break;
            }
        }

        return 0;
    }

    /// <summary>
    /// 等待技能CD
    /// </summary>
    /// <param name="ct">CancellationToken</param>
    public async Task WaitSkillCd(CancellationToken ct = default)
    {
        // 获取CD时间
        if (IsSkillReady())
        {
            return;
        }

        var s = GetSkillCdSeconds() + 0.2;
        Logger.LogInformation("{Name}的E技能CD未结束，等待{Seconds}秒", Name, Math.Round(s, 2));
        await Delay((int)Math.Ceiling(s * 1000), ct);
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
        else if (Name == "恰斯卡")
        {
            var dpi = TaskContext.Instance().DpiScale;
            Simulation.SendInput.SimulateAction(GIActions.NormalAttack, KeyType.KeyDown);
            int tick = -4; // 起飞那一刻需要多一点点时间用来矫正视角高度
            while (ms >= 0)
            {
                if (Ct is { IsCancellationRequested: true })
                {
                    return;
                }

                // 恰在蓄力时转得越快越容易把视角趋向于水平
                // 基于上面这个特性，如果我们用同一个鼠标方向向量，大致能在所有设备上控制视角高低（只要帧率不太低）

                // 恰的子弹上膛机制：怪物要在HUD准星框内超过一定时长（体感0.2-0.3秒）才能让子弹上膛。所以搜索敌人要低速。不然敌人体型小或者远就很容易锁不上。
                const double lowspeed = 0.7, highspeed = 50;
                double rateX, rateY;
                if (tick < 3)
                {
                    rateX = highspeed;
                    rateY = highspeed * 0.23;
                }
                else if (tick < 40)
                {
                    rateX = lowspeed * 0.7;
                    rateY = 0;
                }
                else if (tick < 43)
                {
                    rateX = highspeed;
                    rateY = highspeed * 0.4;
                }
                else if (tick < 70)
                {
                    rateX = lowspeed * 0.9;
                    rateY = 0;
                }
                else if (tick < 73)
                {
                    rateX = highspeed;
                    rateY = highspeed;
                }
                else
                {
                    rateX = lowspeed;
                    rateY = 0;
                }
                Simulation.SendInput.Mouse.MoveMouseBy((int)(rateX * 50 * dpi), (int)(rateY * 50 * dpi));

                tick = (tick + 1) % 100;
                Sleep(25);
                ms -= 25;
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
        var vk = KeyBindingsSettingsPageViewModel.MappingKey(User32Helper.ToVk(key));
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
        var vk = KeyBindingsSettingsPageViewModel.MappingKey(User32Helper.ToVk(key));
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
        var vk = KeyBindingsSettingsPageViewModel.MappingKey(User32Helper.ToVk(key));
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
    /// 从配置字符串中查找角色cd
    /// 仅有角色名时返回 -1 ,没找到角色返回null
    /// </summary>
    /// <param name="avatarName">角色名</param>
    /// <param name="input">序列</param>
    /// <returns></returns>
    public static double? ParseActionSchedulerByCd(string avatarName, string input)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(avatarName))
            return null;

        var searchIndex = input.Length - 1;

        while (true)
        {
            // 逆向查找角色名最后一次出现的位置
            var foundIndex = input.LastIndexOf(avatarName, searchIndex, StringComparison.Ordinal);
            if (foundIndex == -1) return null;

            // 验证前向边界（分号或字符串起点）
            var startValid = foundIndex == 0 ||
                             input[foundIndex - 1] == ';';

            // 验证后向边界（逗号或分号/字符串终点）
            var endValid = foundIndex + avatarName.Length == input.Length ||
                           input[foundIndex + avatarName.Length] == ',' ||
                           input[foundIndex + avatarName.Length] == ';';

            if (startValid && endValid)
            {
                var valueStart = foundIndex + avatarName.Length;
                // 处理逗号后的数值部分
                if (valueStart >= input.Length || input[valueStart] != ',') return -1;
                var valueEnd = input.IndexOf(';', valueStart);
                if (valueEnd == -1) valueEnd = input.Length;

                if (double.TryParse(input.AsSpan(valueStart + 1, valueEnd - valueStart - 1),
                        out var result))
                {
                    return result;
                }

                // 存在角色名但没有数值的情况
                return -1;
            }

            // 更新搜索范围继续查找
            searchIndex = foundIndex - 1;
            if (searchIndex < 0) break;
        }

        return null;
    }
}