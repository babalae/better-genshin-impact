using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;
using BetterGenshinImpact.GameTask.AutoFishing.Assets;

namespace BetterGenshinImpact.GameTask.RecognitionTest
{
    public class TestTrigger : ITaskTrigger
    {
        public string Name => "开发测试识别触发器";
        public bool IsEnabled { get; set; }
        public int Priority => 9999;
        public bool IsExclusive { get; private set; }

        private readonly RecognitionObject _optionButtonRo;

        private readonly AutoFishingAssets _autoFishingAssets;

        public TestTrigger()
        {
            var info = TaskContext.Instance().SystemInfo;
            _optionButtonRo = new RecognitionObject
            {
                Name = "OptionButton",
                RecognitionType = RecognitionTypes.TemplateMatch,
                TemplateImageMat = GameTaskManager.LoadAssertImage("AutoSkip", "option.png"),
                DrawOnWindow = true
            }.InitTemplate();
            _autoFishingAssets = new AutoFishingAssets();
        }

        public void Init()
        {
            IsEnabled = false;
            IsExclusive = false;
        }

        public void OnCapture(CaptureContent content)
        {
            //content.CaptureRectArea.Find(_optionButtonRo, (optionButtonRectArea) =>
            //{
            //});

            content.CaptureRectArea.Find(_autoFishingAssets.BaitButtonRo, (rectArea) =>
            {
            });

            content.CaptureRectArea.Find(_autoFishingAssets.WaitBiteButtonRo, (rectArea) =>
            {
            });
        }
    }
}
