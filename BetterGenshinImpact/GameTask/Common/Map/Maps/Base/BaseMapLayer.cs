using System.Diagnostics;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using BetterGenshinImpact.Core.Recognition.OpenCv.Model;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

/// <summary>
/// 每层的特征
/// </summary>
public class BaseMapLayer(IndependentBaseMap baseMap)
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
    public KeyPointFeatureBlock[][] SplitBlocks { get; set; } = [];

    /// <summary>
    /// 最近一次合并的特征块
    /// </summary>
    private KeyPointFeatureBlock? _lastMergedBlock = null;

    public (KeyPoint[], Mat) ChooseBlocks(float prevX, float prevY)
    {
        var (cellRow, cellCol) = KeyPointFeatureBlockHelper.GetCellIndex(baseMap.MapSize, baseMap.SplitRow, baseMap.SplitCol, prevX, prevY);
        Debug.WriteLine($"当前坐标({prevX},{prevY})在特征块({cellRow},{cellCol})中");
        if (_lastMergedBlock == null || _lastMergedBlock.MergedCenterCellRow != cellRow || _lastMergedBlock.MergedCenterCellCol != cellCol)
        {
            Debug.WriteLine($"---------切换到新的特征块({cellRow},{cellCol})，合并特征点--------");
            _lastMergedBlock = KeyPointFeatureBlockHelper.MergeNeighboringFeatures(SplitBlocks, TrainDescriptors, cellRow, cellCol);
        }
        return (_lastMergedBlock.KeyPointArray, _lastMergedBlock.Descriptor!);
    }
}