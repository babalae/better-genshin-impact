using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPick
{
    /// <summary>
    /// 非16:9分辨率下可能无法正常工作
    /// </summary>
    [Serializable]
    public partial class AutoPickConfig : ObservableObject
    {
        /// <summary>
        /// 触发器是否启用
        /// </summary>
        [ObservableProperty] private bool _enabled = true;

        /// <summary>
        /// 1080p下拾取文字的起始偏移
        /// </summary>
        [ObservableProperty] private int _fLeftOffset = 115;
        /// <summary>
        /// 1080p下拾取文字的终止偏移
        /// </summary>
        [ObservableProperty] private int _fRightOffset = 400;
    }
}