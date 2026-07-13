using System.Collections.Generic;

namespace BetterGenshinImpact.Service.Tavern.Model;

/// <summary>
/// 图标主表前端封装
/// </summary>
public sealed class IconVo
{
    /// <summary>
    /// 乐观锁
    /// </summary>
    public long? Version { get; set; }

    /// <summary>
    /// ID
    /// </summary>
    public long? Id { get; set; }

    /// <summary>
    /// 创建人
    /// </summary>
    public long? CreatorId { get; set; }

    /// <summary>
    /// 创建时间（时间戳毫秒）
    /// </summary>
    public string? CreateTime { get; set; }

    /// <summary>
    /// 更新人
    /// </summary>
    public long? UpdaterId { get; set; }

    /// <summary>
    /// 更新时间（时间戳毫秒）
    /// </summary>
    public string? UpdateTime { get; set; }

    /// <summary>
    /// 图标标签
    /// </summary>
    public string? Tag { get; set; }

    /// <summary>
    /// 图标类型ID列表
    /// </summary>
    public List<long>? TypeIdList { get; set; }

    /// <summary>
    /// 图标url
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// 图标变体url
    /// </summary>
    public Dictionary<string, string>? UrlVariants { get; set; }

    /// <summary>
    /// 图标描述
    /// </summary>
    public string? Description { get; set; }
}