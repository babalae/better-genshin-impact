using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPick.Assets
{
    public class AutoPickAssets
    {
        public RecognitionObject FRo;
        public RecognitionObject OptionButtonRo;

        public AutoPickAssets()
        {
            var info = TaskContext.Instance().SystemInfo;
            FRo = new RecognitionObject
            {
                Name = "F",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoPick", "F.png"),
                RegionOfInterest = new Rect(info.CaptureAreaRect.Width / 2, 
                    info.CaptureAreaRect.Height / 3, 
                    info.CaptureAreaRect.Width - info.CaptureAreaRect.Width / 2, 
                    info.CaptureAreaRect.Height - info.CaptureAreaRect.Height / 3),
                DrawOnWindow = false
            }.InitTemplate();

            OptionButtonRo = new RecognitionObject
            {
                Name = "OptionButton",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoSkip", "option.png"),
                DrawOnWindow = false
            }.InitTemplate();
        }
    }
}