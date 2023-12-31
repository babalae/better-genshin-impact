using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoFight.Model.Scripts;

public class CombatScript
{
    public record Command(string Name, Dictionary<string, object> Parameters);

}