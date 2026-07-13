namespace BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;

/// <summary>
/// FlannMatch
/// 优点：
/// 1.	速度快：FlannMatch 使用的是近似最近邻搜索算法，适合处理大规模数据集，速度比暴力匹配器快得多。
/// 2.	可扩展性好：适用于高维特征空间，能够处理大量特征点。
/// 缺点：
/// 1.	精度较低：由于是近似搜索，匹配的精度可能不如暴力匹配器高。
/// 2.	复杂性高：需要对参数进行调优，以获得最佳性能。
/// BfMatch
/// 优点：
/// 1.	精度高：BfMatch 进行的是精确匹配，能够找到最优的匹配对。
/// 2.	简单易用：不需要复杂的参数调优，适合初学者和简单应用。
/// 缺点：
/// 1.	速度慢：对于大规模数据集，暴力匹配器的速度较慢，因为它需要计算所有特征点之间的距离。
/// 2.	不适合高维数据：在高维特征空间中，计算量会显著增加，性能下降明显。
/// 选择建议
/// •	大规模数据集：如果你需要处理大量特征点，且对速度要求较高，FlannMatch 是一个更好的选择。
/// •	高精度要求：如果你的应用对匹配精度要求很高，且数据规模较小，BfMatch 会更合适。
/// </summary>
public enum DescriptorMatcherType
{
    // 快速近似最近邻搜索库
    BruteForce,

    // 暴力匹配器
    FlannBased

    // 更多类型命名见 DescriptorMatcher.Create
}
