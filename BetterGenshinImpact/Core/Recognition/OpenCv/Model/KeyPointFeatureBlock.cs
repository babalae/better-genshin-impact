using OpenCvSharp;
using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Recognition.OpenCv.Model;

/// <summary>
/// 特征块
/// 对特征按图像区域进行划分
/// </summary>
public class KeyPointFeatureBlock
{
    public List<KeyPoint> KeyPointList { get; set; } = [];

    private KeyPoint[]? keyPointArray;

    public KeyPoint[] KeyPointArray
    {
        get
        {
            keyPointArray ??= [.. KeyPointList];
            return keyPointArray;
        }
    }

    /// <summary>
    /// 在完整 KeyPoint[] 中的下标
    /// </summary>
    public List<int> KeyPointIndexList { get; set; } = [];

    public Mat? Descriptor;

    public int MergedCenterCellCol = -1;
    public int MergedCenterCellRow = -1;
}
