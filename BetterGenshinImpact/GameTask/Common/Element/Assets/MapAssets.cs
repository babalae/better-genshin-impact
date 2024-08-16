using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.Service;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BetterGenshinImpact.GameTask.Common.Element.Assets;

public class MapAssets : BaseAssets<MapAssets>
{
    public Lazy<Mat> MainMap2048BlockMat { get; } = new(() => new Mat(@"E:\HuiTask\更好的原神\地图匹配\有用的素材\mainMap2048Block.png", ImreadModes.Grayscale));

    public Lazy<Mat> MainMap256BlockMat { get; } = new(() => new Mat(Global.Absolute(@"Assets\Map\mainMap256Block.png"), ImreadModes.Grayscale));

    // 2048 区块下，存在传送点的最大面积，识别结果比这个大的话，需要点击放大

    // 传送点信息

    public List<GiWorldPosition> TpPositions;

    // 每个地区点击后处于的中心位置
    public readonly Dictionary<string, double[]> CountryPositions = new()
    {
        { "蒙德", [-876, 2278] },
        { "璃月", [270, -666] },
        { "稻妻", [-4400, -3050] },
        { "须弥", [2877, -374] },
        { "枫丹", [4515, 3631] },
    };

    public MapAssets()
    {
        var json = File.ReadAllText(Global.Absolute(@"GameTask\AutoTrackPath\Assets\tp.json"));
        TpPositions = JsonSerializer.Deserialize<List<GiWorldPosition>>(json, ConfigService.JsonOptions) ?? throw new Exception("tp.json deserialize failed");
    }
}
