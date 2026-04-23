using static BetterGenshinImpact.GameTask.Common.TaskControl;
using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.RoleBasedAutoFight;

public abstract class BaseRoleStrategy : IRoleStrategy
{
    public abstract int EvaluatePriority(RoleBasedAvatarWrapper wrapper, CombatScenes scenes);

    public virtual async Task ExecuteActionAsync(RoleBasedAvatarWrapper wrapper, CombatScenes scenes, CancellationToken ct)
    {
        var avatar = wrapper.BaseAvatar;

        if (avatar.IsBurstReady)
        {
            avatar.UseBurst();
            wrapper.LastActionTime = DateTime.Now;
            await Task.Delay(2000, ct); // 预留 Q 动画时间
        }

        if (avatar.IsSkillReady())
        {
            // 对于大部分辅助角色，短按E即可，个别需要长按可以通过读取配置
            bool isHold = false;
            if (wrapper.CombatRole == "shield") isHold = true; // 盾辅常长按E

            avatar.UseSkill(isHold);
            wrapper.LastActionTime = DateTime.Now;
            await Task.Delay(1000, ct); // E技能后摇
        }
    }
}

public class ShieldStrategy : BaseRoleStrategy
{
    public override int EvaluatePriority(RoleBasedAvatarWrapper wrapper, CombatScenes scenes)
    {
        // 盾辅：如果 E 技能好了，就可以考虑上场
        if (wrapper.BaseAvatar.IsSkillReady())
            return 100;
        return 0;
    }
}

public class HealerStrategy : BaseRoleStrategy
{
    public override int EvaluatePriority(RoleBasedAvatarWrapper wrapper, CombatScenes scenes)
    {
        // 奶妈当做元素附着/增益角色，不看血量，技能好了就放
        if (wrapper.BaseAvatar.IsBurstReady || wrapper.BaseAvatar.IsSkillReady())
            return 80;
        return 0;
    }
}

public class BufferStrategy : BaseRoleStrategy
{
    public override int EvaluatePriority(RoleBasedAvatarWrapper wrapper, CombatScenes scenes)
    {
        if (wrapper.BaseAvatar.IsBurstReady || wrapper.BaseAvatar.IsSkillReady())
            return 80;
        return 0;
    }
}

public class SubDpsStrategy : BaseRoleStrategy
{
    public override int EvaluatePriority(RoleBasedAvatarWrapper wrapper, CombatScenes scenes)
    {
        if (wrapper.BaseAvatar.IsBurstReady || wrapper.BaseAvatar.IsSkillReady())
            return 80;
        return 0;
    }
}

public class MainDpsStrategy : BaseRoleStrategy
{
    public override int EvaluatePriority(RoleBasedAvatarWrapper wrapper, CombatScenes scenes)
    {
        // 兜底输出位
        return 50;
    }

    public override async Task ExecuteActionAsync(RoleBasedAvatarWrapper wrapper, CombatScenes scenes, CancellationToken ct)
    {
        var avatar = wrapper.BaseAvatar;

        if (avatar.IsBurstReady)
        {
            avatar.UseBurst();
            wrapper.LastActionTime = DateTime.Now;
            await Task.Delay(1500, ct);
        }

        if (avatar.IsSkillReady())
        {
            avatar.UseSkill();
            wrapper.LastActionTime = DateTime.Now;
            await Task.Delay(800, ct);
        }

        // 平A输出
        avatar.Attack();
        await Task.Delay(300, ct);
    }
}
