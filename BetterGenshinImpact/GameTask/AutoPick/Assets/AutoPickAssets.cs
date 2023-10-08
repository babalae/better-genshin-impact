using System.Drawing;
using BetterGenshinImpact.Core.Recognition;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoPick.Assets
{
    public class AutoPickAssets
    {
        public RecognitionObject FRo;
        public RecognitionObject ChatIconRo;

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

            ChatIconRo = new RecognitionObject
            {
                Name = "ChatIcon",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoSkip", "option.png"),
                DrawOnWindow = false,
                DrawOnWindowPen = new Pen(Color.Chocolate, 2)
            }.InitTemplate();
        }
    }
}