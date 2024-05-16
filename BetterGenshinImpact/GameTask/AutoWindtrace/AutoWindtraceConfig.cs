using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoWindtrace;


/// <summary>
/// 自动风行迷踪配置
/// </summary>
[Serializable]
public partial class AutoWindtraceConfig : ObservableObject
{
    /// <summary>
    /// 使用小道具后的额外延迟（毫秒）
    /// </summary>
    //[ObservableProperty] private int _PanelSleepDelay = 0;
}