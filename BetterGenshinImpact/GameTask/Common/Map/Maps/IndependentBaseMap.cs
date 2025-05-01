using System.Collections.Generic;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

/// <summary>
/// 独立地图
/// </summary>
public abstract class IndependentBaseMap : IIndependentMap
{
    public string Name { get; set; }

    /// <summary>
    /// 地图大小
    /// </summary>
    public Size MapSize { get; set; }

    /// <summary>
    /// 特征点拆分行数
    /// </summary>
    public int SplitRow;

    /// <summary>
    /// 特征点拆分列数
    /// </summary>
    public int SplitCol;
    
    
    // ReSharper disable once ConvertToPrimaryConstructor
    protected IndependentBaseMap(string name, Size mapSize, int splitRow, int splitCol)
    {
        Name = name;
        MapSize = mapSize;
        SplitRow = splitRow;
        SplitCol = splitCol;
    }

    /// <summary>
    /// 分层地图特征列表
    /// 0 是主地图
    /// </summary>
    public List<BaseMapLayer> Layers { get; set; } = [];

    protected BaseMapLayer MainLayer => Layers[0];

    protected readonly Feature2D SiftMatcher = Feature2DFactory.Get(Feature2DType.SIFT);

    public Point2f GetBigMapPosition(Mat greyBigMapMat)
    {
        return SiftMatcher.Match(MainLayer.TrainKeyPoints, MainLayer.TrainDescriptors, greyBigMapMat);
    }

    public Rect GetBigMapRect(Mat greyBigMapMat)
    {
        return SiftMatcher.KnnMatchRect(MainLayer.TrainKeyPoints, MainLayer.TrainDescriptors, greyBigMapMat);
    }

    public Point2f GetMiniMapPosition(Mat greyMiniMapMat)
    {
        // 从表到里逐层匹配
        foreach (var layer in Layers)
        {
            var result = SiftMatcher.Match(layer.TrainKeyPoints, layer.TrainDescriptors, greyMiniMapMat);
            if (result != default)
            {
                return result;
            }
        }

        return default;
    }

    public Point2f GetMiniMapPosition(Mat greyMiniMapMat, float prevX, float prevY)
    {
        foreach (var layer in Layers)
        {
            var (keyPoints, descriptors) = layer.ChooseBlocks(prevX, prevY);
            var result = SiftMatcher.Match(keyPoints, descriptors, greyMiniMapMat);
            if (result != default)
            {
                return result;
            }
        }

        return default;
    }
}