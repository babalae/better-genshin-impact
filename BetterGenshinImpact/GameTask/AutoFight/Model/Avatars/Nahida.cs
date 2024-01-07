using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoFight.Model.Avatars;

public class Nahida : Avatar
{
    public Nahida(CombatScenes combatScenes, string name, int index, Rect nameRect) : base(combatScenes, name, index, nameRect)
    {
    }

    public new void UseSkill(bool hold = false)
    {
        throw new NotImplementedException();
    }
}