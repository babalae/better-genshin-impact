using System;
using System.Collections.Generic;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Simulator;
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

    /// <summary>
    /// 兼容仍按当前游戏分辨率访问拾取资产的旧调用方。
    /// 新代码应优先使用 <see cref="Get(Region,string)"/>，避免截图尺寸变化时复用错误模板。
    /// </summary>
    public static AutoPickAssets Instance
    {
        get
        {
            var systemInfo = TaskContext.Instance().SystemInfo;
            return Get(systemInfo.GameScreenSize.Width,
                systemInfo.GameScreenSize.Height,
                TaskContext.Instance().Config.AutoPickConfig.PickKey);
        }
    }

    public User32.VK PickVk { get; private set; } = User32.VK.VK_F;
    public bool UseControllerY { get; private set; }
    public RecognitionObject PickRo { get; private set; }
    public RecognitionObject ChatPickRo { get; private set; }
    public IReadOnlyList<RecognitionObject> ControllerIconBlacklistRos { get; private set; } = [];

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
                ChatPickRo = LoadCustomChatPickKey(pickKey, captureSize);
                if (IsControllerYPromptKey(pickKey))
                {
                    UseControllerY = true;
                    PickVk = User32.VK.VK_F;
                    TaskContext.Instance().Config.KeyBindingsConfig.PickUpOrInteract = Core.Config.KeyId.F;
                    ControllerIconBlacklistRos = LoadControllerIconBlacklistTemplates(captureSize);
                    VirtualXbox360Controller.EnsureConnected(_logger);
                }
                else
                {
                    PickVk = User32Helper.ToVk(pickKey);
                    TaskContext.Instance().Config.KeyBindingsConfig.PickUpOrInteract = (Core.Config.KeyId)(int)PickVk;
                }
            }
            catch (Exception e)
            {
                _logger.LogDebug(e, "加载自定义拾取按键时发生异常");
                _logger.LogError("加载自定义拾取按键失败，继续使用默认的F键");
                return;
            }

            if (UseControllerY)
            {
                _logger.LogInformation("自定义拾取提示：手柄Y（YY模板，交互发送虚拟手柄Y键）");
            }
            else
            {
                _logger.LogInformation("自定义拾取按键：{Key}", pickKey);
            }
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

    public static bool IsControllerYPromptKey(string key)
    {
        return string.Equals(key, "YY", StringComparison.OrdinalIgnoreCase);
    }

    public void PressPickKey()
    {
        if (UseControllerY)
        {
            VirtualXbox360Controller.PressY(_logger);
            return;
        }

        Simulation.SendInput.Keyboard.KeyPress(PickVk);
    }

    private static IReadOnlyList<RecognitionObject> LoadControllerIconBlacklistTemplates(CaptureSize captureSize)
    {
        var assetsFolder = Global.Absolute($@"GameTask\AutoPick\Assets\{captureSize.Width}x{captureSize.Height}\controller_icon_blacklist");
        if (!Directory.Exists(assetsFolder))
        {
            assetsFolder = Global.Absolute(@"GameTask\AutoPick\Assets\1920x1080\controller_icon_blacklist");
        }

        return LoadControllerIconBlacklistTemplates(assetsFolder, captureSize.Width != 1920 ? captureSize.AssetScale : 1d);
    }

    internal static IReadOnlyList<RecognitionObject> LoadControllerIconBlacklistTemplates(string directoryPath, double assetScale = 1d)
    {
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        var templates = new List<RecognitionObject>();
        foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*.png", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                var mat = Mat.FromStream(stream, ImreadModes.Color);
                if (mat.Empty())
                {
                    mat.Dispose();
                    continue;
                }

                if (Math.Abs(assetScale - 1d) > 0.001)
                {
                    var resized = ResizeHelper.Resize(mat, assetScale);
                    mat.Dispose();
                    mat = resized;
                }

                templates.Add(CreateControllerIconBlacklistTemplate(Path.GetFileNameWithoutExtension(filePath), mat));
            }
            catch (Exception)
            {
                // 损坏的可选模板不能阻断自动拾取初始化。
            }
        }

        return templates;
    }

    internal static RecognitionObject CreateControllerIconBlacklistTemplate(string name, Mat templateMat)
    {
        return new RecognitionObject
        {
            Name = $"ControllerIconBlacklist:{name}",
            RecognitionType = RecognitionTypes.TemplateMatch,
            TemplateImageMat = templateMat,
            Threshold = 0.9,
            DrawOnWindow = false
        }.InitTemplate();
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
