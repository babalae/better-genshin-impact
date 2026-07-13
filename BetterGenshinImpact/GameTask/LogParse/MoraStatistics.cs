using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.LogParse
{
    public class MoraStatistics
    {
        public string Name;
        public DateTime Date;
        public DateTime? StatisticsStart;
        public DateTime? StatisticsEnd;
        public List<ActionItem> ActionItems { get; set; } = new List<ActionItem>();
        public MoraStatistics GetFilterMoraStatistics(Func<ActionItem, bool> predicate)
        {
            MoraStatistics moraStatistics = new MoraStatistics();
            moraStatistics.ActionItems.AddRange(ActionItems.Where(predicate));
            return moraStatistics;
        }

        public List<ActionItem> MonsterActionItems => ActionItems.Where(item => item.ActionId == 37).ToList();

        public List<ActionItem> EliteMonsterActionItems =>
            MonsterActionItems.Where(item => (item.Num >= 200)).ToList();

        public List<ActionItem> SmallMonsterActionItems =>
            MonsterActionItems.Except(EliteMonsterActionItems).ToList();
        public string EmergencyBonus
        {
            get
            {
                var ls = ActionItems.Where(item => item.ActionId == 28).ToList();
                var count = ls.Count();
                if (count == 0)
                {
                    return "";
                }

                return ls.Sum(item=>item.Num)+(count>=10?"":$"({count}/10)");
            }
        }
        public string ChestReward
        {
            get
            {
                var ls = ActionItems.Where(item => item.ActionId == 39).ToList();
                var count = ls.Count();
                if (count == 0)
                {
                    return "";
                }

                return ls.Sum(item=>item.Num)+(count>=10?"":$"({count}/10)");
            }
        }

        public string LastEliteTime => EliteMonsterActionItems.MaxBy(item => item?.Time)?.Time ?? null;
        public string LastSmallTime => SmallMonsterActionItems.MaxBy(item => item?.Time)?.Time ?? null;

        public string EliteDetails => string.Join(", ", EliteMonsterActionItems
            .GroupBy(item => item.Num).OrderBy(item => item.Key) // 按 Num 属性分组
            .Select(group => $"{group.Key}*{group.Count()}"));

        public int EliteStatistics => EliteMonsterActionItems?.Count ?? 0;

        //游戏里的上限计算
        public int EliteGameStatistics => EliteMonsterActionItems.Sum(item =>
        {
            if (item?.Num >= 3000)
            {
                return 3;
            }

            if (item.Num >= 1200)
            {
                return 2;
            }

            return 1;
        });

        public int EliteMora => EliteMonsterActionItems?.Sum(item => item.Num) ?? 0;
        public int SmallMonsterStatistics => SmallMonsterActionItems?.Count ?? 0;
        public int SmallMonsterMora => SmallMonsterActionItems?.Sum(item => item.Num) ?? 0;
        public string SmallMonsterDetails => string.Join(", ", SmallMonsterActionItems
            .GroupBy(item => item.Num/10).OrderBy(item => item.Key) // 按 Num 属性分组
            .Select(group => $"{group.Key}*{group.Count()}"));
        public int TotalMoraKillingMonstersMora => MonsterActionItems.Sum(item => item.Num);
        public int OtherMora => ActionItems.Except(MonsterActionItems).Sum(item => item.Num);
        public int AllMora => ActionItems.Sum(item => item.Num);
    }
}