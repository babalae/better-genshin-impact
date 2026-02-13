using System.Collections.Generic;

namespace BetterGenshinImpact.Service.Tavern.Model;

public sealed class ItemTypeVo
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
    /// 创建时间
    /// </summary>
    public string? CreateTime { get; set; }

    /// <summary>
    /// 更新人
    /// </summary>
    public long? UpdaterId { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public string? UpdateTime { get; set; }

    /// <summary>
    /// 物品名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 地区ID（须确保是末端地区）
    /// </summary>
    public long? AreaId { get; set; }

    /// <summary>
    /// 默认刷新时间;单位:毫秒
    /// </summary>
    public long? DefaultRefreshTime { get; set; }

    /// <summary>
    /// 默认描述模板;用于提交新物品点位时的描述模板
    /// </summary>
    public string? DefaultContent { get; set; }

    /// <summary>
    /// 默认数量
    /// </summary>
    public int? DefaultCount { get; set; }

    /// <summary>
    /// 图标ID
    /// </summary>
    public long? IconId { get; set; }

    /// <summary>
    /// 图标样式类型
    /// </summary>
    public int? IconStyleType { get; set; }

    /// <summary>
    /// 隐藏标志
    /// </summary>
    public int? HiddenFlag { get; set; }

    /// <summary>
    /// 物品排序
    /// </summary>
    public int? SortIndex { get; set; }

    /// <summary>
    /// 特殊物品标记;二进制表示；低位第一位：前台是否显示
    /// </summary>
    public int? SpecialFlag { get; set; }

    /// <summary>
    /// 物品类型ID列表
    /// </summary>
    public List<long>? TypeIdList { get; set; }

    /// <summary>
    /// 查询条件下物品总数
    /// </summary>
    public int? Count { get; set; }

    /// <summary>
    /// 物品总数区分
    /// </summary>
    public Dictionary<int, int> CountSplit { get; set; } = new();
}
