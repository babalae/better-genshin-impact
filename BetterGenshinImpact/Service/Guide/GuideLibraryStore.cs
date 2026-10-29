using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BetterGenshinImpact.Service.Guide;

/// <summary>
/// 悬浮小窗的收藏、观看记录与上次页面。
/// 保存在 User/GuideWindow 下：收藏只在增删改时写入，观看记录节流写入（见设计文档 10.2 节）。
/// 所有成员都可以在任意线程调用
/// </summary>
public sealed class GuideLibraryStore
{
    /// <summary>
    /// 观看记录上限，超出时淘汰最久未更新的
    /// </summary>
    private const int MaxHistory = 200;

    /// <summary>
    /// 少于这个秒数的观看位置不记录
    /// </summary>
    private const double MinRecordPosition = 10;

    /// <summary>
    /// 距结尾少于这个秒数视为看完
    /// </summary>
    private const double FinishedTailSeconds = 15;

    /// <summary>
    /// 日志
    /// </summary>
    private readonly ILogger<GuideLibraryStore> _logger;

    /// <summary>
    /// 保护全部数据
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 串行化文件写入
    /// </summary>
    private readonly object _saveLock = new();

    /// <summary>
    /// 收藏文件路径
    /// </summary>
    private readonly string _favoritesPath;

    /// <summary>
    /// 观看记录文件路径
    /// </summary>
    private readonly string _historyPath;

    /// <summary>
    /// 收藏，最近添加的在前
    /// </summary>
    private readonly List<GuideFavorite> _favorites;

    /// <summary>
    /// 观看记录，最近更新的在前
    /// </summary>
    private readonly List<GuideWatchRecord> _history;

    /// <summary>
    /// 上次打开的页面
    /// </summary>
    private string? _lastUrl;

    /// <summary>
    /// 观看记录有未写入的修改
    /// </summary>
    private bool _historyDirty;

    /// <summary>
    /// 收藏或观看记录发生变化（可能在任意线程触发），界面据此刷新列表
    /// </summary>
    public event EventHandler? Changed;

    /// <param name="logger">日志</param>
    public GuideLibraryStore(ILogger<GuideLibraryStore> logger)
    {
        _logger = logger;
        var directory = Global.Absolute(Path.Combine("User", "GuideWindow"));
        _favoritesPath = Path.Combine(directory, "favorites.json");
        _historyPath = Path.Combine(directory, "history.json");
        _favorites = Load<GuideFavoritesFile>(_favoritesPath)?.Items ?? [];
        var history = Load<GuideHistoryFile>(_historyPath);
        _history = history?.Items ?? [];
        _lastUrl = history?.LastUrl;
    }

    /// <summary>
    /// 上次打开的页面，没有时为 null
    /// </summary>
    public string? LastUrl
    {
        get
        {
            lock (_lock)
            {
                return _lastUrl;
            }
        }
    }

    /// <summary>
    /// 记录上次打开的页面；只更新内存，随观看记录一起写入
    /// </summary>
    public void SetLastUrl(string url)
    {
        lock (_lock)
        {
            if (string.Equals(_lastUrl, url, StringComparison.Ordinal))
            {
                return;
            }

            _lastUrl = url;
            _historyDirty = true;
        }
    }

    /// <summary>
    /// 收藏列表快照，最近打开或添加的在前
    /// </summary>
    public IReadOnlyList<GuideFavorite> GetFavorites()
    {
        lock (_lock)
        {
            return _favorites
                .OrderByDescending(item => item.LastOpenedAt ?? item.AddedAt)
                .Select(item => new GuideFavorite
                {
                    Id = item.Id, VideoKey = item.VideoKey, Title = item.Title, Url = item.Url,
                    AddedAt = item.AddedAt, LastOpenedAt = item.LastOpenedAt,
                })
                .ToList();
        }
    }

    /// <summary>
    /// 观看记录快照，最近的在前
    /// </summary>
    public IReadOnlyList<GuideWatchRecord> GetHistory()
    {
        lock (_lock)
        {
            return _history.Select(item => item.Clone()).ToList();
        }
    }

