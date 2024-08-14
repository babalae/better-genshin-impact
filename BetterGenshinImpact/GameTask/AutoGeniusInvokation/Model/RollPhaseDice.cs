using System;
using System.Drawing;

namespace BetterGenshinImpact.GameTask.AutoGeniusInvokation.Model
{
    /// <summary>
    /// 投掷期间骰子
    /// </summary>
    [Obsolete]
    public class RollPhaseDice
    {
        /// <summary>
        /// 元素类型
        /// </summary>
        public ElementalType Type { get; set; }

        /// <summary>
        /// 中心点位置
        /// </summary>
        public Point CenterPosition { get; set; }

        public RollPhaseDice(ElementalType type, Point centerPosition)
        {
            Type = type;
            CenterPosition = centerPosition;
        }

        public RollPhaseDice()
        {
        }

        public override string ToString()
        {
            return $"Type:{Type},CenterPosition:{CenterPosition}";
        }

        public void Click()
        {
            //MouseUtils.Click(CenterPosition.X, CenterPosition.Y);
        }
    }
}
