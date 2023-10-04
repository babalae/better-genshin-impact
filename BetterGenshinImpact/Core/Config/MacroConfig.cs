using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Config
{
    public class MacroConfig
    {
        /// <summary>
        /// 长按空格变空格连发
        /// </summary>
        public bool SpacePressHoldToContinuation { get; set; } = true;
        /// <summary>
        /// 空格连发时间间隔
        /// </summary>
        public int SpaceFireInterval { get; set; } = 50;

        /// <summary>
        /// 长按F变F连发
        /// </summary>
        public bool FPressHoldToContinuation { get; set; } = true;
        /// <summary>
        /// F连发时间间隔
        /// </summary>
        public int FFireInterval { get; set; } = 50;
    }
}
