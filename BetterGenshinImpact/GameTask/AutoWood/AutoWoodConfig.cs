using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoWood;


/// <summary>
/// 自动伐木配置
/// </summary>
[Serializable]
public partial class AutoWoodConfig : ObservableObject
{
    /// <summary>
    /// 使用小道具后的额外延迟（毫秒）
    /// </summary>
    [ObservableProperty] private int _afterZSleepDelay = 0;
}