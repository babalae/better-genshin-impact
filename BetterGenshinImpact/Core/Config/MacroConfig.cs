using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Core.Config
{
    [Serializable]
    public partial class MacroConfig : ObservableObject
    {
        /// <summary>
        /// 长按空格变空格连发
        /// </summary>
        [ObservableProperty] private bool _spacePressHoldToContinuationEnabled = false;

        /// <summary>
        /// 空格连发时间间隔
        /// </summary>
        [ObservableProperty] private int _spaceFireInterval = 100;

        /// <summary>
        /// 长按F变F连发
        /// </summary>
        [ObservableProperty] private bool _fPressHoldToContinuationEnabled = false;

        /// <summary>
        /// F连发时间间隔
        /// </summary>
        [ObservableProperty] private int _fFireInterval = 100;

        /// <summary>
        /// 高延迟下强化的额外等待时间
        /// https://github.com/babalae/better-genshin-impact/issues/9
        /// </summary>
        [ObservableProperty] private int _enhanceWaitDelay = 0;
    }
}