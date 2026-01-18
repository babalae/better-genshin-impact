using System.Text;

namespace BetterGenshinImpact.GameTask.AutoArtifactSalvage
{
    /// <summary>
    /// 圣遗物数值面板信息
    /// </summary>
    public class ArtifactStat
    {
        public ArtifactStat(string name, ArtifactAffix mainAffix, ArtifactAffix[] minorAffix, int level)
        {
            Name = name;
            MainAffix = mainAffix;
            MinorAffixes = minorAffix;
            Level = level;
        }

        /// <summary>
        /// 名称
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// 主词条
        /// </summary>
        public ArtifactAffix MainAffix { get; private set; }

        /// <summary>
        /// 副词条数组
        /// </summary>
        public ArtifactAffix[] MinorAffixes { get; private set; }

        /// <summary>
        /// 等级
        /// </summary>
        public int Level { get; private set; }

        // PS：圣遗物的种类和品质在点击查看之前就可以通过识别图标获悉，所以不必在此模型类中获取

        /// <summary>
        /// 生成一个手工拼接的成员结构示意字符串
        /// </summary>
        /// <returns></returns>
        public string ToStructuredString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Properties").Append('\n');
            sb.Append("├─").Append("Name: ").Append(this.Name).Append('\n');
            sb.Append("├─").Append("MainAffix: ").Append(this.MainAffix.Type).Append(", ").Append(this.MainAffix.Value).Append('\n');
            sb.Append("├─").Append("MinorAffixes: ").Append('\n');
            for (int i = 0; i < this.MinorAffixes.Length; i++)
            {
                sb.Append('│').Append('\t').Append(i == this.MinorAffixes.Length - 1 ? "└─" : "├─").Append($"[{i}]: ").Append(this.MinorAffixes[i].Type).Append(", ").Append(this.MinorAffixes[i].Value);
                if (this.MinorAffixes[i].IsUnactivated) {
                    sb.Append(", Unactivated");
                }
                sb.Append('\n');
            }
            sb.Append("└─").Append("Level: ").Append(this.Level);
            return sb.ToString();
        }
    }
}
