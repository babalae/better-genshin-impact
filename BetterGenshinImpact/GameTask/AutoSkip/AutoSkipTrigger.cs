using System;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using OpenCvSharp;
using Vision.Recognition.Helper.OpenCv;
using Vision.Recognition.Task;
using WindowsInput;

namespace BetterGenshinImpact.GameTask.AutoSkip
{
    public class AutoSkipTrigger : ITaskTrigger
    {
        public string Name => "自动剧情";
        public bool IsEnabled { get; set; }
        public int Priority => 20;
        public bool IsExclusive => false;

        public void Init(ITaskContext context)
        {
            
        }

        public void OnCapture(Mat matSrc, int frameIndex)
        {
            //TODO 切割图片加快效率
            var grayMat = new Mat();
            Cv2.CvtColor(matSrc, grayMat, ColorConversionCodes.BGR2GRAY);
            // 找左上角剧情自动的按钮
            var p1 = MatchTemplateHelper.FindSingleTarget(grayMat, AutoSkipAssets.StopAutoButtonMat);
            if (p1 is { X: > 0, Y: > 0 })
            {
                //TODO 无效操作代码 需要替换
                new InputSimulator().Keyboard.KeyPress(VirtualKeyCode.SPACE);
                return;
            }
            // 不存在则找右下的选项按钮
            var p2 = MatchTemplateHelper.FindSingleTarget(grayMat, AutoSkipAssets.OptionMat);
            if (p2 is { X: > 0, Y: > 0 })
            {
                new InputSimulator().Mouse.MoveMouseTo(p2.X, p2.Y).LeftButtonClick();
                return;
            }
            // 判断左上角的黑色像素个数
            var blackCount = OpenCvCommonHelper.CountGrayMatColor(grayMat, 0);
            var rate = blackCount * 1.0 / (grayMat.Width * grayMat.Height);
            if (rate > 0.9)
            {
                //TODO click center
                return;
            }
        }
    }
}