using System;

namespace BetterGenshinImpact.Model;

[Serializable]
public class OneDragonTaskCondition
{
    public bool RunMonday { get; set; } = false;

    public bool RunTuesday { get; set; } = false;

    public bool RunWednesday { get; set; } = false;

    public bool RunThursday { get; set; } = false;

    public bool RunFriday { get; set; } = false;

    public bool RunSaturday { get; set; } = false;

    public bool RunSunday { get; set; } = false;

    public OneDragonTaskCondition()
    {
    }
}
