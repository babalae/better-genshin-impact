using System;
using System.Collections.Generic;
using System.Drawing;

namespace BetterGenshinImpact.Model.MaskMap;

/// <summary>
/// 点位类型标签
/// </summary>
public class MaskMapPointLabel
{
    public string LabelId { get; set; } = string.Empty;

    public IReadOnlyList<string> LabelIds { get; set; } = Array.Empty<string>();

    public string ParentId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    
    public string IconUrl { get; set; } = string.Empty;

    public int PointCount { get; set; }

    public IReadOnlyList<MaskMapPointLabel> Children { get; set; } = Array.Empty<MaskMapPointLabel>();
    
    /// <summary>
    /// 颜色（如果没有图片时使用，为空则随机生成）
    /// </summary>
    public Color? Color { get; set; }
}
