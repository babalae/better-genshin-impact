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
    

    private DateTime _lastExecutionTime = DateTime.MinValue;
    

    /// <summary>
    /// 热键触发时执行任务
    /// </summary>
    public async Task OnHotkeyPressed()
    {
    }

    /// <summary>
    /// 获取热键显示文本
    /// </summary>
    public string GetHotkeyText()
    {
        return Hotkey?.ToString() ?? "未设置";
    }
}