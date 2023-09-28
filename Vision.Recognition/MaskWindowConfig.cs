using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Vision.Recognition
{
    /// <summary>
    /// 遮罩窗口配置
    /// </summary>
    [Serializable]
    public class MaskWindowConfig
    {
        /// <summary>
        /// 是否启用遮罩窗口
        /// </summary>
        public bool EnableMaskWindow { get; set; } = true;

        /// <summary>
        /// 是否在遮罩窗口上显示识别结果
        /// </summary>
        public bool DisplayRecognitionResultsOnMaskWindow { get; set; } = true;

        /// <summary>
        /// 显示遮罩窗口边框
        /// </summary>
        public bool ShowMaskWindowBorder { get; set; } = false;

        /// <summary>
        /// 显示日志窗口
        /// </summary>
        public bool ShowLogBox { get; set; } = true;

        /// <summary>
        /// 日志窗口位置与大小
        /// </summary>
        public Rect LogBoxLocation { get; set; } = Rect.Empty;

        /// <summary>
        /// 控件是否锁定（拖拽移动等）
        /// </summary>
        public bool ControlLocked { get; set; } = false;

    }
}
