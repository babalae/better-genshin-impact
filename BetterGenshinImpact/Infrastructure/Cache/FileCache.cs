using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers.Security;
using BetterGenshinImpact.Infrastructure.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.Infrastructure.Cache;

/// <summary>
/// 文件缓存的实现：磁盘为 {根目录}/{分区}/{MD5(key)}.bin，按最后写入时间判断过期；
/// 小条目同时放在有总量上限的内存层。多个实例共享磁盘目录，读到被占用或损坏的文件按未命中处理。
/// 同时把每个缓存分区登记为存储区域
/// </summary>
public sealed class FileCache : IFileCache, IStorageAreaProvider, IDisposable
{
    /// <summary>
    /// 只把小于该大小的条目放进内存层，大图只走磁盘，避免常驻内存
    /// </summary>
    internal const int MemoryEntryLimitBytes = 256 * 1024;

    /// <summary>
    /// 内存层总量上限
    /// </summary>
    private const long MemorySizeLimitBytes = 64L * 1024 * 1024;

    /// <summary>
    /// 内存层条目的滑动过期时间
    /// </summary>
    private static readonly TimeSpan MemorySlidingExpiration = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 缓存根目录
    /// </summary>
    private readonly string _root;

    /// <summary>
    /// 时间来源，测试中可替换
    /// </summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// 日志
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// 内存层
    /// </summary>
    private readonly MemoryCache _memory = new(new MemoryCacheOptions { SizeLimit = MemorySizeLimitBytes });

    /// <summary>
    /// 进行中的读取，同一条目只调用一次 factory
    /// </summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> _inflight = new(StringComparer.Ordinal);

    /// <summary>
    /// 传给 factory 的令牌，服务释放时取消；单个调用方取消不会中断其他调用方共享的下载
    /// </summary>
    private readonly CancellationTokenSource _lifetime = new();

    /// <param name="rootDirectory">缓存根目录，通常是 <see cref="AppPaths.FileCache"/></param>
    /// <param name="timeProvider">时间来源</param>
    /// <param name="logger">日志</param>
    public FileCache(string rootDirectory, TimeProvider? timeProvider = null, ILogger<FileCache>? logger = null)
    {
        _root = Path.GetFullPath(rootDirectory);
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>
    /// 缓存根目录
    /// </summary>
    public string RootDirectory => _root;

    /// <summary>
    /// 分区的磁盘目录
    /// </summary>
    public string GetPartitionDirectory(CachePartition partition) => Path.Combine(_root, partition.Id);

    /// <summary>
    /// 条目的磁盘文件。MD5 只用来生成文件名，不用于安全用途
    /// </summary>
    internal string GetFilePath(CachePartition partition, string key) =>
        Path.Combine(GetPartitionDirectory(partition), MD5Helper.ComputeMD5(key) + ".bin");

    /// <inheritdoc />
    public Task<byte[]?> GetOrAddBytesAsync(CachePartition partition, string key,
        Func<CancellationToken, Task<byte[]?>> factory, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var memoryKey = GetMemoryKey(partition, key);
        if (_memory.TryGetValue(memoryKey, out MemoryEntry? cached) && cached is not null)
        {
            if (cached.ExpiresAt > _time.GetUtcNow())
            {
                return Task.FromResult<byte[]?>(cached.Bytes);
            }

            _memory.Remove(memoryKey);
        }

        // 在线程池执行，避免界面线程上的调用方同步读取磁盘
        var load = _inflight.GetOrAdd(memoryKey, _ => new Lazy<Task<byte[]?>>(
            () => Task.Run(() => LoadAsync(partition, key, memoryKey, factory))));
        return load.Value.WaitAsync(ct);
    }

    /// <inheritdoc />
    public void Remove(CachePartition partition, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _memory.Remove(GetMemoryKey(partition, key));
        TryDelete(GetFilePath(partition, key));
    }

    /// <inheritdoc />
    public void RetainOnly(CachePartition partition, IReadOnlyCollection<string> keys)
    {
        var directory = GetPartitionDirectory(partition);
        if (!Directory.Exists(directory))
        {
            return;
        }

        var keep = keys.Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => MD5Helper.ComputeMD5(key) + ".bin")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.bin"))
            {
                if (!keep.Contains(Path.GetFileName(file)))
                {
                    TryDelete(file);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(e, "清理缓存分区 {Partition} 失败", partition.Id);
        }

        // 内存层中被删除的条目最多保留到滑动过期，这里不逐个查找
    }

    /// <summary>
    /// 清空内存层。分区在存储空间窗口中被清理后调用，之后的读取重新走磁盘或 factory
    /// </summary>
    public void ClearMemory() => _memory.Clear();

    /// <summary>
    /// 读取磁盘，未命中时调用 factory 并写入；内容足够小时放进内存层
    /// </summary>
    private async Task<byte[]?> LoadAsync(CachePartition partition, string key, string memoryKey,
        Func<CancellationToken, Task<byte[]?>> factory)
    {
        try
        {
            var path = GetFilePath(partition, key);
            var bytes = TryRead(path, partition.Ttl, out var expiresAt);
            if (bytes is null)
            {
                bytes = await factory(_lifetime.Token).ConfigureAwait(false);
                if (bytes is not { Length: > 0 })
                {
                    return null;
                }

                TryWrite(path, bytes);
                expiresAt = _time.GetUtcNow() + partition.Ttl;
            }

            Remember(memoryKey, bytes, expiresAt);
            return bytes;
        }
        finally
        {
            _inflight.TryRemove(memoryKey, out _);
        }
    }

    /// <summary>
    /// 读取未过期的磁盘条目；过期或损坏的文件删除，被占用时按未命中处理
    /// </summary>
    /// <param name="path">条目文件</param>
    /// <param name="ttl">有效期</param>
    /// <param name="expiresAt">条目到期时间</param>
    private byte[]? TryRead(string path, TimeSpan ttl, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            expiresAt = lastWrite + ttl;
            if (expiresAt <= _time.GetUtcNow())
            {
                TryDelete(path);
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > 0)
            {
                return bytes;
            }

            TryDelete(path);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(e, "读取缓存文件失败：{Path}", path);
            return null;
        }
    }

