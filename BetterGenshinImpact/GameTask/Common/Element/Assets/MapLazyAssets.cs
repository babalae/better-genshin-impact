using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.AutoTrackPath.Model;
using BetterGenshinImpact.Service;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BetterGenshinImpact.Model;

namespace BetterGenshinImpact.GameTask.Common.Element.Assets;

public class MapLazyAssets : Singleton<MapLazyAssets>
{

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
        { "纳塔", [8973.5, -1879.1] },
    };
    
    public readonly Dictionary<string, GiWorldPosition> DomainPositionMap = new();
    public readonly List<String> DomainNameList = [];
    // 反方向行走的副本
    public readonly List<string> DomainBackwardList = ["无妄引咎密宫", "芬德尼尔之顶"];

    public MapLazyAssets()
    {
        var json = File.ReadAllText(Global.Absolute(@"GameTask\AutoTrackPath\Assets\tp.json"));
        TpPositions = JsonSerializer.Deserialize<List<GiWorldPosition>>(json, ConfigService.JsonOptions) ?? throw new System.Exception("tp.json deserialize failed");
        
        // 取出秘境 description=Domain
        foreach (var tp in TpPositions.Where(tp => tp.Description == "Domain"))
        {
            DomainPositionMap[tp.Name] = tp;
            DomainNameList.Add(tp.Name);
        }

    }
}
