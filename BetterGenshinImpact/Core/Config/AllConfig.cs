using BetterGenshinImpact.GameTask.AutoFishing;
using BetterGenshinImpact.GameTask.AutoSkip;
using System;
using BetterGenshinImpact.GameTask.AutoPick;
using CommunityToolkit.Mvvm.ComponentModel;
using Fischless.WindowCapture;

namespace BetterGenshinImpact.Core.Config
{
    /// <summary>
    /// 更好的原神配置
    /// </summary>
    [Serializable]
    public partial class AllConfig : ObservableObject
    {
        /// <summary>
        /// 窗口捕获的方式
        /// </summary>
        [ObservableProperty] private string _captureMode = CaptureModes.BitBlt.ToString();

        /// <summary>
        /// 窗口捕获帧数/触发器触发频率
        /// </summary>
        [ObservableProperty] private int _frameRate = 30;

        /// <summary>
        /// 触发器触发频率(ms)
        /// </summary>
        [ObservableProperty] private int _triggerInterval = 100;

        /// <summary>
        /// 遮罩窗口配置
        /// </summary>
        public MaskWindowConfig MaskWindowConfig { get; set; } = new();

        /// <summary>
        /// 自动拾取配置
        /// </summary>
        public AutoPickConfig AutoPickConfig { get; set; } = new();

        /// <summary>
        /// 自动剧情配置
        /// </summary>
        public AutoSkipConfig AutoSkipConfig { get; set; } = new();

        /// <summary>
        /// 自动钓鱼配置
        /// </summary>
        public AutoFishingConfig AutoFishingConfig { get; set; } = new();

        /// <summary>
        /// 脚本类配置
        /// </summary>
        public MacroConfig MacroConfig { get; set; } = new();
    }
}