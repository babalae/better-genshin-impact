using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Infrastructure.Storage;

/// <summary>
/// 存储区域分组，决定在存储空间窗口中的分组和图标
/// </summary>
public enum StorageGroup
{
    /// <summary>可再生的缓存</summary>
    Cache,

    /// <summary>下载的文件</summary>
    Download,

    /// <summary>网页数据</summary>
    WebData,

    /// <summary>临时文件</summary>
    Temp,

    /// <summary>日志、截图与调试图片</summary>
    Diagnostics,

    /// <summary>用户数据</summary>
    UserData,
}

/// <summary>
/// 清理时机
/// </summary>
public enum StorageCleanupTiming
{
    /// <summary>立即删除，正在使用的文件跳过</summary>
    Immediate,

    /// <summary>文件被进程长期占用（推理缓存、网页数据），记录后在下次启动、使用方加载前删除</summary>
    NextStartup,

    /// <summary>只展示占用，不提供清理</summary>
    None,
}

/// <summary>
/// 一块可在存储空间窗口展示和清理的磁盘区域。各功能通过 <see cref="IStorageAreaProvider"/> 登记
/// </summary>
/// <param name="Id">区域标识，全局唯一</param>
/// <param name="DisplayName">展示名称</param>
/// <param name="Group">所属分组</param>
/// <param name="Paths">区域包含的文件或目录，必须位于程序目录内</param>
public sealed record StorageArea(string Id, string DisplayName, StorageGroup Group, IReadOnlyList<string> Paths)
{
    /// <summary>
    /// 这块空间存的是什么
    /// </summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// 清理后用户会感受到什么，例如“下次打开地图时重新下载图标”
    /// </summary>
    public string CleanupEffect { get; init; } = "";

    /// <summary>
    /// 清理时机
    /// </summary>
    public StorageCleanupTiming Timing { get; init; } = StorageCleanupTiming.Immediate;

    /// <summary>
    /// 会丢失用户能感知的数据（登录状态、脚本网页设置、截图）时为 true，清理前需确认
    /// </summary>
    public bool RequiresConfirmation { get; init; }

    /// <summary>
    /// 是否包含在“一键清理”中。需要确认的区域不应包含
    /// </summary>
    public bool IncludedInQuickCleanup { get; init; } = true;

    /// <summary>
    /// 返回非空字符串表示当前不能清理及原因，例如“实例正在运行”
    /// </summary>
    public Func<string?>? GetBusyReason { get; init; }

    /// <summary>
    /// 自定义清理；为空时删除 Paths 中的文件和目录内的全部内容
    /// </summary>
    public Func<CancellationToken, Task<StorageCleanupResult>>? CleanupAsync { get; init; }
}

/// <summary>
/// 清理结果
/// </summary>
/// <param name="FreedBytes">释放的字节数</param>
/// <param name="SkippedFiles">因被占用或无权限而跳过的文件数</param>
/// <param name="Deferred">是否推迟到下次启动执行</param>
public sealed record StorageCleanupResult(long FreedBytes, int SkippedFiles, bool Deferred)
{
    /// <summary>
    /// 没有删除任何内容的结果
    /// </summary>
    public static StorageCleanupResult Empty { get; } = new(0, 0, false);

    /// <summary>
    /// 合并两次清理的结果
    /// </summary>
    public StorageCleanupResult Add(StorageCleanupResult other) =>
        new(FreedBytes + other.FreedBytes, SkippedFiles + other.SkippedFiles, Deferred || other.Deferred);
}
