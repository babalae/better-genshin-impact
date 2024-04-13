using System.Collections.Generic;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OpenCv.Model;

/// <summary>
/// 特征块
/// 对特征按图像区域进行划分
/// </summary>
public class KeyPointFeatureBlock
{
    public List<KeyPoint> KeyPointList { get; } = new();

    /// <summary>
    /// 在完整 KeyPoint[] 中的下标
    /// </summary>
    public List<int> KeyPointIndexList { get; } = new();

    public Mat? Descriptor;
}
