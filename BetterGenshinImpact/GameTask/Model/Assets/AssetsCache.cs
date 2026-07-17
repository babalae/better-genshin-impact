using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace BetterGenshinImpact.GameTask.Model.Assets;

internal readonly record struct CaptureSize
{
    public int Width { get; }
    public int Height { get; }

    public double AssetScale => Math.Min(Width / 1920d, 1d);
    public Rect CaptureRect => new(0, 0, Width, Height);

    public CaptureSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
    }

    public static CaptureSize From(Region region)
    {
        ArgumentNullException.ThrowIfNull(region);
        return new CaptureSize(region.Width, region.Height);
    }
}

internal sealed class AssetsCache<TKey, TAssets>
    where TKey : notnull
    where TAssets : class
{
    private readonly ConcurrentDictionary<TKey, Lazy<TAssets>> _cache = new();
    private readonly Func<TKey, TAssets> _factory;

    public AssetsCache(Func<TKey, TAssets> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public TAssets Get(TKey key)
    {
        var lazy = _cache.GetOrAdd(
            key,
            cacheKey => new Lazy<TAssets>(
                () => _factory(cacheKey),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return lazy.Value;
        }
        catch
        {
            if (_cache.TryGetValue(key, out var cachedLazy) && ReferenceEquals(cachedLazy, lazy))
            {
                _cache.TryRemove(key, out _);
            }

            throw;
        }
    }
}

internal sealed class CaptureAssetsCache<TAssets>
    where TAssets : class
{
    private readonly AssetsCache<CaptureSize, TAssets> _cache;

    public CaptureAssetsCache(Func<CaptureSize, TAssets> factory)
    {
        _cache = new AssetsCache<CaptureSize, TAssets>(factory);
    }

    public TAssets Get(Region region)
    {
        return _cache.Get(CaptureSize.From(region));
    }

    public TAssets Get(int captureWidth, int captureHeight)
    {
        return _cache.Get(new CaptureSize(captureWidth, captureHeight));
    }
}
