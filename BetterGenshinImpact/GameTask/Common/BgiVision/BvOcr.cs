using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.AutoPick;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.BgiVision;

/// <summary>
/// 一些基础图像识别操作
/// </summary>
public static partial class Bv
{
    public static string FindFKeyText(ImageRegion region)
    {
        using var foundRectArea = region.Find(AutoPickAssets.Instance.PickRo);
        if (foundRectArea.IsEmpty())
        {
            return string.Empty;
        }

        var scale = TaskContext.Instance().SystemInfo.AssetScale;
        var textRect = new Rect(foundRectArea.X + (int)(115 * scale), foundRectArea.Y,
            (int)((400 - 115) * scale), foundRectArea.Height);
        if (textRect.X + textRect.Width > region.SrcMat.Width
            || textRect.Y + textRect.Height > region.SrcMat.Height)
        {
            Debug.WriteLine("AutoPickTrigger: 文字区域 out of range");
            return string.Empty;
        }

        var textMat = new Mat(region.SrcMat, textRect);
        var boundingRect = AutoPickTrigger.GetWhiteTextBoundingRect(textMat);
        // 如果找到有效区域
        if (boundingRect.Width > 5 && boundingRect.Height > 5)
        {
            // 截取只包含文字的区域
            var textOnlyMat = new Mat(textMat, new Rect(0, 0,
                boundingRect.Right + 3 < textMat.Width ? boundingRect.Right + 3 : textMat.Width, textMat.Height));
            return OcrFactory.Paddle.OcrWithoutDetector(textOnlyMat);
        }
        else
        {
            return OcrFactory.Paddle.Ocr(textMat);
        }
    }
}