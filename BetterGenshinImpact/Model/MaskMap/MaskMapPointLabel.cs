using System.Drawing;

namespace BetterGenshinImpact.Model.MaskMap;

/// <summary>
/// 点位类型标签
/// </summary>
public class MaskMapPointLabel
{
    public string LabelId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    
    public string IconUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// 颜色（如果没有图片时使用，为空则随机生成）
    /// </summary>
    public Color? Color { get; set; }
}