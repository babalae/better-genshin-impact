using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BetterGenshinImpact.Model.MaskMap;

namespace BetterGenshinImpact.Service;

public class PointImageCacheManager
{
    private static readonly Lazy<PointImageCacheManager> _instance = new(() => new PointImageCacheManager());
    public static PointImageCacheManager Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, ImageSource> _imageCache = new();
    private readonly object _loadLock = new();

    private PointImageCacheManager()
    {
    }

    /// <summary>
    /// 获取图片（如果未缓存则加载）
    /// </summary>
    public ImageSource? GetImage(string labelId, string iconUrl)
    {
        if (string.IsNullOrEmpty(iconUrl))
            return null;

        return _imageCache.GetOrAdd(labelId, _ => LoadImage(iconUrl));
    }

    /// <summary>
    /// 预加载图片
    /// </summary>
    public void PreloadImage(string labelId, string iconUrl)
    {
        if (string.IsNullOrEmpty(iconUrl) || _imageCache.ContainsKey(labelId))
            return;

        try
        {
            var image = LoadImage(iconUrl);
            _imageCache.TryAdd(labelId, image);
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
        foreach (var image in _imageCache.Values)
        {
            if (image is BitmapSource bitmapSource)
            {
                estimatedMemory += bitmapSource.PixelWidth * bitmapSource.PixelHeight * 4; // 假设 ARGB
            }
        }

        return (_imageCache.Count, estimatedMemory);
    }
}