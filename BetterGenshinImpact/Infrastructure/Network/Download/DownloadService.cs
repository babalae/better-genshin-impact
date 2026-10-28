using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.Infrastructure.Network.Download;

/// <summary>
/// 下载服务的实现。同时是下载中心的任务列表（<see cref="IDownloadMonitor"/>），
/// 并把每个下载分类登记为存储区域（见设计文档第 7 节）。传输细节见 DownloadService.Transfer.cs
/// </summary>
public sealed partial class DownloadService : IDownloadService, IDownloadMonitor, IStorageAreaProvider
{
    /// <summary>
    /// 同时下载的任务数上限，其余排队
    /// </summary>
    private const int MaxConcurrentDownloads = 3;

    /// <summary>
    /// 下载专用客户端：不限总时长、不自动解压缩
    /// </summary>
    private readonly HttpClient _client;

    /// <summary>
    /// 下载根目录，按分类分子目录
    /// </summary>
    private readonly string _root;

    /// <summary>
    /// 日志
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// 全局下载位置
    /// </summary>
    private readonly SemaphoreSlim _slots = new(MaxConcurrentDownloads, MaxConcurrentDownloads);

    /// <summary>
    /// 按文件路径的互斥锁：同一地址的多个任务依次执行，后来的任务复用或续传前一个任务的文件
    /// </summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 保护 <see cref="_tasks"/>
    /// </summary>
    private readonly object _tasksLock = new();

    /// <summary>
    /// 显示在下载中心的任务
    /// </summary>
    private readonly List<DownloadTask> _tasks = [];

    /// <param name="client">下载客户端，通常是 <see cref="HttpClientNames.Download"/></param>
    /// <param name="rootDirectory">下载根目录，通常是 <see cref="AppPaths.Downloads"/></param>
    /// <param name="logger">日志</param>
    public DownloadService(HttpClient client, string rootDirectory, ILogger<DownloadService>? logger = null)
    {
        _client = client;
        _root = Path.GetFullPath(rootDirectory);
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>
    /// 可重试错误的重试间隔，次数等于元素个数。测试中可改为零间隔
    /// </summary>
    internal IReadOnlyList<TimeSpan> RetryDelays { get; init; } =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlyList<DownloadTask> Tasks
    {
        get
        {
            lock (_tasksLock)
            {
                return _tasks.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public string GetDirectory(DownloadCategory category) => Path.Combine(_root, category.Id);

    /// <inheritdoc />
    public DownloadTask Start(DownloadRequest request, CancellationToken ct = default)
    {
        Validate(request);
        var task = new DownloadTask(request, ct);
        if (request.ShowInDownloadCenter)
        {
            lock (_tasksLock)
            {
                _tasks.Add(task);
            }
        }

        Launch(task);
        return task;
    }

    /// <inheritdoc />
    public DownloadTask Retry(Guid id)
    {
        DownloadTask task;
        lock (_tasksLock)
        {
            var index = _tasks.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                throw new InvalidOperationException("下载任务不存在。");
            }

            var previous = _tasks[index];
            if (!previous.Progress.IsFinished || previous.Request.Origin is not null)
            {
                throw new InvalidOperationException("该下载不能在下载中心重试。");
            }

            // 重试的任务不再跟随原调用方的取消令牌，只能在下载中心取消
            task = new DownloadTask(previous.Request, CancellationToken.None);
            _tasks[index] = task;
        }

        Launch(task);
        return task;
    }

    /// <inheritdoc />
    public void ClearFinished()
    {
        lock (_tasksLock)
        {
            _tasks.RemoveAll(task => task.Progress.IsFinished);
        }

        RaiseChanged();
    }

    /// <summary>
    /// 在后台执行任务并通知下载中心
    /// </summary>
    private void Launch(DownloadTask task)
    {
        _ = Task.Run(() => RunAsync(task));
        if (task.Request.ShowInDownloadCenter)
        {
            RaiseChanged();
        }
    }

    /// <summary>
    /// 通知下载中心刷新；订阅者的异常不能影响下载
    /// </summary>
    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "下载中心刷新失败");
        }
    }

