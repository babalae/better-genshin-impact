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

        public AutoFishingAssets()
        {
            var info = TaskContext.Instance().SystemInfo;
            SpaceButtonRo = new RecognitionObject
            {
                Name = "SpaceButton",
                RecognitionType = RecognitionType.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoFishing", "space.png"),
                RegionOfInterest = new Rect(info.CaptureAreaRect.Width - info.CaptureAreaRect.Width / 3, 
                    info.CaptureAreaRect.Height - info.CaptureAreaRect.Height / 5, 
                    info.CaptureAreaRect.Width / 3, 
                    info.CaptureAreaRect.Height / 5),
                DrawOnWindow = false
            };
        }
    }
}