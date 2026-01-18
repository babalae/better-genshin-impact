using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Model.MaskMap;

namespace BetterGenshinImpact.Service;

public class PointImageCacheManager
{
    private static readonly Lazy<PointImageCacheManager> _instance = new(() => new PointImageCacheManager());
    public static PointImageCacheManager Instance => _instance.Value;

    private static readonly TimeSpan _ttl = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, CacheEntry> _imageCache = new();
    private readonly object _loadLock = new();
    private readonly ConcurrentDictionary<string, Task> _inflightLoads = new(StringComparer.OrdinalIgnoreCase);

    private PointImageCacheManager()
    {
    }

    public event EventHandler<string>? ImageUpdated;

    /// <summary>
    /// 获取图片（如果未缓存则加载）
    /// </summary>
    public ImageSource? GetImage(string labelId, string iconUrl)
    {
        if (string.IsNullOrEmpty(iconUrl))
            return null;

        var now = DateTimeOffset.UtcNow;
        if (_imageCache.TryGetValue(labelId, out var existing) &&
            existing.ExpiresAt > now &&
            string.Equals(existing.IconUrl, iconUrl, StringComparison.OrdinalIgnoreCase))
        {
            return existing.Image;
        }

        lock (_loadLock)
        {
            now = DateTimeOffset.UtcNow;
            if (_imageCache.TryGetValue(labelId, out existing) &&
                existing.ExpiresAt > now &&
                string.Equals(existing.IconUrl, iconUrl, StringComparison.OrdinalIgnoreCase))
            {
                return existing.Image;
            }

            var image = LoadImage(iconUrl);
            if (image == null)
            {
                return null;
            }

            _imageCache[labelId] = new CacheEntry(image, iconUrl, now.Add(_ttl));
            return image;
        }
    }

    public ImageSource? TryGetImage(string labelId, string iconUrl)
    {
        if (string.IsNullOrEmpty(iconUrl))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        if (_imageCache.TryGetValue(labelId, out var existing) &&
            existing.ExpiresAt > now &&
            string.Equals(existing.IconUrl, iconUrl, StringComparison.OrdinalIgnoreCase))
        {
            return existing.Image;
        }

        if (existing != null && existing.ExpiresAt <= now)
        {
            _imageCache.TryRemove(labelId, out _);
        }

        return null;
    }

    public Task EnsureImageAsync(string labelId, string iconUrl, CancellationToken ct = default)
    {
        if (TryGetImage(labelId, iconUrl) != null)
        {
            return Task.CompletedTask;
        }

        var key = $"{labelId}|{iconUrl}";
        return _inflightLoads.GetOrAdd(key, _ => LoadAndStoreAsync(labelId, iconUrl, key, ct));
    }

    /// <summary>
    /// 预加载图片
    /// </summary>
    public void PreloadImage(string labelId, string iconUrl)
    {
        if (string.IsNullOrEmpty(iconUrl))
            return;

        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_imageCache.TryGetValue(labelId, out var existing) &&
                existing.ExpiresAt > now &&
                string.Equals(existing.IconUrl, iconUrl, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _ = EnsureImageAsync(labelId, iconUrl);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"预加载图片失败: {labelId}, {ex.Message}");
        }
    }

    /// <summary>
    /// 批量预加载图片
    /// </summary>
    public void PreloadImages(IEnumerable<MaskMapPointLabel> labels)
    {
        Parallel.ForEach(labels, label => { PreloadImage(label.LabelId, label.IconUrl); });
    }

    /// <summary>
    /// 异步批量预加载图片
    /// </summary>
    public async Task PreloadImagesAsync(IEnumerable<MaskMapPointLabel> labels)
    {
        await Task.Run(() => PreloadImages(labels));
    }

    /// <summary>
    /// 清除缓存
    /// </summary>
    public void ClearCache()
    {
        _imageCache.Clear();
    }

    /// <summary>
    /// 移除指定图片缓存
    /// </summary>
    public void RemoveImage(string labelId)
    {
        _imageCache.TryRemove(labelId, out _);
    }

    private async Task LoadAndStoreAsync(string labelId, string iconUrl, string inflightKey, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var image = await StaRunner.Instance.InvokeAsync(() => LoadImage(iconUrl)).ConfigureAwait(false);
            if (image == null)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            _imageCache[labelId] = new CacheEntry(image, iconUrl, now.Add(_ttl));
            ImageUpdated?.Invoke(this, labelId);
        }
        catch
        {
        }
        finally
        {
            _inflightLoads.TryRemove(inflightKey, out _);
        }
    }

    /// <summary>
    /// 加载图片
    /// </summary>
    private ImageSource LoadImage(string iconUrl)
    {
        try
        {
            BitmapImage bitmap;

            // 判断是否为网络路径
            if (iconUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                iconUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(iconUrl, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
            }
            // 判断是否为 pack URI
            else if (iconUrl.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
            {
                bitmap = new BitmapImage(new Uri(iconUrl, UriKind.Absolute));
            }
            // 本地文件路径
            else if (File.Exists(iconUrl))
            {
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(iconUrl, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
            }
            else
            {
                // 尝试作为相对路径
                var basePath = AppDomain.CurrentDomain.BaseDirectory;
                var fullPath = Path.Combine(basePath, iconUrl);

                if (File.Exists(fullPath))
                {
                    bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                }
                else
                {
                    return null;
                }
            }

            bitmap.Freeze(); // 冻结以提高性能和线程安全
            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载图片失败: {iconUrl}, {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 获取缓存统计信息
    /// </summary>
    public (int Count, long EstimatedMemoryBytes) GetCacheStats()
    {
        long estimatedMemory = 0;
        foreach (var entry in _imageCache.Values)
        {
            if (entry.Image is BitmapSource bitmapSource)
            {
                estimatedMemory += bitmapSource.PixelWidth * bitmapSource.PixelHeight * 4; // 假设 ARGB
            }
        }

        return (_imageCache.Count, estimatedMemory);
    }

    private sealed class CacheEntry
    {
        public ImageSource Image { get; }
        public string IconUrl { get; }
        public DateTimeOffset ExpiresAt { get; }

        public CacheEntry(ImageSource image, string iconUrl, DateTimeOffset expiresAt)
        {
            Image = image;
            IconUrl = iconUrl;
            ExpiresAt = expiresAt;
        }
    }

    private sealed class StaRunner
    {
        public static StaRunner Instance { get; } = new();

        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;

        private StaRunner()
        {
            _thread = new Thread(Run) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        private void Run()
        {
            foreach (var action in _queue.GetConsumingEnumerable())
            {
                action();
            }
        }

        public Task<T> InvokeAsync<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(() =>
            {
                try
                {
                    tcs.SetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }
    }
}