    /// <summary>
    /// 执行一个任务：等待文件锁和下载位置，下载并校验，最后移动到目标位置
    /// </summary>
    private async Task RunAsync(DownloadTask task)
    {
        var request = task.Request;
        var ct = task.Token;
        var directory = GetDirectory(request.Category);
        var filePath = Path.Combine(directory, GetFileName(request.Url));
        var fileLock = _fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
        var fileLockTaken = false;
        var slotTaken = false;
        try
        {
            // 先取文件锁再取下载位置，避免等待同一文件的任务占着下载位置
            await fileLock.WaitAsync(ct).ConfigureAwait(false);
            fileLockTaken = true;
            await _slots.WaitAsync(ct).ConfigureAwait(false);
            slotTaken = true;

            Directory.CreateDirectory(directory);
            var downloaded = await DownloadToFileAsync(task, filePath, ct).ConfigureAwait(false);
            var result = MoveToDestination(downloaded, request.DestinationPath);
            var size = new FileInfo(result).Length;
            task.Complete(result, new DownloadProgress(DownloadState.Completed, size, size, 0));
            _logger.LogInformation("下载完成：{Name}（{Host}），{Megabytes:F1} MB", request.DisplayName, request.Url.Host, size / 1048576d);
        }
        catch (OperationCanceledException e) when (ct.IsCancellationRequested)
        {
            var last = task.Progress;
            task.Fail(e, new DownloadProgress(DownloadState.Canceled, last.ReceivedBytes, last.TotalBytes, 0));
            _logger.LogInformation("下载已取消：{Name}", request.DisplayName);
        }
        catch (Exception e)
        {
            var last = task.Progress;
            var reason = e.Message;
            task.Fail(WithDisplayName(e, request.DisplayName),
                new DownloadProgress(DownloadState.Failed, last.ReceivedBytes, last.TotalBytes, 0, reason));
            _logger.LogWarning(e, "下载失败：{Name}（{Host}）", request.DisplayName, request.Url.Host);
        }
        finally
        {
            if (slotTaken)
            {
                _slots.Release();
            }

            if (fileLockTaken)
            {
                fileLock.Release();
            }
        }

        if (request.ShowInDownloadCenter)
        {
            RaiseChanged();
        }
    }

    /// <summary>
    /// 更新任务进度；状态变化时通知下载中心
    /// </summary>
    private void Report(DownloadTask task, DownloadProgress progress)
    {
        var stateChanged = task.Progress.State != progress.State;
        task.Report(progress);
        if (stateChanged && task.Request.ShowInDownloadCenter)
        {
            RaiseChanged();
        }
    }

    /// <summary>
    /// 给调用方看到的异常加上下载名称，保留异常类型（运行库安装依据 InvalidDataException 删除损坏的包）
    /// </summary>
    private static Exception WithDisplayName(Exception e, string displayName)
    {
        var message = $"{displayName}：{e.Message}";
        return e switch
        {
            InvalidDataException => new InvalidDataException(message, e),
            HttpRequestException http => new HttpRequestException(message, e, http.StatusCode),
            UnauthorizedAccessException => new UnauthorizedAccessException(message, e),
            IOException => new IOException(message, e),
            _ => new InvalidOperationException(message, e),
        };
    }

