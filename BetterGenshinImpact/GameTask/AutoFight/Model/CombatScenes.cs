using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// 战斗场景
/// </summary>
public class CombatScenes
{
    /// <summary>
    /// 当前配队
    /// </summary>
    public Character[] Characters { get; set; } = new Character[6];


    public void InitializeTeam()
    {

    }


}