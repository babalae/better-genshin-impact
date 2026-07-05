using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Collections.Concurrent;
using System.IO;

namespace BetterGenshinImpact.Core.Recognition;

public static class RecognitionAssets
{
    private static readonly ConcurrentDictionary<RecognitionAssetCacheKey, RecognitionObject> Cache = new();

    public static RecognitionObject Get(string taskName, string objectName, Region region)
    {
        ArgumentNullException.ThrowIfNull(region);
        return Get(taskName, objectName, region.Width, region.Height);
    }

    public static RecognitionObject Get(string taskName, string objectName)
    {
        var captureRect = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect;
        return Get(taskName, objectName, captureRect.Width, captureRect.Height);
    }

    public static RecognitionObject Get(string taskName, string objectName, int captureWidth, int captureHeight)
    {
        var key = new RecognitionAssetCacheKey(taskName, objectName, captureWidth, captureHeight);
        var recognitionObject = Cache.GetOrAdd(key, static cacheKey => Load(cacheKey));
        return recognitionObject.Clone();
    }

    public static void Clear(string taskName, string objectName)
    {
        foreach (var key in Cache.Keys)
        {
            if (string.Equals(key.TaskName, taskName, StringComparison.Ordinal)
                && string.Equals(key.ObjectName, objectName, StringComparison.Ordinal))
            {
                Cache.TryRemove(key, out _);
            }
        }
    }

    public static void ClearTask(string taskName)
    {
        foreach (var key in Cache.Keys)
        {
            if (string.Equals(key.TaskName, taskName, StringComparison.Ordinal))
            {
                Cache.TryRemove(key, out _);
            }
        }
    }

    public static void ClearAll()
    {
        Cache.Clear();
    }

    private static RecognitionObject Load(RecognitionAssetCacheKey key)
    {
        return RecognitionObjectJsonLoader.LoadFromFile(
            Global.Absolute($@"GameTask\{key.TaskName}\Assets\Recognition.json"),
            key.ObjectName,
            new RecognitionObjectJsonLoadContext
            {
                CaptureHeight = key.CaptureHeight,
                CaptureWidth = key.CaptureWidth,
                TemplateLoader = (template, mode) => LoadTemplateImage(key.TaskName, template, key.CaptureWidth, key.CaptureHeight, mode),
            });
    }

    private static Mat LoadTemplateImage(string taskName, string assetName, int captureWidth, int captureHeight, ImreadModes flags)
    {
        var assetsFolder = Global.Absolute($@"GameTask\{taskName}\Assets\{captureWidth}x{captureHeight}");
        if (!Directory.Exists(assetsFolder))
        {
            assetsFolder = Global.Absolute($@"GameTask\{taskName}\Assets\1920x1080");
        }

        if (!Directory.Exists(assetsFolder))
        {
            throw new FileNotFoundException($"未找到{taskName}的素材文件夹");
        }

        var filePath = Path.Combine(assetsFolder, assetName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"未找到{taskName}中的{assetName}文件");
        }

        using var stream = File.OpenRead(filePath);
        var mat = Mat.FromStream(stream, flags);
        if (captureWidth < 1920)
        {
            using (mat)
            {
                return ResizeHelper.Resize(mat, captureWidth / 1920d);
            }
        }

        return mat;
    }

    private readonly record struct RecognitionAssetCacheKey(string TaskName, string ObjectName, int CaptureWidth, int CaptureHeight);
}
