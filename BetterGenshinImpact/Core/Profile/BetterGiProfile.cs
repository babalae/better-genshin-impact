using System;

namespace BetterGenshinImpact.Core.Profile;

/// <summary>
/// BetterGI 通用运行实例的索引信息。
/// 这里只保存跨模块的基础字段，云原神专属配置存放在 Modules/CloudGame.json。
/// </summary>
public sealed class BetterGiProfile
{
    /// <summary>
    /// Profile 的稳定唯一标识，同时作为实例目录名和会话 ID。
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// 用户可编辑的实例显示名称。
    /// </summary>
    public string Name { get; set; } = "云原神";

    /// <summary>
    /// 运行目标类型，用于后续加载对应模块。
    /// </summary>
    public ProfileType Type { get; set; } = ProfileType.CloudGame;

    /// <summary>
    /// Profile 首次创建时间。
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