    /// <summary>
    /// 收藏数量
    /// </summary>
    public int FavoriteCount
    {
        get
        {
            lock (_lock)
            {
                return _favorites.Count;
            }
        }
    }

    /// <summary>
    /// 观看记录数量
    /// </summary>
    public int HistoryCount
    {
        get
        {
            lock (_lock)
            {
                return _history.Count;
            }
        }
    }

    /// <summary>
    /// 视频是否已收藏
    /// </summary>
    public bool IsFavorite(string? videoKey)
    {
        if (string.IsNullOrEmpty(videoKey))
        {
            return false;
        }

        lock (_lock)
        {
            return _favorites.Any(item => item.VideoKey == videoKey);
        }
    }

    /// <summary>
    /// 收藏或取消收藏，立即在后台写入
    /// </summary>
    /// <returns>操作后是否处于已收藏状态</returns>
    public bool ToggleFavorite(string videoKey, string title, string url)
    {
        bool favorite;
        lock (_lock)
        {
            var index = _favorites.FindIndex(item => item.VideoKey == videoKey);
            if (index >= 0)
            {
                _favorites.RemoveAt(index);
                favorite = false;
            }
            else
            {
                _favorites.Insert(0, new GuideFavorite
                {
                    VideoKey = videoKey,
                    Title = string.IsNullOrWhiteSpace(title) ? url : title.Trim(),
                    Url = url,
                });
                favorite = true;
            }
        }

        SaveFavoritesInBackground();
        return favorite;
    }

    /// <summary>
    /// 重命名收藏
    /// </summary>
    public void RenameFavorite(string id, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        lock (_lock)
        {
            var item = _favorites.FirstOrDefault(favorite => favorite.Id == id);
            if (item is null || item.Title == title.Trim())
            {
                return;
            }

            item.Title = title.Trim();
        }

        SaveFavoritesInBackground();
    }

    /// <summary>
    /// 删除收藏
    /// </summary>
    public void RemoveFavorite(string id)
    {
        lock (_lock)
        {
            if (_favorites.RemoveAll(favorite => favorite.Id == id) == 0)
            {
                return;
            }
        }

        SaveFavoritesInBackground();
    }

    /// <summary>
    /// 记录收藏最近一次被打开的时间，用于排序
    /// </summary>
    public void MarkFavoriteOpened(string id)
    {
        lock (_lock)
        {
            var item = _favorites.FirstOrDefault(favorite => favorite.Id == id);
            if (item is null)
            {
                return;
            }

            item.LastOpenedAt = DateTimeOffset.Now;
        }

        SaveFavoritesInBackground();
    }

    /// <summary>
    /// 查找视频记录，返回副本；没有时返回 null
    /// </summary>
    public GuideWatchRecord? FindRecord(string? videoKey)
    {
        if (string.IsNullOrEmpty(videoKey))
        {
            return null;
        }

        lock (_lock)
        {
            return _history.FirstOrDefault(item => item.VideoKey == videoKey)?.Clone();
        }
    }

    /// <summary>
    /// 更新观看进度：位置太靠前时不记录，接近结尾时记为看完。只更新内存，由 <see cref="FlushInBackground"/> 写入
    /// </summary>
    /// <param name="videoKey">视频标识</param>
    /// <param name="title">标题</param>
    /// <param name="url">网址</param>
    /// <param name="position">当前位置（秒）</param>
    /// <param name="duration">时长（秒）</param>
    /// <param name="playbackRate">倍速（不含临时加速）</param>
    /// <param name="ended">是否已播放结束</param>
    public void UpdateProgress(string videoKey, string title, string url, double position, double duration,
        double playbackRate, bool ended = false)
    {
        if (string.IsNullOrEmpty(videoKey) || double.IsNaN(position))
        {
            return;
        }

        var finished = ended || duration > 0 && duration - position < FinishedTailSeconds;
        lock (_lock)
        {
            var index = _history.FindIndex(item => item.VideoKey == videoKey);
            var record = index >= 0 ? _history[index] : null;
            if (record is null && (position < MinRecordPosition || finished))
            {
                return;
            }

            if (record is null)
            {
                record = new GuideWatchRecord { VideoKey = videoKey };
            }
            else
            {
                _history.RemoveAt(index);
            }

            record.Title = string.IsNullOrWhiteSpace(title) ? record.Title : title.Trim();
            record.Url = url;
            record.Duration = duration > 0 ? duration : record.Duration;
            record.Finished = finished;
            record.Position = finished ? 0 : Math.Max(0, position);
            record.PlaybackRate = playbackRate > 0 ? playbackRate : 1;
            record.UpdatedAt = DateTimeOffset.Now;
            _history.Insert(0, record);
            if (_history.Count > MaxHistory)
            {
                _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
            }

            _historyDirty = true;
        }
    }

