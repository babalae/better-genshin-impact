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

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 冷却时间（毫秒），防止重复触发
    /// </summary>
    public int CooldownMs { get; set; } = 1000;

    private DateTime _lastExecutionTime = DateTime.MinValue;
    private bool _isExecuting = false;

    public override async Task Run()
    {
        // 热键触发器通常不直接调用Run方法
        // 而是通过热键事件触发ExecuteTasks方法
        await ExecuteTasks();
    }

    /// <summary>
    /// 热键触发时执行任务
    /// </summary>
    public async Task OnHotkeyPressed()
    {
        if (!IsEnabled || _isExecuting)
            return;

        // 检查冷却时间
        var now = DateTime.Now;
        if ((now - _lastExecutionTime).TotalMilliseconds < CooldownMs)
            return;

        _lastExecutionTime = now;
        _isExecuting = true;

        try
        {
            await ExecuteTasks();
        }
        finally
        {
            _isExecuting = false;
        }
    }

    private async Task ExecuteTasks()
    {
        List<BaseGearTask> list = GearTaskRefenceList.Select(gearTask => gearTask.ToGearTask()).ToList();
        foreach (var gearTask in list)
        {
            await gearTask.Run(CancellationToken.None);
        }
    }

    /// <summary>
    /// 获取热键显示文本
    /// </summary>
    public string GetHotkeyText()
    {
        return Hotkey?.ToString() ?? "未设置";
    }
}