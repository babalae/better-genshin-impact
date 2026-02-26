using System;

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
        
        /// <summary>
        /// 灵活改变骰子的数量（因为在不同的牌局中或者角色技能中会发生骰子实际需要的数量增加或减少）
        /// </summary>
        public int DiceDelta { get; set; } = 0;

        public override string? ToString()
        {
            if (Action == ActionEnum.UseSkill)
            {
                if (string.IsNullOrEmpty(Character.Skills[TargetIndex].Name))
                {
                    return $"【{Character.Name}】使用【技能{TargetIndex}】{(DiceDelta != 0 ? $"(骰子{(DiceDelta > 0 ? "增加" : "减少")}{Math.Abs(DiceDelta)})" : "")}";
                }
                else
                {
                    return $"【{Character.Name}】使用【{Character.Skills[TargetIndex].Name}】{(DiceDelta != 0 ? $"(骰子{(DiceDelta > 0 ? "增加" : "减少")}{Math.Abs(DiceDelta)})" : "")}";
                }
            }
            else if (Action == ActionEnum.SwitchLater)
            {
                return $"【{Character.Name}】切换至【角色{TargetIndex}】";
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
                throw new ArgumentException("未知行动");
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
                throw new ArgumentException("未知行动");
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
                throw new ArgumentException("未知行动");
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