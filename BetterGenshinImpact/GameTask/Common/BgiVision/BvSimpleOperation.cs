using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;

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
    public static bool ClickWhiteConfirmButton(ImageRegion captureRa)
    {
        var ra = captureRa.Find(ElementAssets.Instance.BtnWhiteConfirm);
        if (ra.IsExist())
        {
            ra.Click();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 点击白色取消按钮
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool ClickWhiteCancelButton(ImageRegion captureRa)
    {
        var ra = captureRa.Find(ElementAssets.Instance.BtnWhiteCancel);
        if (ra.IsExist())
        {
            ra.Click();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 点击黑色确认按钮
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool ClickBlackConfirmButton(ImageRegion captureRa)
    {
        var ra = captureRa.Find(ElementAssets.Instance.BtnBlackConfirm);
        if (ra.IsExist())
        {
            ra.Click();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 点击黑色取消按钮
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool ClickBlackCancelButton(ImageRegion captureRa)
    {
        var ra = captureRa.Find(ElementAssets.Instance.BtnBlackCancel);
        if (ra.IsExist())
        {
            ra.Click();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 点击联机确认按钮
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool ClickOnlineYesButton(ImageRegion captureRa)
    {
        var ra = captureRa.Find(ElementAssets.Instance.BtnOnlineYes);
        if (ra.IsExist())
        {
            ra.Click();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 点击联机取消按钮
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool ClickOnlineNoButton(ImageRegion captureRa)
    {
        var ra = captureRa.Find(ElementAssets.Instance.BtnOnlineNo);
        if (ra.IsExist())
        {
            ra.Click();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 点击确认按钮（优先点击白色背景的确认按钮）
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool ClickConfirmButton(ImageRegion captureRa)
    {
        return ClickBlackConfirmButton(captureRa) || ClickWhiteConfirmButton(captureRa) || ClickOnlineYesButton(captureRa);
    }

    /// <summary>
    /// 点击取消按钮（优先点击白色背景的确认按钮）
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool ClickCancelButton(ImageRegion captureRa)
    {
        return ClickBlackCancelButton(captureRa) || ClickWhiteCancelButton(captureRa) || ClickOnlineNoButton(captureRa);
    }
}
