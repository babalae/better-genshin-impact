using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using OpenCvSharp;
using System;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoFight;
using Vanara.PInvoke;
using System.Threading;

namespace BetterGenshinImpact.GameTask.Common.BgiVision;

/// <summary>
/// 模仿OpenCv的静态类
/// 用于原神的各类识别与控制操作
///
/// 此处主要是对游戏内的一些状态进行识别
/// </summary>
public static partial class Bv
{
    public static string WhichGameUi()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 是否在主界面
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool IsInMainUi(ImageRegion captureRa)
    {
        return captureRa.Find(ElementAssets.Instance.PaimonMenuRo).IsExist();
    }

    /// <summary>
    /// 等待主界面加载完成
    /// </summary>
    /// <param name="ct"></param>
    /// <param name="retryTimes"></param>
    /// <returns></returns>
    public static async Task<bool> WaitForMainUi(CancellationToken ct, int retryTimes = 25)
    {
        for (var i = 0; i < retryTimes; i++)
        {
            await TaskControl.Delay(1000, ct);
            using var ra3 = TaskControl.CaptureToRectArea();
            if (IsInMainUi(ra3))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 是否在大地图界面
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool IsInBigMapUi(ImageRegion captureRa)
    {
        return captureRa.Find(QuickTeleportAssets.Instance.MapScaleButtonRo).IsExist();
    }

    /// <summary>
    /// 大地图界面是否在地底
    /// 鼠标悬浮在地下图标或者处于切换动画的时候可能会误识别
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool BigMapIsUnderground(ImageRegion captureRa)
    {
        return captureRa.Find(QuickTeleportAssets.Instance.MapUndergroundSwitchButtonRo).IsExist();
    }

    public static MotionStatus GetMotionStatus(ImageRegion captureRa)
    {
        var spaceExist = captureRa.Find(ElementAssets.Instance.SpaceKey).IsExist();
        var xExist = captureRa.Find(ElementAssets.Instance.XKey).IsExist();
        if (spaceExist)
        {
            return xExist ? MotionStatus.Climb : MotionStatus.Fly;
        }
        else
        {
            return MotionStatus.Normal;
        }
    }

    /// <summary>
    /// 是否出现复苏提示
    /// </summary>
    /// <param name="region"></param>
    /// <returns></returns>
    public static bool IsInRevivePrompt(ImageRegion region)
    {
        using var confirmRectArea = region.Find(AutoFightContext.Instance.FightAssets.ConfirmRa);
        if (!confirmRectArea.IsEmpty())
        {
            var list = region.FindMulti(new RecognitionObject
            {
                RecognitionType = RecognitionTypes.Ocr,
                RegionOfInterest = new Rect(0, 0, region.Width, region.Height / 2)
            });
            if (list.Any(r => r.Text.Contains("复苏")))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 是否出现全队死亡和复苏提示
    /// </summary>
    /// <param name="region"></param>
    /// <returns></returns>
    public static bool ClickIfInReviveModal(ImageRegion region)
    {
        var list = region.FindMulti(new RecognitionObject
        {
            RecognitionType = RecognitionTypes.Ocr,
            RegionOfInterest = new Rect(0, region.Height / 4 * 3, region.Width, region.Height / 4)
        });
        var r = list.FirstOrDefault(r => r.Text.Contains("复苏"));
        if (r != null)
        {
            r.Click();
            return true;
        }

        return false;
    }

    /// <summary>
    /// 当前角色是否低血量
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool CurrentAvatarIsLowHp(ImageRegion captureRa)
    {
        // 获取 (808, 1010) 位置的像素颜色
        var pixelColor = captureRa.SrcMat.At<Vec3b>(1010, 808);

        // 判断颜色是否是 (255, 90, 90)
        return pixelColor is { Item2: 255, Item1: 90, Item0: 90 };
    }
}

public enum MotionStatus
{
    Normal, // 正常
    Fly, // 飞行
    Climb, // 攀爬
}
