using BetterGenshinImpact.Core.Recognition.OpenCv.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

/// <summary>
/// 每层的特征
/// </summary>
public class BaseMapLayer
{
    // public string Name { get; set; } = string.Empty;
    //
    // public string LayerId { get; set; } = string.Empty;
    //
    // public string LayerGroupId { get; set; } = string.Empty;
    
    /// <summary>
    /// 层级
    /// </summary>
    public int Floor { get; set; } = 0;
    
    /// <summary>
    /// 当前层的所有特征
    /// </summary>
    public Mat TrainDescriptors { get; set; } = new();
    public KeyPoint[] TrainKeyPoints { get; set; } = [];
    
    /// <summary>
    /// 切割后的特征块
    /// </summary>
    public  KeyPointFeatureBlock[][] SplitFeatureBlocks { get; set; } = [];
    

}