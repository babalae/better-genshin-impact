using BetterGenshinImpact.Core.Config;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;

namespace BetterGenshinImpact.GameTask.AutoFishing.Assets
{
    public class AutoFishingAssets
    {
        public RecognitionObject SpaceButtonRo;

        public RecognitionObject BaitButtonRo;
        public RecognitionObject WaitBiteButtonRo;
        public RecognitionObject LiftRodButtonRo;
        public RecognitionObject ExitFishingButtonRo;

        public AutoFishingAssets()
        {
            var info = TaskContext.Instance().SystemInfo;
            SpaceButtonRo = new RecognitionObject
            {
                Name = "SpaceButton",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoFishing", "space.png"),
                RegionOfInterest = new Rect(info.CaptureAreaRect.Width - info.CaptureAreaRect.Width / 3,
                    info.CaptureAreaRect.Height - info.CaptureAreaRect.Height / 5,
                    info.CaptureAreaRect.Width / 3,
                    info.CaptureAreaRect.Height / 5),
                DrawOnWindow = false
            }.InitTemplate();

            BaitButtonRo = new RecognitionObject
            {
                Name = "BaitButton",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoFishing", "switch_bait.png"),
                RegionOfInterest = new Rect(info.CaptureAreaRect.Width - info.CaptureAreaRect.Width / 2,
                    info.CaptureAreaRect.Height - info.CaptureAreaRect.Height / 4,
                    info.CaptureAreaRect.Width / 2,
                    info.CaptureAreaRect.Height / 4),
                Threshold = 0.7,
                DrawOnWindow = true
            }.InitTemplate();
            WaitBiteButtonRo = new RecognitionObject
            {
                Name = "WaitBiteButton",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoFishing", "wait_bite.png"),
                RegionOfInterest = new Rect(info.CaptureAreaRect.Width - info.CaptureAreaRect.Width / 2,
                    info.CaptureAreaRect.Height - info.CaptureAreaRect.Height / 4,
                    info.CaptureAreaRect.Width / 2,
                    info.CaptureAreaRect.Height / 4),
                Threshold = 0.7,
                DrawOnWindow = true
            }.InitTemplate();
            LiftRodButtonRo = new RecognitionObject
            {
                Name = "LiftRodButton",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoFishing", "lift_rod.png"),
                RegionOfInterest = new Rect(info.CaptureAreaRect.Width - info.CaptureAreaRect.Width / 2,
                    info.CaptureAreaRect.Height - info.CaptureAreaRect.Height / 4,
                    info.CaptureAreaRect.Width / 2,
                    info.CaptureAreaRect.Height / 4),
                Threshold = 0.7,
                DrawOnWindow = true
            }.InitTemplate();

            var w = (int)(140 * info.AssetScale);
            var h = (int)(150 * info.AssetScale);
            ExitFishingButtonRo = new RecognitionObject
            {
                Name = "ExitFishingButton",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoFishing", "exit_fishing.png"),
                RegionOfInterest = new Rect(info.CaptureAreaRect.Width - w,
                    info.CaptureAreaRect.Height - h,
                    w,
                    h),
                Threshold = 0.8,
                DrawOnWindow = true
            }.InitTemplate();
        }
    }
}