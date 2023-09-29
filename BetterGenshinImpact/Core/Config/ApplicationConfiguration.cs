using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoSkip;
using System;
using Vision.WindowCapture;

namespace BetterGenshinImpact.Core.Config
{
    /// <summary>
    /// 更好的原神配置
    /// </summary>
    [Serializable]
    public class ApplicationConfiguration
    {
        /// <summary>
        /// 窗口捕获的方式
        /// </summary>
        public string CaptureMode { get; set; } = CaptureModeEnum.BitBlt.ToString();

        /// <summary>
        /// 窗口捕获帧数/触发器触发频率
        /// </summary>
        public int FrameRate { get; set; } = 30;

        /// <summary>
        /// 触发器触发频率(ms)
        /// </summary>
        public int FrameInterval { get; set; } = 50;

        /// <summary>
        /// 遮罩窗口配置
        /// </summary>
        public MaskWindowConfig MaskWindowConfig { get; set; } = new();

        /// <summary>
        /// 自动剧情配置
        /// </summary>
        public AutoSkipConfig AutoSkipConfig { get; set; } = new();

        /// <summary>
        /// 自动钓鱼配置
        /// </summary>
        public AutoFishingConfig AutoFishingConfig { get; set; } = new();

    }
}