    /// <summary>
    /// 删除一条观看记录
    /// </summary>
    public void RemoveRecord(string videoKey)
    {
        lock (_lock)
        {
            if (_history.RemoveAll(item => item.VideoKey == videoKey) == 0)
            {
                return;
            }

            _historyDirty = true;
        }

        FlushInBackground();
    }

    /// <summary>
    /// 清空观看记录
    /// </summary>
    public void ClearHistory()
    {
        lock (_lock)
        {
            if (_history.Count == 0)
            {
                return;
            }

            _history.Clear();
            _historyDirty = true;
        }

        FlushInBackground();
    }

    /// <summary>
    /// 在线程池中写入未保存的观看记录
    /// </summary>
    public void FlushInBackground()
    {
        _ = Task.Run(Flush);
    }

    /// <summary>
    /// 写入未保存的观看记录（同步），退出时调用
    /// </summary>
    public void Flush()
    {
        bool saved;
        // 取快照与写入在同一把写入锁内完成，并发保存时后写入的总是更新的快照
        lock (_saveLock)
        {
            GuideHistoryFile file;
            lock (_lock)
            {
                if (!_historyDirty)
                {
                    return;
                }

                _historyDirty = false;
                file = new GuideHistoryFile { LastUrl = _lastUrl, Items = _history.Select(item => item.Clone()).ToList() };
            }

            saved = Save(_historyPath, file);
            if (!saved)
            {
                lock (_lock)
                {
                    _historyDirty = true;
                }
            }
        }

        if (saved)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 通知界面，并在线程池中写入收藏
    /// </summary>
    private void SaveFavoritesInBackground()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        _ = Task.Run(() =>
        {
            lock (_saveLock)
            {
                GuideFavoritesFile file;
                lock (_lock)
                {
                    file = new GuideFavoritesFile
                    {
                        Items = _favorites.Select(item => new GuideFavorite
                        {
                            Id = item.Id, VideoKey = item.VideoKey, Title = item.Title, Url = item.Url,
                            AddedAt = item.AddedAt, LastOpenedAt = item.LastOpenedAt,
                        }).ToList(),
                    };
                }

                Save(_favoritesPath, file);
            }
        });
    }

    /// <summary>
    /// 原子写入 JSON，失败时记录日志。调用方持有写入锁
    /// </summary>
    /// <returns>写入成功时返回 true</returns>
    private bool Save<T>(string path, T content)
    {
        try
        {
            AtomicFile.WriteAllText(path, JsonConvert.SerializeObject(content, Formatting.Indented));
            return true;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "保存悬浮小窗数据失败：{Path}", path);
            return false;
        }
    }

    /// <summary>
    /// 读取 JSON。文件损坏时改名保留为 .invalid-时间，返回 null 使用空数据，不覆盖原内容
    /// </summary>
    private T? Load<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "读取悬浮小窗数据失败，已保留原文件：{Path}", path);
            try
            {
                File.Move(path, $"{path}.invalid-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
            }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
            {
                _logger.LogDebug(moveError, "保留损坏的悬浮小窗数据文件失败");
            }

            return null;
        }
    }
}
