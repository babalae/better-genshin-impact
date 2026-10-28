using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Infrastructure.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Infrastructure.Cache;

/// <summary>
/// 文件缓存的后台维护（见设计文档 5.3 节），只在主实例运行：启动 2 分钟后执行，每 24 小时最多一次。
/// 删除过期条目、已不存在的分区目录和残留的临时文件；总量超过上限时按最后写入时间删除最早的条目，降到上限的 80%
/// </summary>
/// <param name="cache">文件缓存，提供目录和分区信息</param>
/// <param name="config">存储空间设置，每次维护时读取上限</param>
/// <param name="isRoot">当前进程是否为主实例</param>
/// <param name="timeProvider">时间来源</param>
/// <param name="logger">日志</param>
public sealed class CacheMaintenanceService(
    FileCache cache,
    StorageConfig config,
    bool isRoot,
    TimeProvider timeProvider,
    ILogger<CacheMaintenanceService> logger) : BackgroundService
{
    /// <summary>
    /// 上次维护时间的记录文件名，位于缓存根目录；不写进 config.json，避免配置被频繁保存
    /// </summary>
    internal const string StateFileName = ".maintenance";

    /// <summary>
    /// 启动后等待多久再执行，避开启动高峰
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 两次维护的最小间隔
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>
    /// 临时文件超过这个时间视为残留（写入中断）
    /// </summary>
    private static readonly TimeSpan TempFileAge = TimeSpan.FromHours(1);

    /// <summary>
    /// 上限的最小值，防止配置异常时删光缓存
    /// </summary>
    private const int MinLimitMb = 64;

    /// <summary>
    /// 维护记录文件
    /// </summary>
    private string StateFile => Path.Combine(cache.RootDirectory, StateFileName);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!isRoot)
        {
            return;
        }

        try
        {
            await Task.Delay(StartupDelay, timeProvider, stoppingToken).ConfigureAwait(false);
            // 本进程最近一次维护时间；记录文件写入失败时也不会立即重复执行
            DateTimeOffset? lastRunInProcess = null;
            while (!stoppingToken.IsCancellationRequested)
            {
                // 距上次维护不足 24 小时（含上次运行留下的记录）时，等到期再执行
                var lastRun = Max(ReadLastRun(), lastRunInProcess);
                var wait = lastRun is { } value ? value + Interval - timeProvider.GetUtcNow() : TimeSpan.Zero;
                // 系统时间被调回时记录可能在未来，最多等一个间隔
                wait = wait > Interval ? Interval : wait;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, timeProvider, stoppingToken).ConfigureAwait(false);
                }

                RunOnce(stoppingToken);
                lastRunInProcess = timeProvider.GetUtcNow();
                WriteLastRun();
            }
        }
        catch (OperationCanceledException)
        {
            // 程序退出
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "文件缓存维护失败");
        }
    }

    /// <summary>
    /// 执行一次维护，返回释放的字节数
    /// </summary>
    internal long RunOnce(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        long freed = 0;
        var remaining = new List<FileInfo>();

        // 1. 已不存在的分区目录整体删除
        foreach (var path in cache.ListUnusedEntries())
        {
            ct.ThrowIfCancellationRequested();
            freed += StorageFileOperations.DeletePath(path, ct).FreedBytes;
        }

        // 2. 各分区删除过期条目和残留的临时文件，其余条目参与总量统计
        foreach (var partition in CachePartitions.All)
        {
            foreach (var file in ListFiles(cache.GetPartitionDirectory(partition)))
            {
                ct.ThrowIfCancellationRequested();
                var lastWrite = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
                var isEntry = file.Name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase);
                // 正在写入的临时文件（可能来自其他实例）保留，也不计入总量；其他未知文件直接删除
                var expired = AtomicFile.IsTempFile(file.Name)
                    ? lastWrite + TempFileAge <= now
                    : !isEntry || lastWrite + partition.Ttl <= now;
                if (expired)
                {
                    // 删除后 FileInfo 会重新读取磁盘状态，长度要在删除前取得
                    var length = file.Length;
                    if (TryDelete(file))
                    {
                        freed += length;
                    }
                }
                else if (isEntry)
                {
                    remaining.Add(file);
                }
            }
        }

        // 3. 总量超过上限时，从最早写入的条目开始删除，直到降到上限的 80%
        var limit = Math.Max(config.FileCacheLimitMb, MinLimitMb) * 1024L * 1024;
        var total = remaining.Sum(file => file.Length);
        if (total > limit)
        {
            var target = limit * 8 / 10;
            foreach (var file in remaining.OrderBy(file => file.LastWriteTimeUtc))
            {
                ct.ThrowIfCancellationRequested();
                if (total <= target)
                {
                    break;
                }

                var length = file.Length;
                if (TryDelete(file))
                {
                    total -= length;
                    freed += length;
                }
            }

            // 内存层可能还保存着被删除的条目，一并清空，避免与磁盘不一致太久
            cache.ClearMemory();
        }

        logger.LogInformation("文件缓存维护完成，释放 {FreedMegabytes:F1} MB，当前 {TotalMegabytes:F1} MB",
            freed / 1048576d, total / 1048576d);
        return freed;
    }

    /// <summary>
    /// 列出分区目录中的文件；目录不存在或无法访问时为空
    /// </summary>
    private static IEnumerable<FileInfo> ListFiles(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? new DirectoryInfo(directory).GetFiles() : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// 删除文件；被占用（其他实例正在读取）时跳过
    /// </summary>
    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 两个可能为空的时间中较晚的一个
    /// </summary>
    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : a > b ? a : b;

    /// <summary>
    /// 读取上次维护时间；没有记录或记录损坏时为 null
    /// </summary>
    private DateTimeOffset? ReadLastRun()
    {
        try
        {
            return File.Exists(StateFile)
                   && DateTimeOffset.TryParse(File.ReadAllText(StateFile).Trim(), CultureInfo.InvariantCulture,
                       DateTimeStyles.AssumeUniversal, out var value)
                ? value
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 记录本次维护时间；失败时下次启动会再执行一次，不影响使用
    /// </summary>
    private void WriteLastRun()
    {
        try
        {
            AtomicFile.WriteAllText(StateFile, timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(e, "写入文件缓存维护记录失败");
        }
    }
}
