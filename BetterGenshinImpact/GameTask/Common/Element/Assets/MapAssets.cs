using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;
using System;

namespace BetterGenshinImpact.GameTask.Common.Element.Assets;

public class MapAssets : BaseAssets<MapAssets>
{
    public Lazy<Mat> MainMap100BlockMat { get; } = new(() => new Mat(Global.Absolute(@"Assets\Map\mainMap100Block.png")));

    public Lazy<Mat> MainMap1024BlockMat { get; } = new(() => new Mat(@"E:\HuiTask\更好的原神\地图匹配\有用的素材\mainMap1024Block.png", ImreadModes.Grayscale));

    public Lazy<Mat> MainMap2048BlockMat { get; } = new(() => new Mat(@"E:\HuiTask\更好的原神\地图匹配\有用的素材\mainMap2048Block.png", ImreadModes.Grayscale));

    // 每个地区点击后处于的中心位置

    // 2048 区块下，存在传送点的最大面积，识别结果比这个大的话，需要点击放大

    // 传送点信息
}
