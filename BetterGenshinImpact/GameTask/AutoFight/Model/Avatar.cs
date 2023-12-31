using System.Linq;
using BetterGenshinImpact.GameTask.AutoFight.Config;
using OpenCvSharp;
using System.Threading;
using BetterGenshinImpact.Core.Recognition.OCR;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Helpers;
using static SharpDX.Utilities;

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
    public CancellationTokenSource? Cts { get; set; }

    public Avatar(string name, int index, Rect nameRect)
    {
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
    /// 切换到本角色
    /// 切换cd是1秒，如果切换失败，会尝试再次切换，最多尝试5次
    /// </summary>
    public void Switch()
    {
        for (var i = 0; i < 5; i++)
        {
            if (Cts is { IsCancellationRequested: true })
            {
                return;
            }

            if (IsActive(GetContentFromDispatcher()))
            {
                return;
            }

            AutoFightContext.Instance().Simulator.KeyPress(User32.VK.VK_1 + (byte)Index - 1);
            Thread.Sleep(1050); // 比1秒多一点，给截图留出时间
        }
    }

    /// <summary>
    /// 是否出战状态
    /// </summary>
    /// <returns></returns>
    public bool IsActive(CaptureContent content)
    {
        // 通过寻找右侧人物编号来判断是否出战
        if (IndexRect == Rect.Empty)
        {
            var assetScale = TaskContext.Instance().SystemInfo.AssetScale;
            // 剪裁出队伍区域
            var teamRa = content.CaptureRectArea.Crop(AutoFightContext.Instance().FightAssets.TeamRect);
            var blockX = NameRect.X + NameRect.Width * 2 - 10;
            var block = teamRa.Crop(new Rect(blockX, NameRect.Y, teamRa.Width - blockX, NameRect.Height * 2));
            // 取白色区域
            var bMat = OpenCvCommonHelper.Threshold(block.SrcMat, new Scalar(255, 255, 255), new Scalar(255, 255, 255));
            // 矩形识别
            Cv2.FindContours(bMat, out var contours, out _, RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);
            if (contours.Length > 0)
            {
                var boxes = contours.Select(Cv2.BoundingRect).Where(w => w.Width >= 20 * assetScale && w.Height >= 18 * assetScale).OrderByDescending(w => w.Width).ToList();
                if (boxes.Any())
                {
                    IndexRect = boxes.First();
                    return false;
                }
            }
        }
        else
        {
            // 剪裁出IndexRect区域
            var teamRa = content.CaptureRectArea.Crop(AutoFightContext.Instance().FightAssets.TeamRect);
            var blockX = NameRect.X + NameRect.Width * 2 - 10;
            var indexBlock = teamRa.Crop(new Rect(blockX + IndexRect.X, NameRect.Y + IndexRect.Y, IndexRect.Width, IndexRect.Height));
            int count = OpenCvCommonHelper.CountGrayMatColor(indexBlock.SrcGreyMat, 255);
            if (count * 1.0 / (IndexRect.Width * IndexRect.Height) > 0.7)
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
    public void Attack(int ms)
    {
        while (ms > 0)
        {
            if (Cts is { IsCancellationRequested: true })
            {
                return;
            }
            AutoFightContext.Instance().Simulator.LeftButtonClick();
            ms -= 200;
            Sleep(200);
        }
    }

    /// <summary>
    /// 使用元素战技 E
    /// </summary>
    public void UseSkill(bool hold = false)
    {
        for (var i = 0; i < 5; i++)
        {
            if (Cts is { IsCancellationRequested: true })
            {
                return;
            }
            if (hold)
            {
                AutoFightContext.Instance().Simulator.LongKeyPress(User32.VK.VK_E);
            }
            else
            {
                AutoFightContext.Instance().Simulator.KeyPress(User32.VK.VK_E);
            }

            Sleep(500);

            var cd = GetSkillCurrentCd(GetContentFromDispatcher());
            if (cd > 0)
            {
                Logger.LogInformation(hold ? "{Name} 长按元素战技，cd:{Cd}" : "{Name} 点按元素战技，cd:{Cd}", Name, cd);
                // todo 把cd加入执行队列
                return;
            }
        }
    }

    /// <summary>
    /// 元素战技是否正在CD中
    /// 右下 267x132
    /// 77x77
    /// </summary>
    public double GetSkillCurrentCd(CaptureContent content)
    {
        var eRa = content.CaptureRectArea.Crop(AutoFightContext.Instance().FightAssets.ERect);
        var text = OcrFactory.Paddle.Ocr(eRa.SrcGreyMat);
        return StringUtils.TryParseDouble(text);
    }

    /// <summary>
    /// 使用元素爆发 Q
    /// </summary>
    public void UseBurst()
    {
        for (var i = 0; i < 5; i++)
        {
            if (Cts is { IsCancellationRequested: true })
            {
                return;
            }
            AutoFightContext.Instance().Simulator.KeyPress(User32.VK.VK_Q);
            Sleep(500);
            var cd = GetBurstCurrentCd(GetContentFromDispatcher());
            if (cd > 0)
            {
                Logger.LogInformation("{Name} 释放元素爆发，cd:{Cd}", Name, cd);
                // todo  把cd加入执行队列
                return;
            }
        }
    }

    /// <summary>
    /// 元素爆发是否正在CD中
    /// 右下 157x165
    /// 110x110
    /// </summary>
    public double GetBurstCurrentCd(CaptureContent content)
    {
        var qRa = content.CaptureRectArea.Crop(AutoFightContext.Instance().FightAssets.QRect);
        var text = OcrFactory.Paddle.Ocr(qRa.SrcGreyMat);
        return StringUtils.TryParseDouble(text);
    }

    /// <summary>
    /// 冲刺
    /// </summary>
    public void Dash()
    {
        if (Cts is { IsCancellationRequested: true })
        {
            return;
        }
        AutoFightContext.Instance().Simulator.KeyPress(User32.VK.VK_SHIFT);
    }


    public void Walk(string key, int ms)
    {
        if (Cts is { IsCancellationRequested: true })
        {
            return;
        }
        User32.VK vk = User32.VK.VK_NONAME;
        if (key == "w")
        {
            vk = User32.VK.VK_W;
        }
        else if (key == "s")
        {
            vk = User32.VK.VK_S;
        }
        else if (key == "a")
        {
            vk = User32.VK.VK_A;
        }
        else if (key == "d")
        {
            vk = User32.VK.VK_D;
        }

        if (vk == User32.VK.VK_NONAME)
        {
            return;
        }

        AutoFightContext.Instance().Simulator.KeyDown(vk);
        Sleep(ms);
        AutoFightContext.Instance().Simulator.KeyUp(vk);
    }
}