using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using Vision.Recognition.Helper.OpenCv;
using Vision.Recognition.Task;
using WindowsInput;
using BetterGenshinImpact.GameTask.AutoSkip;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoPick
{
    public class AutoPickTrigger : ITaskTrigger
    {
        private readonly ILogger<AutoPickTrigger> _logger = App.GetLogger<AutoPickTrigger>();

        public string Name => "自动拾取";
        public bool IsEnabled { get; set; }
        public int Priority => 30;
        public bool IsExclusive => false;
        public void Init()
        {
            IsEnabled = true;
        }

        public void OnCapture(CaptureContent content)
        {
            var grayRightBottomMat = content.SrcGreyRightBottomMat.Clone();
            var p2 = MatchTemplateHelper.FindSingleTarget(grayRightBottomMat, AutoPickAssets.FMat);
            if (p2 is { X: > 0, Y: > 0 })
            {
                _logger.LogInformation("找到F按钮");
                new InputSimulator().Keyboard.KeyPress(VirtualKeyCode.VK_F);
            }
        }
    }
}
