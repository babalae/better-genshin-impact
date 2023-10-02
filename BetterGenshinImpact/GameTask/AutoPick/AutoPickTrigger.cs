using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using Microsoft.Extensions.Logging;
using WindowsInput;

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
            var p2 = OldMatchTemplateHelper.FindSingleTarget(grayRightBottomMat, AutoPickAssets.FMat);
            if (p2 is { X: > 0, Y: > 0 })
            {
                _logger.LogInformation("找到F按钮");
                new InputSimulator().Keyboard.KeyPress(VirtualKeyCode.VK_F);
            }
        }
    }
}
