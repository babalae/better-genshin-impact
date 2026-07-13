using System;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.Model.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;
using Vanara.PInvoke;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.AutoPick.Assets;

public sealed class AutoPickAssets
{
    private static readonly AssetsCache<CacheKey, AutoPickAssets> Cache = new(
        static key => new AutoPickAssets(key.CaptureSize, key.PickKey));
    private readonly ILogger<AutoPickAssets> _logger = App.GetLogger<AutoPickAssets>();

    public User32.VK PickVk { get; private set; } = User32.VK.VK_F;
    public RecognitionObject PickRo { get; private set; }
    public RecognitionObject ChatPickRo { get; private set; }

    private int CaptureHeight { get; }
    private double AssetScale { get; }

    private AutoPickAssets(CaptureSize captureSize, string pickKey)
    {
        CaptureHeight = captureSize.Height;
        AssetScale = captureSize.AssetScale;
        PickRo = RecognitionAssets.Get("AutoPick", "F", captureSize.Width, captureSize.Height);
        ChatPickRo = LoadCustomChatPickKey("F", captureSize);
        if (pickKey != "F")
        {
            try
            {
                PickRo = LoadCustomPickKey(pickKey, captureSize);
                PickVk = User32Helper.ToVk(pickKey);
                ChatPickRo = LoadCustomChatPickKey(pickKey, captureSize);
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "加载自定义拾取按键时发生异常");
                _logger.LogError("加载自定义拾取按键失败，继续使用默认的F键");
                return;
            }

            _logger.LogInformation("自定义拾取按键：{Key}", pickKey);
        }
    }

    public static AutoPickAssets Get(Region region, string pickKey)
    {
        return Get(CaptureSize.From(region), pickKey);
    }

    public static AutoPickAssets Get(int captureWidth, int captureHeight, string pickKey)
    {
        return Get(new CaptureSize(captureWidth, captureHeight), pickKey);
    }

    private static AutoPickAssets Get(CaptureSize captureSize, string pickKey)
    {
        var normalizedPickKey = string.IsNullOrWhiteSpace(pickKey) ? "F" : pickKey.Trim().ToUpperInvariant();
        return Cache.Get(new CacheKey(captureSize, normalizedPickKey));
    }

    private RecognitionObject LoadCustomPickKey(string key, CaptureSize captureSize)
    {
        return new RecognitionObject
        {
            Name = key,
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoPick", key + ".png", captureSize.Width, captureSize.Height),
            RegionOfInterest = new Rect((int)(1090 * AssetScale),
                (int)(330 * AssetScale),
                (int)(60 * AssetScale),
                (int)(420 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();
    }

    private RecognitionObject LoadCustomChatPickKey(string key, CaptureSize captureSize)
    {
        return new RecognitionObject
        {
            Name = "chatPick" + key,
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = GameTaskManager.LoadAssetImage("AutoPick", key + ".png", captureSize.Width, captureSize.Height),
            RegionOfInterest = new Rect((int)(1200 * AssetScale),
                (int)(350 * AssetScale),
                (int)(50 * AssetScale),
                CaptureHeight - (int)(220 * AssetScale) - (int)(350 * AssetScale)),
            DrawOnWindow = false
        }.InitTemplate();
    }

    private readonly record struct CacheKey(CaptureSize CaptureSize, string PickKey);

}
