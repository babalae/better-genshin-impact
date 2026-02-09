using BetterGenshinImpact.Helpers;
﻿using System;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model
{
    public class ActionCommand
    {
        /// <summary>
        ///  角色
        /// </summary>
        public Character Character { get; set; } = default!;

        public ActionEnum Action { get; set; }

        /// <summary>
        /// 目标编号（技能编号，从右往左）
        /// </summary>
        public int TargetIndex { get; set; }

        public override string? ToString()
        {
            if (Action == ActionEnum.UseSkill)
            {
                if (string.IsNullOrEmpty(Character.Skills[TargetIndex].Name))
                {
                    return $"{Lang.S["GameTask_10934_8ebc76"]};
                }
                else
                {
                    return $"{Lang.S["GameTask_10933_397371"]};
                }
            }
            else if (Action == ActionEnum.SwitchLater)
            {
                return $"{Lang.S["GameTask_10932_6cc49b"]};
            }
            else
            {
                return base.ToString();
            }
        }


        public int GetSpecificElementDiceUseCount()
        {
            if (Action == ActionEnum.UseSkill)
            {
                return Character.Skills[TargetIndex].SpecificElementCost;
            }
            else
            {
                throw new ArgumentException(Lang.S["GameTask_10931_1b209c"]);
            }
        }

        public int GetAllDiceUseCount()
        {
            if (Action == ActionEnum.UseSkill)
            {
                return Character.Skills[TargetIndex].AllCost;
            }
            else
            {
                throw new ArgumentException(Lang.S["GameTask_10931_1b209c"]);
            }
        }

        public ElementalType GetDiceUseElementType()
        {
            if (Action == ActionEnum.UseSkill)
            {
                return Character.Element;
            }
            else
            {
                throw new ArgumentException(Lang.S["GameTask_10931_1b209c"]);
            }
        }

        public bool SwitchLater()
        {
            return Character.SwitchLater();
        }

        public bool UseSkill(Duel duel)
        {
            return Character.UseSkill(TargetIndex, duel);
        }
    }
}