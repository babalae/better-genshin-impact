using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Infrastructure.Storage;

/// <summary>
/// 旧目录清理：删除重构前遗留、已不再使用的目录，不做迁移（见设计文档 3.4 节）。
/// 只由主实例在启动后于后台执行；每个版本只执行一次，全部成功才写入版本标记，失败时下次启动重试。
/// </summary>
/// <param name="isRoot">当前进程是否为主实例</param>
/// <param name="logger">日志</param>
public sealed class LegacyDataCleaner(bool isRoot, ILogger<LegacyDataCleaner> logger) : IHostedService
{
    /// <summary>
    /// 已不再使用的旧目录（相对程序目录）。
    /// 条目必须在使用它的代码切换到新路径之后才能加入，否则旧代码会在下次启动时重新创建它；
    /// 版本号变化后清理会重新触发，新加入的条目会在新版本首次启动时执行
    /// </summary>
    private static readonly string[] LegacyPaths =
    [
        // 阶段 1：临时文件改到 Data/Temp
        @"User\Temp",
        @"Repos\Temp",
        // 阶段 2：推理编译缓存改到 Data/Cache/Inference，按版本号的空目录 Cache/{版本号}/Model 不再创建
        "Cache",
        // 阶段 4：运行库安装包改由下载服务保存到 Data/Downloads/onnxruntime
        @"Plugins\OnnxRuntime\.downloads",
        // 阶段 5：通用缓存和音乐封面改到 Data/Cache/Files
        @"User\Cache\MemoryFileCache",
        @"User\Music\Artwork",
        // 阶段 6：网页数据改到 Data/WebView2（软件内网页为 App，云原神实例为 CloudGame）
        "WebView2Data",
    ];

    /// <summary>
    /// 清理上面的目录后，若已为空则一并删除的父目录（相对程序目录）；不为空说明还有其他用途，保留
    /// </summary>
    private static readonly string[] LegacyParentsRemovedWhenEmpty =
    [
        @"User\Cache",
    ];

    /// <summary>
    /// 停止服务时取消清理
    /// </summary>
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// 后台清理任务
    /// </summary>
    private Task _task = Task.CompletedTask;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (isRoot)
        {
            // 新旧路径互不重叠，后台删除不影响启动和新目录的使用
            _task = Task.Run(() => Run(_cts.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        try
        {
            await _task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// 依次删除旧目录，全部成功后写入当前版本号
    /// </summary>
    private void Run(CancellationToken ct)
    {
        try
        {
            if (ReadCompletedVersion() == Global.Version)
            {
                return;
            }

            var allSucceeded = true;
            foreach (var relativePath in LegacyPaths)
            {
                ct.ThrowIfCancellationRequested();
                var fullPath = Path.GetFullPath(Path.Combine(Global.StartUpPath, relativePath));
                if (!SafePath.IsUnder(Global.StartUpPath, fullPath) || (!Directory.Exists(fullPath) && !File.Exists(fullPath)))
                {
                    continue;
                }

                var result = StorageFileOperations.DeletePath(fullPath, ct);
                if (result.SkippedFiles > 0)
                {
                    allSucceeded = false;
                    logger.LogWarning("旧目录 {Path} 有 {SkippedFiles} 个文件无法删除，下次启动重试", relativePath, result.SkippedFiles);
                }
                else
                {
                    logger.LogInformation("已删除旧目录 {Path}，释放 {FreedMegabytes:F1} MB", relativePath, result.FreedBytes / 1024d / 1024d);
                }
            }

            foreach (var relativePath in LegacyParentsRemovedWhenEmpty)
            {
                var fullPath = Path.GetFullPath(Path.Combine(Global.StartUpPath, relativePath));
                try
                {
                    if (SafePath.IsUnder(Global.StartUpPath, fullPath) && Directory.Exists(fullPath)
                                                                     && !Directory.EnumerateFileSystemEntries(fullPath).Any())
                    {
                        Directory.Delete(fullPath);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // 空目录删除失败不影响使用，也不阻止写入完成标记
                    logger.LogDebug(e, "删除空的旧目录 {Path} 失败", relativePath);
                }
            }

            if (allSucceeded)
            {
                AtomicFile.WriteAllText(AppPaths.LegacyCleanupStateFile,
                    JsonConvert.SerializeObject(new LegacyCleanupState { Version = Global.Version }, Formatting.Indented));
            }
        }
        catch (OperationCanceledException)
        {
            // 程序退出时中止，下次启动继续
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "清理旧目录失败，下次启动重试");
        }
    }

    /// <summary>
    /// 读取最近一次全部清理成功的版本号；没有记录或记录损坏时返回 null
    /// </summary>
    private string? ReadCompletedVersion()
    {
        try
        {
            if (!File.Exists(AppPaths.LegacyCleanupStateFile))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<LegacyCleanupState>(File.ReadAllText(AppPaths.LegacyCleanupStateFile))?.Version;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogDebug(e, "读取旧目录清理标记失败，重新执行清理");
            return null;
        }
    }

    /// <summary>
    /// 旧目录清理标记的文件内容
    /// </summary>
    private sealed class LegacyCleanupState
    {
        /// <summary>
        /// 最近一次全部清理成功的版本号
        /// </summary>
        public string? Version { get; set; }
    }
}
