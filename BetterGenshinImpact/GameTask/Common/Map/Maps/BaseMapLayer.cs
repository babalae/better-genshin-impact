using BetterGenshinImpact.Core.Recognition.OpenCv.Model;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

/// <summary>
/// 每层的特征
/// </summary>
public class BaseMapLayer
{
    public string Name { get; set; } = string.Empty;
    
    public  KeyPointFeatureBlock[][] FeatureBlocks { get; set; } = [];

}