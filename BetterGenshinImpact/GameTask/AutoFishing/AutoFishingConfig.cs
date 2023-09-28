using OpenCvSharp;
using System;

namespace BetterGenshinImpact.GameTask.AutoFishing
{
    /// <summary>
    /// 自动钓鱼配置
    /// </summary>
    [Serializable]
    public class AutoFishingConfig
    {
        /// <summary>
        /// 触发器是否启用
        /// 启用后：
        /// 1. 自动判断是否进入钓鱼状态
        /// 2. 自动提杆
        /// 3. 自动拉条
        /// </summary>
        public bool Enable { get; set; } = true;

        /// <summary>
        /// 鱼儿上钩文字识别区域
        /// 暂时无用
        /// </summary>
        public Rect FishHookedRecognitionArea { get; set; } = Rect.Empty;
    }

}
