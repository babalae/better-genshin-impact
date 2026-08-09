using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Music.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Music.Service;

public sealed class MusicLibraryService(
    IMusicScoreParser scoreParser,
    IMusicStateStore stateStore) : IMusicLibraryService
{
    private readonly ILogger<MusicLibraryService> _logger = App.GetLogger<MusicLibraryService>();
    private readonly object _watcherSyncRoot = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;

    private static readonly EnumerationOptions ScoreEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        ReturnSpecialDirectories = false,
        // 曲谱目录中可能包含目录联接或符号链接。跳过它们可以避免递归回到父目录，
        // 造成刷新时无限扫描并最终耗尽内存。
        AttributesToSkip = FileAttributes.ReparsePoint
    };

    private static readonly string[] InternalDataFiles =
    [
        Path.GetFullPath(Global.Absolute(@"User\Music\music-state.json")),
        Path.GetFullPath(Global.Absolute(@"User\Music\instrument-profiles.json"))
    ];

    public event EventHandler? FilesChanged;

    public event EventHandler<MusicScoreParseFailedEventArgs>? ScoreParseFailed;

    public async Task<IReadOnlyList<PerformanceScore>> ScanAsync(
        string rootFolder,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
        {
            return [];
        }

        // 刷新期间用户仍可能调整轨道或播放顺序。先在调用线程制作状态快照，
        // 避免后台扫描线程与 UI 线程同时读写 Dictionary/List。
        var preferenceSnapshot = stateStore.State.Items
            .Where(x => x.Value != null)
            .ToDictionary(
            x => x.Key,
            x => new MusicItemPreference
            {
                OutputProfileName = x.Value.OutputProfileName,
                Transpose = x.Value.Transpose,
                DisabledTrackIndexes = [.. x.Value.DisabledTrackIndexes ?? []]
            },
            StringComparer.OrdinalIgnoreCase);
        // 目录枚举和曲谱解析不能占用 WPF UI 线程。串行解析也能避免目录较大时
        // 一次创建大量解析任务和中间对象，页面进入时只需等待最终结果回到 UI。
        return await Task.Run<IReadOnlyList<PerformanceScore>>(async () =>
        {
            var scores = new List<PerformanceScore>();
            foreach (var path in Directory.EnumerateFiles(rootFolder, "*", ScoreEnumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsInternalDataFile(path) || !scoreParser.CanParse(path))
                {
                    continue;
                }

                try
                {
                    scores.Add(await scoreParser
                        .ParseAsync(path, rootFolder, cancellationToken)
                        .ConfigureAwait(false));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "解析曲谱文件失败，已跳过：{Path}", path);
                    try
                    {
                        ScoreParseFailed?.Invoke(
                            this,
                            new MusicScoreParseFailedEventArgs(path, e.Message));
                    }
                    catch (Exception notificationException)
                    {
                        _logger.LogDebug(notificationException, "通知曲谱解析失败事件时发生异常：{Path}", path);
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            foreach (var score in scores)
            {
                if (!preferenceSnapshot.TryGetValue(score.RelativePath, out var preference))
                {
                    continue;
                }

                score.OutputProfileName = preference.OutputProfileName;
                score.Transpose = preference.Transpose;
                foreach (var track in score.Tracks)
                {
                    track.IsEnabled = !preference.DisabledTrackIndexes.Contains(track.Index);
                }
            }

            return scores
                .OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    public void Watch(string rootFolder)
    {
        lock (_watcherSyncRoot)
        {
            DisposeWatcher();
            if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
            {
                return;
            }

            try
            {
                _watcher = new FileSystemWatcher(rootFolder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                                   | NotifyFilters.LastWrite | NotifyFilters.Size
                };
                _watcher.Changed += OnFileChanged;
                _watcher.Created += OnFileChanged;
                _watcher.Deleted += OnFileChanged;
                _watcher.Renamed += OnFileChanged;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                DisposeWatcher();
                _logger.LogWarning(e, "监听曲谱目录失败：{RootFolder}", rootFolder);
            }
        }
    }

    public void Dispose()
    {
        lock (_watcherSyncRoot)
        {
            DisposeWatcher();
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (IsInternalDataFile(e.FullPath)
            || Path.HasExtension(e.FullPath) && !scoreParser.CanParse(e.FullPath))
        {
            return;
        }

        lock (_watcherSyncRoot)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(
                _ =>
                {
                    try
                    {
                        FilesChanged?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception exception)
                    {
                        _logger.LogDebug(exception, "通知曲谱目录变化失败");
                    }
                },
                null,
                TimeSpan.FromMilliseconds(500),
                Timeout.InfiniteTimeSpan);
        }
    }

    private void DisposeWatcher()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        if (_watcher == null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnFileChanged;
        _watcher.Created -= OnFileChanged;
        _watcher.Deleted -= OnFileChanged;
        _watcher.Renamed -= OnFileChanged;
        _watcher.Dispose();
        _watcher = null;
    }

    private static bool IsInternalDataFile(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            return InternalDataFiles.Contains(fullPath, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
