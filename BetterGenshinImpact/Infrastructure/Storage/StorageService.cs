using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Infrastructure.Storage;

/// <summary>
/// 汇总各功能登记的存储区域，执行统计和清理
/// </summary>
public sealed class StorageService(
    IEnumerable<IStorageAreaProvider> providers,
    PendingCleanupStore pendingCleanupStore,
    ILogger<StorageService> logger) : IStorageService
{
    /// <inheritdoc />
    public IReadOnlyList<StorageArea> GetAreas()
    {
        var areas = new List<StorageArea>();
        foreach (var provider in providers)
        {
            try
            {
                areas.AddRange(provider.GetAreas());
            }
            catch (Exception e)
            {
                // 单个功能出错不影响其他区域的展示
                logger.LogWarning(e, "获取存储区域失败：{Provider}", provider.GetType().Name);
            }
        }

        return areas;
    }

    /// <inheritdoc />
    public Task<long> MeasureAsync(StorageArea area, CancellationToken ct) =>
        Task.Run(() => area.Paths.Sum(path => StorageFileOperations.Measure(path, ct)), ct);

    /// <inheritdoc />
    public bool IsCleanupPending(StorageArea area) =>
        area.Timing == StorageCleanupTiming.NextStartup && pendingCleanupStore.ContainsAll(area.Paths);

    /// <inheritdoc />
    public async Task<StorageCleanupResult> CleanupAsync(StorageArea area, CancellationToken ct)
    {
        if (area.GetBusyReason?.Invoke() is { } busyReason)
        {
            throw new InvalidOperationException(busyReason);
        }

        // 登记错误时也不能删到程序目录之外
        var outside = area.Paths.FirstOrDefault(path => !SafePath.IsUnder(Global.StartUpPath, path));
        if (outside != null)
        {
            throw new InvalidOperationException($"存储区域 {area.Id} 包含程序目录外的路径：{outside}");
        }

        StorageCleanupResult result;
        switch (area.Timing)
        {
            case StorageCleanupTiming.None:
                return StorageCleanupResult.Empty;

            case StorageCleanupTiming.NextStartup:
                pendingCleanupStore.Add(area.Paths);
                result = new StorageCleanupResult(0, 0, true);
                break;

            default:
                result = area.CleanupAsync != null
                    ? await area.CleanupAsync(ct)
                    : await Task.Run(() => area.Paths.Aggregate(
                        StorageCleanupResult.Empty,
                        (total, path) => total.Add(StorageFileOperations.Clear(path, ct))), ct);
                break;
        }

        logger.LogInformation("清理存储区域 {Area}：释放 {FreedBytes} 字节，跳过 {SkippedFiles} 个文件，推迟到下次启动 {Deferred}",
            area.Id, result.FreedBytes, result.SkippedFiles, result.Deferred);
        return result;
    }
}
