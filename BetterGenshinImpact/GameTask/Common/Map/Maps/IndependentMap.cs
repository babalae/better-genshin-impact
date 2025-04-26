using System;
using System.Collections.Generic;
using BetterGenshinImpact.Core.Recognition.OpenCv.Model;

namespace BetterGenshinImpact.GameTask.Common.Map.Maps;

/// <summary>
/// 独立地图
/// </summary>
public class IndependentMap
{
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 0 是主地图
    /// </summary>
    public List<BaseMapLayer> Layers { get; set; } = [];
}