using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.Service.Guide;

/// <summary>
/// 一条收藏
/// </summary>
public sealed class GuideFavorite
{
    /// <summary>
    /// 收藏 Id（GUID）
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 视频标识，见设计文档 10.1 节
    /// </summary>
    public string VideoKey { get; set; } = string.Empty;

    /// <summary>
    /// 标题，用户可以重命名
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 打开用的网址
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 收藏时间
    /// </summary>
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// 最近一次从收藏打开的时间
    /// </summary>
    public DateTimeOffset? LastOpenedAt { get; set; }
}

/// <summary>
/// 一条观看记录
/// </summary>
public sealed class GuideWatchRecord
{
    /// <summary>
    /// 视频标识，见设计文档 10.1 节
    /// </summary>
    public string VideoKey { get; set; } = string.Empty;

    /// <summary>
    /// 标题
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 打开用的网址
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 观看位置（秒）
    /// </summary>
    public double Position { get; set; }

    /// <summary>
    /// 时长（秒），未知为 0
    /// </summary>
    public double Duration { get; set; }

    /// <summary>
    /// 倍速，不含临时加速
    /// </summary>
    public double PlaybackRate { get; set; } = 1;

    /// <summary>
    /// 是否已看完
    /// </summary>
    public bool Finished { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// 复制一份，供界面展示或跨线程传递
    /// </summary>
    public GuideWatchRecord Clone() => (GuideWatchRecord)MemberwiseClone();
}

/// <summary>
/// favorites.json 的内容
/// </summary>
internal sealed class GuideFavoritesFile
{
    /// <summary>
    /// 文件格式版本
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// 收藏列表
    /// </summary>
    public List<GuideFavorite> Items { get; set; } = [];
}

/// <summary>
/// history.json 的内容
/// </summary>
internal sealed class GuideHistoryFile
{
    /// <summary>
    /// 文件格式版本
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// 上次打开的页面，用于恢复打开小窗
    /// </summary>
    public string? LastUrl { get; set; }

    /// <summary>
    /// 观看记录，最近的在前
    /// </summary>
    public List<GuideWatchRecord> Items { get; set; } = [];
}
