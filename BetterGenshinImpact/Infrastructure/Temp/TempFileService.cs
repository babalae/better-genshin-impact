using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Infrastructure.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.Infrastructure.Temp;

/// <summary>
/// 临时文件服务。每个进程在 Data/Temp 下拥有一个目录（实例类型-进程号），目录内按分类划分；
/// 进程存活期间独占打开目录中的租约文件，其他进程据此判断目录是否仍在使用。
/// 进程退出时删除自己的目录；进程异常结束留下的目录由之后启动的任意实例回收。
/// </summary>
public sealed class TempFileService : ITempFileService, IStorageAreaProvider, IHostedService, IDisposable
{
    /// <summary>
    /// 租约文件名
    /// </summary>
    private const string LeaseFileName = ".lease";

    /// <summary>
    /// 没有租约文件的目录在创建多久之后才视为遗留，避免误删其他进程刚创建、尚未加锁的目录
    /// </summary>
    private static readonly TimeSpan LeaselessGracePeriod = TimeSpan.FromMinutes(1);

    /// <summary>
    /// 本进程的目录名（实例类型-进程号）
    /// </summary>
    private readonly string _processDirectoryName;

    /// <summary>
    /// 日志
    /// </summary>
    private readonly ILogger<TempFileService> _logger;

    /// <summary>
    /// 保护进程目录的初始化与释放
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 本进程正在使用的作用域目录，清理分类时跳过
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _activeScopes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 停止服务时取消遗留目录回收
    /// </summary>
    private readonly CancellationTokenSource _recycleCts = new();

    /// <summary>
    /// 后台回收遗留目录的任务
    /// </summary>
    private Task _recycleTask = Task.CompletedTask;

    /// <summary>
    /// 本进程持有的租约，进程结束时由系统关闭并删除
    /// </summary>
    private FileStream? _lease;

    /// <summary>
    /// 进程目录是否已创建
    /// </summary>
    private bool _initialized;

    /// <summary>
    /// 是否已释放（程序正在退出）
    /// </summary>
    private bool _released;

    /// <param name="processDirectoryName">本进程的目录名，例如 primary-12345</param>
    /// <param name="logger">日志</param>
    public TempFileService(string processDirectoryName, ILogger<TempFileService> logger)
    {
        if (!SafePath.IsSafeRelativePath(processDirectoryName) || processDirectoryName.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException($"进程临时目录名不合法：{processDirectoryName}", nameof(processDirectoryName));
        }

        _processDirectoryName = processDirectoryName;
        _logger = logger;
    }

    /// <summary>
    /// 本进程的临时目录
    /// </summary>
    private string ProcessDirectory => Path.Combine(AppPaths.Temp, _processDirectoryName);

    /// <inheritdoc />
    public TempScope CreateScope(TempCategory category)
    {
        var directory = Path.Combine(EnsureCategoryDirectory(category), $"{DateTime.Now:yyyyMMdd-HHmmss}-{ShortId.Create(null, 8)}");
        Directory.CreateDirectory(directory);
        _activeScopes[directory] = 0;
        return new TempScope(directory, ReleaseScope);
    }

    /// <inheritdoc />
    public string CreateFilePath(TempCategory category, string extension)
    {
        if (extension.Length > 0 && !extension.StartsWith('.'))
        {
            extension = "." + extension;
        }

        if (extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"扩展名不合法：{extension}", nameof(extension));
        }

