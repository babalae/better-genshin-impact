using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Fischless.WindowsInput;
using OpenCvSharp;
using System.Text.RegularExpressions;

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
    /// 点击减少按钮
    /// </summary>
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool ClickReduceButton(ImageRegion captureRa)
    {
        var ra = captureRa.Find(ElementAssets.Instance.Keyreduce);
        if (ra.IsExist())
        {
            ra.Click();
            return true;
        }         
        return false;
    }
    
    /// <summary>
    /// 点击增加按钮
    /// <param name="captureRa"></param>
    /// <returns></returns>
    public static bool ClickAddButton(ImageRegion captureRa)
    {
        var ra = captureRa.Find(ElementAssets.Instance.Keyincrease);
        if (ra.IsExist())
        {
            ra.Click();
            return true;
        }         
        return false;
    }
    
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

    /// <summary>
    /// 找到交互按钮
    /// </summary>
    /// <param name="captureRa"></param>
    /// <param name="text"></param>
    /// <returns></returns>
    public static bool FindF(ImageRegion captureRa, params string[] text)
    {
        using var ra = captureRa.Find(AutoPickAssets.Instance.PickRo);
        if (ra.IsExist())
        {
            if (text.Length == 0)
            {
                return true;
            }

            var scale = TaskContext.Instance().SystemInfo.AssetScale;
            var config = TaskContext.Instance().Config.AutoPickConfig;
            var textRect = new Rect(ra.X + (int)(config.ItemTextLeftOffset * scale), ra.Y,
                (int)((config.ItemTextRightOffset - config.ItemTextLeftOffset) * scale), ra.Height);

            var textRa = captureRa.DeriveCrop(textRect);
            var list = textRa.FindMulti(RecognitionObject.OcrThis);

            foreach (var item in list)
            {
                // 所有匹配成功才算成功
                var success = true;
                foreach (var t in text)
                {
                    if (!Regex.IsMatch(item.Text, t))
                    {
                        success = false;
                    }
                }
                return success;
            }
        }

        return false;
    }

    /// <summary>
    /// 找到交互按钮并点击
    /// </summary>
    /// <param name="captureRa"></param>
    /// <param name="text"></param>
    /// <returns></returns>
    public static bool FindFAndPress(ImageRegion captureRa, params string[] text)
    {
        if (FindF(captureRa, text))
        {
            Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
            return true;
        }

        return false;
    }

    public static bool FindFAndPress(ImageRegion captureRa, IKeyboardSimulator keyboard, params string[] text)
    {
        if (FindF(captureRa, text))
        {
            keyboard.KeyPress(AutoPickAssets.Instance.PickVk);
            return true;
        }

        return false;
    }
}
