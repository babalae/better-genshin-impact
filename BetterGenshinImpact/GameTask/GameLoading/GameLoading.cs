using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.GameLoading
{
    public class GameLoadingTrigger : ITaskTrigger
    {
        private RecognitionObject? _startGameRo;

        public string Name => "GameLoading";

        public bool IsEnabled { get; set; }

        public int Priority => 999;

        public bool IsExclusive => false;

        public void Init()
        {
            var info = TaskContext.Instance().SystemInfo;
            _startGameRo = new RecognitionObject
            {
                Name = "StartGame",
                RecognitionType = RecognitionTypes.Ocr,
                // ROI 应该时捕捉窗口的中间底部部分
                RegionOfInterest = new Rect((int)(info.CaptureAreaRect.Width / 2 - 100),
                    (int)(info.CaptureAreaRect.Height - 100),
                    200,
                    100),
                OneContainMatchText = new List<string>
            {
                "点", "击", "进", "入"
            },
                DrawOnWindow = true
            }.InitTemplate();
            IsEnabled = true;
        }

        public void OnCapture(CaptureContent content)
        {
            using var foundRectArea = content.CaptureRectArea.Find(_startGameRo!);
            if (!foundRectArea.IsEmpty())
            {
                // 在游戏窗口中心点击
                var info = TaskContext.Instance().SystemInfo;
                var x = info.CaptureAreaRect.Right - (info.CaptureAreaRect.Width / 2);
                var y = info.CaptureAreaRect.Bottom - (info.CaptureAreaRect.Height / 2);
                Simulation.MouseEvent.Click(x, y);
                // 一旦进入游戏，这个触发器就不再需要了
                // TODO：如果其他触发器成功，这个触发器同样也不再需要了，考虑使用其他触发器的成功来禁用该事件
                IsEnabled = false;
            }
        }
    }

}