        return Path.Combine(EnsureCategoryDirectory(category), ShortId.Create(null) + extension);
    }

    /// <inheritdoc />
    public Task<StorageCleanupResult> CleanupAsync(TempCategory category, CancellationToken ct) =>
        Task.Run(() => CleanupCategory(category, ct), ct);

    /// <inheritdoc />
    public IEnumerable<StorageArea> GetAreas()
    {
        var processDirectories = ListProcessDirectories();
        return TempCategories.All.Select(category => new StorageArea(
            $"temp:{category.Id}",
            category.DisplayName,
            StorageGroup.Temp,
            processDirectories.Select(directory => Path.Combine(directory, category.Id)).Where(Directory.Exists).ToList())
        {
            Description = "程序运行中产生的临时文件，退出时自动删除",
            CleanupEffect = "正在使用的文件会跳过",
            CleanupAsync = ct => CleanupAsync(category, ct),
        }).ToList();
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 先让本进程目录与租约就绪，再在后台回收其他已结束进程留下的目录
        EnsureProcessDirectory();
        _recycleTask = Task.Run(() => RecycleAbandonedDirectories(_recycleCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _recycleCts.CancelAsync();
        try
        {
            await _recycleTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }

        Release();
    }

    /// <summary>
    /// 宿主未正常停止时，容器释放阶段兜底删除本进程目录
    /// </summary>
    public void Dispose()
    {
        _recycleCts.Cancel();
        Release();
    }

    /// <summary>
    /// 确保本进程目录存在并持有租约；返回目录路径
    /// </summary>
    private string EnsureProcessDirectory()
    {
        lock (_lock)
        {
            var directory = ProcessDirectory;
            if (!_initialized && !_released)
            {
                // 同名目录只可能来自进程号相同、已经结束的进程，先整体删除再重建
                StorageFileOperations.DeletePath(directory);
                Directory.CreateDirectory(directory);
                try
                {
                    // 进程无论正常退出还是被结束，系统都会关闭句柄并删除租约文件
                    _lease = new FileStream(Path.Combine(directory, LeaseFileName), FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // 拿不到租约时目录仍可使用，只是一分钟后可能被其他实例当作遗留目录回收
                    _logger.LogWarning(e, "无法创建临时目录租约：{Directory}", directory);
                }

                _initialized = true;
            }

            // 退出后仍有调用时只创建目录、不再加锁，下次启动会回收
            return directory;
        }
    }

    /// <summary>
    /// 确保分类目录存在；返回目录路径
    /// </summary>
    private string EnsureCategoryDirectory(TempCategory category)
    {
        var directory = Path.Combine(EnsureProcessDirectory(), category.Id);
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// 作用域释放时删除目录并解除登记
    /// </summary>
    private void ReleaseScope(TempScope scope)
    {
        var result = StorageFileOperations.DeletePath(scope.DirectoryPath);
        _activeScopes.TryRemove(scope.DirectoryPath, out _);
        if (result.SkippedFiles > 0)
        {
            _logger.LogWarning("临时目录 {Directory} 有 {SkippedFiles} 个文件无法删除，将在退出或下次启动时回收",
                scope.DirectoryPath, result.SkippedFiles);
        }
    }

    /// <summary>
    /// 释放租约并删除本进程目录，只执行一次
    /// </summary>
    private void Release()
    {
        lock (_lock)
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _lease?.Dispose();
            _lease = null;
            if (!_initialized)
            {
                return;
            }

            var result = StorageFileOperations.DeletePath(ProcessDirectory);
            if (result.SkippedFiles > 0)
            {
                _logger.LogDebug("退出时有 {SkippedFiles} 个临时文件无法删除，下次启动回收", result.SkippedFiles);
            }
        }
    }

    /// <summary>
    /// 清理一个分类：本进程跳过正在使用的作用域；已结束进程的直接删除；其他运行中实例的计为跳过
    /// </summary>
    private StorageCleanupResult CleanupCategory(TempCategory category, CancellationToken ct)
    {
        var result = StorageCleanupResult.Empty;
        foreach (var processDirectory in ListProcessDirectories())
        {
            ct.ThrowIfCancellationRequested();
            var categoryDirectory = Path.Combine(processDirectory, category.Id);
            if (!Directory.Exists(categoryDirectory))
            {
                continue;
            }

            if (IsOwnDirectory(processDirectory))
            {
                foreach (var entry in ListEntries(categoryDirectory))
                {
                    result = result.Add(_activeScopes.ContainsKey(entry)
                        ? new StorageCleanupResult(0, 1, false)
                        : StorageFileOperations.DeletePath(entry, ct));
                }
            }
            else if (IsAbandoned(processDirectory))
            {
                result = result.Add(StorageFileOperations.DeletePath(categoryDirectory, ct));
            }
            else
            {
                // 其他运行中实例的文件可能正在使用
                result = result.Add(new StorageCleanupResult(0, ListEntries(categoryDirectory).Length, false));
            }
        }

        return result;
    }

    /// <summary>
    /// 回收已结束进程留下的目录
    /// </summary>
    private void RecycleAbandonedDirectories(CancellationToken ct)
    {
        try
        {
            foreach (var directory in ListProcessDirectories())
            {
                ct.ThrowIfCancellationRequested();
                if (IsOwnDirectory(directory) || !IsAbandoned(directory))
                {
                    continue;
                }

                var result = StorageFileOperations.DeletePath(directory, ct);
                _logger.LogDebug("已回收遗留的临时目录 {Directory}：释放 {FreedBytes} 字节，跳过 {SkippedFiles} 个文件",
                    Path.GetFileName(directory), result.FreedBytes, result.SkippedFiles);
            }
        }
        catch (OperationCanceledException)
        {
            // 程序退出时中止，下次启动继续
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "回收遗留的临时目录失败");
        }
    }

    /// <summary>
    /// 判断其他进程的目录是否已无人使用：租约文件能被独占打开，或没有租约且已超过宽限期
    /// </summary>
    private static bool IsAbandoned(string processDirectory)
    {
        var leasePath = Path.Combine(processDirectory, LeaseFileName);
        try
        {
            if (!File.Exists(leasePath))
            {
                return DateTime.UtcNow - Directory.GetCreationTimeUtc(processDirectory) > LeaselessGracePeriod;
            }

            using var probe = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 打不开说明所属进程仍持有租约
            return false;
        }
    }

    /// <summary>
    /// 是否为本进程的目录
    /// </summary>
    private bool IsOwnDirectory(string processDirectory) =>
        string.Equals(Path.GetFileName(processDirectory), _processDirectoryName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 列出 Data/Temp 下的各进程目录；无法访问时返回空
    /// </summary>
    private static string[] ListProcessDirectories()
    {
        try
        {
            return Directory.Exists(AppPaths.Temp) ? Directory.GetDirectories(AppPaths.Temp) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// 列出目录下的文件和子目录；无法访问时返回空
    /// </summary>
    private static string[] ListEntries(string directory)
    {
        try
        {
            return Directory.GetFileSystemEntries(directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
