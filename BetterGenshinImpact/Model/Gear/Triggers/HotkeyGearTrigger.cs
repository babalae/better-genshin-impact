using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;
using BetterGenshinImpact.Model.Gear.Tasks;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.Model.Gear.Triggers;

/// <summary>
/// 热键触发器
/// </summary>
public class HotkeyGearTrigger : GearBaseTrigger
{
    /// <summary>
    /// 热键配置
    /// </summary>
    public HotKey? Hotkey { get; set; }
}