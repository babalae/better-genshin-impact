using System;
using System.Collections.Generic;
using System.Linq;

namespace LogParse
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

        public List<ActionItem> MonsterActionItems => this.ActionItems.Where(item => item.ActionId == 37).ToList();

        public List<ActionItem> EliteMonsterActionItems =>
            this.MonsterActionItems.Where(item => (item.Num >= 200)).ToList();

        public List<ActionItem> SmallMonsterActionItems =>
            this.MonsterActionItems.Except(EliteMonsterActionItems).ToList();
        public string EmergencyBonus
        {
            get
            {
                var ls = this.ActionItems.Where(item => item.ActionId == 28).ToList();
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
            else if (item.Num >= 1200)
            {
                return 2;
            }
            else
            {
                return 1;
            }
        });

        public int EliteMora => EliteMonsterActionItems?.Sum(item => item.Num) ?? 0;
        public int SmallMonsterStatistics => SmallMonsterActionItems?.Count ?? 0;
        public int SmallMonsterMora => SmallMonsterActionItems?.Sum(item => item.Num) ?? 0;
        public int TotalMoraKillingMonstersMora => this.MonsterActionItems.Sum((item) => item.Num);
        public int OtherMora => this.ActionItems.Except(MonsterActionItems).Sum((item) => item.Num);
        public int AllMora => this.ActionItems.Sum((item) => item.Num);

        public MoraStatistics()
        {
        }
    }
}