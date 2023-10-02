using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Win32.Foundation;

namespace BetterGenshinImpact.GameTask.Model
{
    public class SystemInfo
    {
        /// <summary>
        /// 显示器分辨率 无缩放
        /// </summary>
        public Size DisplaySize { get; set; }

        public string GameProcessName { get; set; }

        public int GameProcessId { get; set; }

        public nint GameProcessHandle { get; set; }

        /// <summary>
        /// 游戏窗口内分辨率
        /// </summary>
        public RECT GameScreenSize { get; set; }

        /// <summary>
        /// 素材缩放比例
        /// </summary>
        public double AssetScale { get; set; } = 1;
    }
}
