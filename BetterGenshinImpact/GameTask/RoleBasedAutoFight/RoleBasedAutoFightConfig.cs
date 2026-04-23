using static BetterGenshinImpact.GameTask.Common.TaskControl;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace BetterGenshinImpact.GameTask.RoleBasedAutoFight;

[Serializable]
public partial class RoleBasedAutoFightConfig : ObservableObject
{
    [ObservableProperty] private int _timeout = 300;
    
    [ObservableProperty] private bool _useNreGadget = true;
    
    [ObservableProperty] private bool _autoChaseEnemy = true;
    
    [ObservableProperty] private bool _autoEscapeAbnormal = true;
    
    [ObservableProperty] private double _antiPreemptionSeconds = 1.5;

    public void Sync(RoleBasedAutoFightConfig source)
    {
        Timeout = source.Timeout;
        UseNreGadget = source.UseNreGadget;
        AutoChaseEnemy = source.AutoChaseEnemy;
        AutoEscapeAbnormal = source.AutoEscapeAbnormal;
        AntiPreemptionSeconds = source.AntiPreemptionSeconds;
    }

    public RoleBasedAutoFightConfig Clone()
    {
        return new RoleBasedAutoFightConfig
        {
            Timeout = Timeout,
            UseNreGadget = UseNreGadget,
            AutoChaseEnemy = AutoChaseEnemy,
            AutoEscapeAbnormal = AutoEscapeAbnormal,
            AntiPreemptionSeconds = AntiPreemptionSeconds
        };
    }
}
