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

        private AutoPickAssets _autoPickAssets;

        public AutoPickTrigger()
        {
            _autoPickAssets = new AutoPickAssets();
        }

        public void Init()
        {
            IsEnabled = true;
        }

        public void OnCapture(CaptureContent content)
        {
            content.CaptureRectArea.Find(_autoPickAssets.FRo, _ =>
            {
                _logger.LogInformation("找到F按钮");
                new InputSimulator().Keyboard.KeyPress(VirtualKeyCode.VK_F);
            });
        }
    }
}