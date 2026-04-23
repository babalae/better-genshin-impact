using static BetterGenshinImpact.GameTask.Common.TaskControl;
using System;
using System.Collections.Generic;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.Common;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.RoleBasedAutoFight;

public class RoleBasedAvatarWrapper
{
    public Avatar BaseAvatar { get; }
    
    // 定位: shield, healer, buffer, sub_dps, main_dps
    public string CombatRole { get; }
    
    // 最后一次执行技能的时间（用于避免技能还没放完就切人）
    public DateTime LastActionTime { get; set; } = DateTime.MinValue;
    
    // 全局防插队锁
    private static DateTime _lastGlobalSwitchTime = DateTime.MinValue;

    public RoleBasedAvatarWrapper(Avatar baseAvatar)
    {
        BaseAvatar = baseAvatar;
        
        var roles = baseAvatar.CombatAvatar.CombatRole;
        if (roles != null && roles.Count > 0)
        {
            CombatRole = roles[0];
        }
        else
        {
            CombatRole = "sub_dps"; // 默认
        }
    }

    /// <summary>
    /// 尝试切换到此角色，自带防插队锁机制
    /// </summary>
    public bool TrySwitch(double antiPreemptionSeconds)
    {
        // 检查防插队锁
        if ((DateTime.Now - _lastGlobalSwitchTime).TotalSeconds < antiPreemptionSeconds)
        {
            TaskControl.Logger.LogTrace($"[防插队锁] 切人冷却中，放弃切换至 {BaseAvatar.Name}");
            return false;
        }

        // 尝试底层切人
        bool success = BaseAvatar.TrySwitch(2);
        if (success)
        {
            _lastGlobalSwitchTime = DateTime.Now;
            TaskControl.Logger.LogInformation($"[角色切换] 成功切换至 {BaseAvatar.Name}，激活防插队锁({antiPreemptionSeconds}s)");
        }
        return success;
    }
}
