using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.GameTask.Model;

namespace BetterGenshinImpact.GameTask.RoleBasedAutoFight;

public class RoleBasedAutoFightParam : BaseTaskParam<RoleBasedAutoFightTask>
{
    public int Timeout { get; set; }
    public bool UseNreGadget { get; set; }
    public bool AutoChaseEnemy { get; set; }
    public bool AutoEscapeAbnormal { get; set; }
    public double AntiPreemptionSeconds { get; set; }

    public RoleBasedAutoFightParam(RoleBasedAutoFightConfig config) : base(null, null)
    {
        Timeout = config.Timeout;
        UseNreGadget = config.UseNreGadget;
        AutoChaseEnemy = config.AutoChaseEnemy;
        AutoEscapeAbnormal = config.AutoEscapeAbnormal;
        AntiPreemptionSeconds = config.AntiPreemptionSeconds;
    }
}
