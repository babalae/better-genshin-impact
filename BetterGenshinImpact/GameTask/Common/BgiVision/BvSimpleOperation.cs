using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using Fischless.WindowsInput;
using OpenCvSharp;
using System;
using System.Text.RegularExpressions;
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
    /// <summary>
    /// 通用方法：查找识别对象，如果存在则点击
    /// </summary>
    /// <param name="captureRa">截图区域</param>
    /// <param name="ro">识别对象</param>
    /// <returns>是否找到并点击</returns>
    public static bool FindAndClick(ImageRegion captureRa, RecognitionObject ro)
    {
        var ra = captureRa.Find(ro);
        if (ra.IsExist())
        {
            Thread.Sleep(500);
            ra.Click();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 在整个游戏画面中按指定范围查找元素并点击
    /// </summary>
    /// <param name="captureRa">整个游戏画面的截图区域</param>
    /// <param name="objectName">识别对象名称</param>
    /// <param name="searchRect">可选的局部搜索区域，坐标相对于整个游戏画面</param>
    /// <returns>是否找到并点击</returns>
    private static bool FindElementAndClick(ImageRegion captureRa, string objectName, Rect? searchRect = null)
    {
        // ------- 这段后续要注释掉 ------------
        var expectedCaptureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        if (captureRa.Width != expectedCaptureRect.Width || captureRa.Height != expectedCaptureRect.Height)
        {
            throw new ArgumentException(
                $"captureRa 必须是整个游戏画面的截图区域，预期尺寸为 {expectedCaptureRect.Width}x{expectedCaptureRect.Height}，实际尺寸为 {captureRa.Width}x{captureRa.Height}。局部搜索请通过 searchRect 传入。",
                nameof(captureRa));
        }
        // ------- 这段后续要注释掉 ------------

        var ro = ElementRecognition.Get(objectName, captureRa);
        if (searchRect is { } rect)
        {
            var effectiveSearchRect = rect.Intersect(new Rect(0, 0, captureRa.Width, captureRa.Height));
            if (ro.RegionOfInterest != default)
            {
                effectiveSearchRect = effectiveSearchRect.Intersect(ro.RegionOfInterest);
            }

            if (effectiveSearchRect.Width <= 0 || effectiveSearchRect.Height <= 0)
            {
                return false;
            }

            ro.RegionOfInterest = effectiveSearchRect;
        }

        return FindAndClick(captureRa, ro);
    }

    /// <summary>
    /// 点击减少按钮
    /// </summary>
    /// <param name="captureRa">整个游戏画面的截图区域</param>
    /// <param name="searchRect">可选的局部搜索区域，坐标相对于整个游戏画面</param>
    /// <returns></returns>
    public static bool ClickReduceButton(ImageRegion captureRa, Rect? searchRect = null)
        => FindElementAndClick(captureRa, "Keyreduce", searchRect);

    /// <summary>
    /// 点击增加按钮
    /// </summary>
    /// <param name="captureRa">整个游戏画面的截图区域</param>
    /// <param name="searchRect">可选的局部搜索区域，坐标相对于整个游戏画面</param>
    /// <returns></returns>
    public static bool ClickAddButton(ImageRegion captureRa, Rect? searchRect = null)
        => FindElementAndClick(captureRa, "Keyincrease", searchRect);

    /// <summary>
    /// 点击白色确认按钮
    /// </summary>
    /// <param name="captureRa">整个游戏画面的截图区域</param>
    /// <param name="searchRect">可选的局部搜索区域，坐标相对于整个游戏画面</param>
    /// <returns></returns>
    public static bool ClickWhiteConfirmButton(ImageRegion captureRa, Rect? searchRect = null)
        => FindElementAndClick(captureRa, "BtnWhiteConfirm", searchRect);

    /// <summary>
    /// 点击白色取消按钮
    /// </summary>
    /// <param name="captureRa">整个游戏画面的截图区域</param>
    /// <param name="searchRect">可选的局部搜索区域，坐标相对于整个游戏画面</param>
    /// <returns></returns>
    public static bool ClickWhiteCancelButton(ImageRegion captureRa, Rect? searchRect = null)
        => FindElementAndClick(captureRa, "BtnWhiteCancel", searchRect);

    /// <summary>
    /// 点击黑色确认按钮
    /// </summary>
    /// <param name="captureRa">整个游戏画面的截图区域</param>
    /// <param name="searchRect">可选的局部搜索区域，坐标相对于整个游戏画面</param>
    /// <returns></returns>
    public static bool ClickBlackConfirmButton(ImageRegion captureRa, Rect? searchRect = null)
        => FindElementAndClick(captureRa, "BtnBlackConfirm", searchRect);

    /// <summary>
    /// 点击黑色取消按钮
    /// </summary>
    /// <param name="captureRa">整个游戏画面的截图区域</param>
    /// <param name="searchRect">可选的局部搜索区域，坐标相对于整个游戏画面</param>
    /// <returns></returns>
    public static bool ClickBlackCancelButton(ImageRegion captureRa, Rect? searchRect = null)
        => FindElementAndClick(captureRa, "BtnBlackCancel", searchRect);

    /// <summary>
    /// 点击联机确认按钮
    /// </summary>
    /// <param name="captureRa">整个游戏画面的截图区域</param>
    /// <param name="searchRect">可选的局部搜索区域，坐标相对于整个游戏画面</param>
    /// <returns></returns>
    public static bool ClickOnlineYesButton(ImageRegion captureRa, Rect? searchRect = null)
        => FindElementAndClick(captureRa, "BtnOnlineYes", searchRect);

    /// <summary>
    /// 点击联机取消按钮
    /// </summary>
    /// <param name="captureRa">整个游戏画面的截图区域</param>
    /// <param name="searchRect">可选的局部搜索区域，坐标相对于整个游戏画面</param>
    /// <returns></returns>
    public static bool ClickOnlineNoButton(ImageRegion captureRa, Rect? searchRect = null)
        => FindElementAndClick(captureRa, "BtnOnlineNo", searchRect);

    /// <summary>
    /// 点击确认按钮（优先点击白色背景的确认按钮）
    /// </summary>
    /// <param name="captureRa">整个游戏画面的截图区域</param>
    /// <param name="searchRect">可选的局部搜索区域，坐标相对于整个游戏画面</param>
    /// <returns></returns>
    public static bool ClickConfirmButton(ImageRegion captureRa, Rect? searchRect = null)
    {
        return ClickBlackConfirmButton(captureRa, searchRect) || ClickWhiteConfirmButton(captureRa, searchRect) || ClickOnlineYesButton(captureRa, searchRect);
    }

    /// <summary>
    /// 点击取消按钮（优先点击白色背景的确认按钮）
    /// </summary>
    /// <param name="captureRa">整个游戏画面的截图区域</param>
    /// <param name="searchRect">可选的局部搜索区域，坐标相对于整个游戏画面</param>
    /// <returns></returns>
    public static bool ClickCancelButton(ImageRegion captureRa, Rect? searchRect = null)
    {
        return ClickBlackCancelButton(captureRa, searchRect) || ClickWhiteCancelButton(captureRa, searchRect) || ClickOnlineNoButton(captureRa, searchRect);
    }

    /// <summary>
    /// 找到交互按钮
    /// </summary>
    /// <param name="captureRa"></param>
    /// <param name="text"></param>
    /// <returns></returns>
    public static bool FindF(ImageRegion captureRa, params string[] text)
    {
        using var ra = captureRa.Find(AutoPickAssets.Get(captureRa, TaskContext.Instance().Config.AutoPickConfig.PickKey).PickRo);
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
            Simulation.SendInput.Keyboard.KeyPress(AutoPickAssets.Get(captureRa, TaskContext.Instance().Config.AutoPickConfig.PickKey).PickVk);
            return true;
        }

        return false;
    }

    public static bool FindFAndPress(ImageRegion captureRa, IKeyboardSimulator keyboard, params string[] text)
    {
        if (FindF(captureRa, text))
        {
            keyboard.KeyPress(AutoPickAssets.Get(captureRa, TaskContext.Instance().Config.AutoPickConfig.PickKey).PickVk);
            return true;
        }

        return false;
    }
}
