using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Model;
using OpenCvSharp;
using System;

namespace BetterGenshinImpact.GameTask.Common.Element.Assets;

public class MapAssets : BaseAssets<MapAssets>
{
    public Lazy<Mat> MainMap100BlockMat { get; } = new(() => new Mat(Global.Absolute(@"Assets\Map\mainMap100Block.png")));

    public Lazy<Mat> MainMap1024BlockMat { get; } = new(() => new Mat(@"E:\HuiTask\更好的原神\地图匹配\有用的素材\mainMap1024Block.png", ImreadModes.Grayscale));
}