    /// <summary>
    /// 原子写入磁盘条目，最后写入时间取自 <see cref="_time"/>；失败只记录日志，内容仍返回给调用方
    /// </summary>
    private void TryWrite(string path, byte[] bytes)
    {
        try
        {
            AtomicFile.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, _time.GetUtcNow().UtcDateTime);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(e, "写入缓存文件失败：{Path}", path);
        }
    }

    /// <summary>
    /// 小条目放进内存层，与磁盘条目同时到期
    /// </summary>
    private void Remember(string memoryKey, byte[] bytes, DateTimeOffset expiresAt)
    {
        var remaining = expiresAt - _time.GetUtcNow();
        if (bytes.Length >= MemoryEntryLimitBytes || remaining <= TimeSpan.Zero)
        {
            return;
        }

        // 内存层使用系统时钟，到期时间另外保存，读取时按 _time 判断，两层的有效期一致
        _memory.Set(memoryKey, new MemoryEntry(bytes, expiresAt), new MemoryCacheEntryOptions
        {
            Size = bytes.Length,
            SlidingExpiration = MemorySlidingExpiration,
            AbsoluteExpirationRelativeToNow = remaining,
        });
    }

    /// <summary>
    /// 内存层条目
    /// </summary>
    /// <param name="Bytes">内容</param>
    /// <param name="ExpiresAt">与磁盘条目相同的到期时间</param>
    private sealed record MemoryEntry(byte[] Bytes, DateTimeOffset ExpiresAt);

    /// <summary>
    /// 内存层的键
    /// </summary>
    private static string GetMemoryKey(CachePartition partition, string key) => partition.Id + "\n" + key;

    /// <summary>
    /// 删除文件，失败时忽略（可能正被其他实例读取）
    /// </summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <inheritdoc />
    public IEnumerable<StorageArea> GetAreas()
    {
        var areas = CachePartitions.All.Select(partition => new StorageArea(
            $"cache:{partition.Id}", partition.DisplayName, StorageGroup.Cache, [GetPartitionDirectory(partition)])
        {
            Description = partition.Description,
            CleanupEffect = partition.CleanupEffect,
            CleanupAsync = ct => CleanupAsync(GetPartitionDirectory(partition), ct),
        }).ToList();

        // 代码里已不存在的分区目录（功能下线后的遗留），只在存在时显示；缓存维护也会定期删除
        var unused = ListUnusedEntries();
        if (unused.Count > 0)
        {
            areas.Add(new StorageArea("cache:unused", "已不再使用的缓存", StorageGroup.Cache, unused)
            {
                Description = "旧功能留下的缓存文件",
                CleanupEffect = "不影响现有功能",
            });
        }

        return areas;
    }

    /// <summary>
    /// 清理一个分区：先清空内存层，再删除磁盘文件，正在使用的文件跳过
    /// </summary>
    private Task<StorageCleanupResult> CleanupAsync(string directory, CancellationToken ct) => Task.Run(() =>
    {
        ClearMemory();
        return StorageFileOperations.Clear(directory, ct);
    }, ct);

    /// <summary>
    /// 根目录下不属于任何分区的文件和目录，不含维护记录文件
    /// </summary>
    internal IReadOnlyList<string> ListUnusedEntries()
    {
        var known = CachePartitions.All.Select(partition => partition.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        known.Add(CacheMaintenanceService.StateFileName);
        try
        {
            return Directory.Exists(_root)
                ? Directory.GetFileSystemEntries(_root).Where(path => !known.Contains(Path.GetFileName(path))).ToList()
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _memory.Dispose();
    }
}
