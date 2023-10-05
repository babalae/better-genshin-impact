using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPick
{
    [Serializable]
    public partial class AutoPickConfig : ObservableObject
    {
        /// <summary>
        /// 触发器是否启用
        /// </summary>
        [ObservableProperty] private bool _enabled = false;
    }
}