    /// <summary>
    /// 下载文件名：地址的 SHA256，加上地址路径中的扩展名。不使用服务器返回的文件名
    /// </summary>
    internal static string GetFileName(Uri url)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri)));
        var extension = Path.GetExtension(url.AbsolutePath);
        var safeExtension = extension.Length is >= 2 and <= 9 && extension.Skip(1).All(char.IsAsciiLetterOrDigit);
        return safeExtension ? key + extension.ToLowerInvariant() : key;
    }

    /// <summary>
    /// 指定了目标路径时移动过去，否则留在下载目录。File.Move 在 Windows 上允许跨分区（复制后删除）
    /// </summary>
    private static string MoveToDestination(string path, string? destination)
    {
        if (destination is null)
        {
            return path;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(path, destination, true);
        return destination;
    }

    /// <summary>
    /// 校验请求：地址、分类目录名、目标路径和哈希格式
    /// </summary>
    private static void Validate(DownloadRequest request)
    {
        if (!request.Url.IsAbsoluteUri || request.Url.Scheme is not ("http" or "https")
            || request.Mirrors.Any(mirror => !mirror.IsAbsoluteUri || mirror.Scheme is not ("http" or "https")))
        {
            throw new ArgumentException("下载地址只支持 http 和 https。", nameof(request));
        }

        if (!SafePath.IsSafeRelativePath(request.Category.Id) || request.Category.Id.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException($"下载分类名无效：{request.Category.Id}", nameof(request));
        }

        if (request.DestinationPath is { } destination
            && (!Path.IsPathFullyQualified(destination) || !SafePath.IsUnder(Global.StartUpPath, destination)))
        {
            throw new ArgumentException($"下载的目标路径必须位于程序目录内：{destination}", nameof(request));
        }

        if (request.MaxBytes <= 0)
        {
            throw new ArgumentException("下载大小上限必须大于 0。", nameof(request));
        }

        if (request.ExpectedHash is { } hash)
        {
            _ = DecodeHash(hash);
        }
    }

    /// <summary>
    /// 解析期望哈希：长度符合十六进制时按十六进制解析，否则按 Base64 解析（NuGet 使用 Base64）
    /// </summary>
    /// <exception cref="ArgumentException">算法不支持或哈希值无法解析</exception>
    private static (HashAlgorithmName Algorithm, byte[] Value) DecodeHash(DownloadHash hash)
    {
        var (algorithm, size) = hash.Algorithm.ToUpperInvariant() switch
        {
            "SHA256" => (HashAlgorithmName.SHA256, 32),
            "SHA512" => (HashAlgorithmName.SHA512, 64),
            _ => throw new ArgumentException($"不支持的哈希算法：{hash.Algorithm}", nameof(hash)),
        };
        try
        {
            var value = hash.Value.Length == size * 2 && hash.Value.All(Uri.IsHexDigit)
                ? Convert.FromHexString(hash.Value)
                : Convert.FromBase64String(hash.Value);
            return value.Length == size ? (algorithm, value) : throw new FormatException();
        }
        catch (FormatException e)
        {
            throw new ArgumentException($"哈希值格式无效：{hash.Value}", nameof(hash), e);
        }
    }

    /// <inheritdoc />
    public IEnumerable<StorageArea> GetAreas()
    {
        var areas = new List<StorageArea>();
        foreach (var category in DownloadCategories.All)
        {
            var directory = GetDirectory(category);
            var partialCount = CountPartialFiles(directory);
            areas.Add(new StorageArea($"download:{category.Id}", category.DisplayName, StorageGroup.Download, [directory])
            {
                Description = partialCount > 0
                    ? $"含 {partialCount} 个未完成的下载，可继续。{category.Description}"
                    : category.Description,
                CleanupEffect = partialCount > 0
                    ? "未完成的下载需要从头重新下载，正在下载的文件会跳过"
                    : "需要时重新下载",
            });
        }

        // 代码里已不存在的分类目录（功能下线后的遗留），只在存在时显示
        var known = DownloadCategories.All.Select(category => category.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unused = ListEntries(_root).Where(path => !known.Contains(Path.GetFileName(path))).ToList();
        if (unused.Count > 0)
        {
            areas.Add(new StorageArea("download:unused", "已不再使用的下载", StorageGroup.Download, unused)
            {
                Description = "旧功能留下的下载文件",
                CleanupEffect = "不影响现有功能",
            });
        }

        return areas;
    }

    /// <summary>
    /// 统计目录中未完成的下载数；目录不存在或无法访问时为 0
    /// </summary>
    private static int CountPartialFiles(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.part").Count() : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// 列出目录下的文件和子目录；目录不存在或无法访问时为空
    /// </summary>
    private static IEnumerable<string> ListEntries(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.GetFileSystemEntries(directory) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
