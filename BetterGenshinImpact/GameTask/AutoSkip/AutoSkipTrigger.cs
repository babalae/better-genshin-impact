using System;
using System.Drawing;
using OpenCvSharp;
using Vision.Recognition.Task;

namespace BetterGenshinImpact.GameTask.AutoSkip
{
    public class AutoSkipTrigger : ITaskTrigger
    {
        public bool IsEnabled { get; set; }
        public int Priority => 20;
        public bool IsExclusive => false;

        public void Init(ITaskContext context)
        {
            throw new NotImplementedException();
        }

        public void OnCapture(Bitmap bitmap, int frameIndex)
        {
            throw new NotImplementedException();
        }

        public void OnCapture(Mat matSrc, int frameIndex)
        {
            throw new NotImplementedException();
        }
    }
}