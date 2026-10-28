using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Infrastructure.Cache;

/// <summary>
/// 缓存分区：一个功能的一类缓存。决定磁盘子目录、有效期和在存储空间窗口中的名称
/// </summary>
/// <param name="Id">子目录名，只使用小写字母、数字和短横线</param>
/// <param name="DisplayName">展示名称</param>
/// <param name="Ttl">有效期，按文件最后写入时间计算</param>
public sealed record CachePartition(string Id, string DisplayName, TimeSpan Ttl)
{
    /// <summary>
    /// 存储空间窗口中的说明：这里存的是什么
    /// </summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// 清理后的影响，展示在存储空间窗口中
    /// </summary>
    public string CleanupEffect { get; init; } = "需要时重新下载";
}

/// <summary>
/// 已定义的缓存分区。新功能在这里新增一项并加入 <see cref="All"/>，即可使用缓存并出现在存储空间窗口中
/// </summary>
public static class CachePartitions
{
    /// <summary>
    /// 地图遮罩上的点位图标
    /// </summary>
    public static readonly CachePartition MapIcons = new("map-icons", "地图点位图标", TimeSpan.FromDays(20))
    {
        Description = "地图遮罩上的点位图标",
        CleanupEffect = "打开地图时重新下载",
    };

    /// <summary>
    /// 点击地图点位时显示的详情图片
    /// </summary>
    public static readonly CachePartition MapPointImages = new("map-point-images", "点位详情图片", TimeSpan.FromDays(7))
    {
        Description = "点击地图点位时显示的图片",
        CleanupEffect = "再次查看时重新下载",
    };

    /// <summary>
    /// 空荧酒馆点位数据（按页缓存，加载时只保留最新的页）
    /// </summary>
    public static readonly CachePartition KongyingTavern = new("kongying-tavern", "空荧酒馆点位数据", TimeSpan.FromDays(30))
    {
        Description = "地图遮罩使用的空荧酒馆点位数据",
        CleanupEffect = "下次加载点位时重新获取",
    };

    /// <summary>
    /// 自动演奏曲目的封面
    /// </summary>
    public static readonly CachePartition MusicArtwork = new("music-artwork", "音乐封面", TimeSpan.FromDays(180))
    {
        Description = "自动演奏曲目的封面图片",
    };

    /// <summary>
    /// 更新日志、脚本说明等 Markdown 文档中的网络图片
    /// </summary>
    public static readonly CachePartition MarkdownImages = new("markdown-images", "文档图片", TimeSpan.FromDays(7))
    {
        Description = "更新日志、脚本说明等文档中的网络图片",
        CleanupEffect = "再次打开文档时重新下载",
    };

    /// <summary>
    /// 全部分区，供存储区域登记和缓存维护使用
    /// </summary>
    public static IReadOnlyList<CachePartition> All { get; } =
        [MapIcons, MapPointImages, KongyingTavern, MusicArtwork, MarkdownImages];
}
