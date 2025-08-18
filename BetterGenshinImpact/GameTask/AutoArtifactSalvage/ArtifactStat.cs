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
    }
}
