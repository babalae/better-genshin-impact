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

        public AutoPickAssets()
        {
            var info = TaskContext.Instance().SystemInfo;
            FRo = new RecognitionObject
            {
                Name = "F",
                RecognitionType = RecognitionType.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoPick", "F.png"),
                RegionOfInterest = new Rect(info.CaptureAreaRect.Width / 2, 
                    info.CaptureAreaRect.Height / 3, 
                    info.CaptureAreaRect.Width - info.CaptureAreaRect.Width / 2, 
                    info.CaptureAreaRect.Height - info.CaptureAreaRect.Height / 3),
                DrawOnWindow = false
            };
            FRo.InitTemplate();
        }
    }
}