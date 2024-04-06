using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.Common.BgiVision;

/// <summary>
/// 模仿OpenCv的静态类
/// 用于原神的各类识别与控制操作
///
/// 此处主要是对游戏内的一些状态进行识别
/// </summary>
public static partial class Bv
{
    /// <summary>
    /// 点击白色确认按钮
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool ClickWhiteConfirmButton(RectArea captureRa)
    {
        var ra = captureRa.Find(ElementAssets.Instance.BtnWhiteConfirm);
        if (ra.IsExist())
        {
            ra.ClickCenter();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 点击白色取消按钮
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool ClickWhiteCancelButton(RectArea captureRa)
    {
        var ra = captureRa.Find(ElementAssets.Instance.BtnWhiteCancel);
        if (ra.IsExist())
        {
            ra.ClickCenter();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 点击黑色确认按钮
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool ClickBlackConfirmButton(RectArea captureRa)
    {
        var ra = captureRa.Find(ElementAssets.Instance.BtnBlackConfirm);
        if (ra.IsExist())
        {
            ra.ClickCenter();
            return true;
        }
        return false;
    }

    public static bool ClickBlackCancelButton(RectArea captureRa)
    {
        var ra = captureRa.Find(ElementAssets.Instance.BtnBlackCancel);
        if (ra.IsExist())
        {
            ra.ClickCenter();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 点击确认按钮（优先点击白色背景的确认按钮）
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool ClickConfirmButton(RectArea captureRa)
    {
        return ClickBlackConfirmButton(captureRa) || ClickWhiteConfirmButton(captureRa);
    }

    /// <summary>
    /// 点击取消按钮（优先点击白色背景的确认按钮）
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool ClickCancelButton(RectArea captureRa)
    {
        return ClickBlackCancelButton(captureRa) || ClickWhiteCancelButton(captureRa);
    }
}
