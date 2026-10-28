using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Infrastructure.Storage;

/// <summary>
/// 推迟到下次启动执行的清理。用于被进程长期占用的目录（推理编译缓存、网页数据）：
/// 用户点击清理时只登记路径，主实例下次启动、使用方加载之前再删除其中的内容。
/// 记录保存在 Data/pending-cleanup.json，路径以相对程序目录的形式保存。
/// </summary>
/// <remarks>
/// 锁只在进程内生效。多个实例同时登记的概率很低，后写者覆盖时最多丢失一次登记，用户可以再次点击清理。
/// </remarks>
public sealed class PendingCleanupStore(ILogger<PendingCleanupStore> logger)
{
    /// <summary>
    /// 串行化本进程内对记录文件的读写
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 登记路径，下次启动时删除其中的内容
    /// </summary>
    /// <exception cref="ArgumentException">路径不在程序目录内</exception>
    public void Add(IEnumerable<string> paths)
    {
        lock (_lock)
        {
            var pending = Load();
            foreach (var path in paths)
            {
                if (!SafePath.IsUnder(Global.StartUpPath, path))
                {
                    throw new ArgumentException($"只能清理程序目录内的路径：{path}", nameof(paths));
                }

                pending.Add(Path.GetRelativePath(Global.StartUpPath, Path.GetFullPath(path)));
            }

            Save(pending);
        }
    }

    /// <summary>
    /// 判断给定路径是否全部已登记、等待下次启动清理
    /// </summary>
    public bool ContainsAll(IEnumerable<string> paths)
    {
        lock (_lock)
        {
            var pending = Load();
            var relativePaths = paths
                .Where(path => SafePath.IsUnder(Global.StartUpPath, path))
                .Select(path => Path.GetRelativePath(Global.StartUpPath, Path.GetFullPath(path)))
                .ToList();
            return relativePaths.Count > 0 && relativePaths.All(pending.Contains);
        }
    }

    /// <summary>
    /// 执行全部登记的清理。只应由主实例在启动早期调用；
    /// 全部删除成功的条目会被移除，仍有文件被占用的条目保留到下次启动
    /// </summary>
    public void ExecutePending()
    {
        lock (_lock)
        {
            var pending = Load();
            if (pending.Count == 0)
            {
                return;
            }

            var remaining = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var relativePath in pending)
            {
                var fullPath = Path.GetFullPath(Path.Combine(Global.StartUpPath, relativePath));
                if (!SafePath.IsUnder(Global.StartUpPath, fullPath))
                {
                    // 记录被篡改或损坏，丢弃该条目
                    logger.LogWarning("忽略程序目录外的待清理路径：{Path}", relativePath);
                    continue;
                }

                var result = StorageFileOperations.Clear(fullPath);
                if (result.SkippedFiles > 0)
                {
                    remaining.Add(relativePath);
                }

                logger.LogInformation("已执行推迟的清理 {Path}：释放 {FreedBytes} 字节，跳过 {SkippedFiles} 个文件",
                    relativePath, result.FreedBytes, result.SkippedFiles);
            }

            Save(remaining);
        }
    }

    /// <summary>
    /// 读取登记记录；文件不存在或损坏时返回空集合
    /// </summary>
    private SortedSet<string> Load()
    {
        var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(AppPaths.PendingCleanupFile))
            {
                var paths = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(AppPaths.PendingCleanupFile));
                result.UnionWith(paths?.Where(path => !string.IsNullOrWhiteSpace(path)) ?? []);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(e, "读取待清理记录失败，已忽略");
        }

        return result;
    }

    /// <summary>
    /// 保存登记记录；没有条目时删除记录文件
    /// </summary>
    private static void Save(SortedSet<string> pending)
    {
        if (pending.Count == 0)
        {
            if (File.Exists(AppPaths.PendingCleanupFile))
            {
                File.Delete(AppPaths.PendingCleanupFile);
            }

            return;
        }

        AtomicFile.WriteAllText(AppPaths.PendingCleanupFile, JsonConvert.SerializeObject(pending.ToList(), Formatting.Indented));
    }
}
