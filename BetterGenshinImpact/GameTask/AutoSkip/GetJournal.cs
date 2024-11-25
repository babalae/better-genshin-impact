using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.View.Drawable;
using Microsoft.Extensions.Logging;
using System;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using static Vanara.PInvoke.User32;

namespace BetterGenshinImpact.GameTask.AutoSkip;

using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;

namespace BetterGenshinImpact.GameTask.AutoSkip
{
    public partial class AutoSkipTrigger
    {
        private void HandleGetJournal(CaptureContent content)
        {
            // 检测第一张纪行图片
            using var imageARegion = content.CaptureRectArea.Find(_autoSkipAssets.jixin_notice);
            if (!imageARegion.IsEmpty())
            {
                // OCR识别并一键领取
                var receiveButtonA = content.CaptureRectArea.Find(_autoSkipAssets.jixin_notice);
                if (!receiveButtonA.IsEmpty())
                {
                    var ocrTextA = OcrFactory.Paddle.Ocr(receiveButtonA.SrcGreyMat);
                    if (ocrTextA.Contains("一键领取"))
                    {
                        receiveButtonA.Click();
                        _logger.LogInformation("OCR确认，已一键领取纪行。");
                    }
                }
            }

            // 检测第二张纪行图片
            using var imageBRegion = content.CaptureRectArea.Find(_autoSkipAssets.jixin_reward);
            if (!imageBRegion.IsEmpty())
            {
                // OCR识别并一键领取
                var receiveButtonB = content.CaptureRectArea.Find(_autoSkipAssets.jixin_reward);
                if (!receiveButtonB.IsEmpty())
                {
                    var ocrTextB = OcrFactory.Paddle.Ocr(receiveButtonB.SrcGreyMat);
                    if (ocrTextB.Contains("一键领取"))
                    {
                        receiveButtonB.Click();
                        _logger.LogInformation("OCR确认，已一键领取物品。");
                    }
                }
            }
        }
    }
}
