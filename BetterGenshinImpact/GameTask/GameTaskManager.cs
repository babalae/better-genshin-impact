using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Assets;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.GameLoading;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Placeholder;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using OpenCvSharp;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.GameTask.SkillCd;
using BetterGenshinImpact.GameTask.Runtime;
using System;

namespace BetterGenshinImpact.GameTask;

internal class GameTaskManager
{
    /// <summary>
    /// 创建一次截图会话用的全部触发器，按优先级从高到低排列。由 TaskTriggerDispatcher.Start 调用
    /// </summary>
    /// <param name="runtime">本次截图会话绑定的运行环境，触发器在整个会话内只使用它</param>
    /// <param name="config">配置根</param>
    public static List<ITaskTrigger> CreateTriggers(GameRuntime runtime, AllConfig config)
    {
        List<ITaskTrigger> triggers =
        [
            new TestTrigger(runtime),
            new GameLoadingTrigger(runtime, config),
            new AutoPick.AutoPickTrigger(runtime, config),
            new QuickTeleport.QuickTeleportTrigger(runtime, config),
            new AutoSkip.AutoSkipTrigger(runtime, config),
            new AutoFishing.AutoFishingTrigger(runtime, config),
            new AutoEat.AutoEatTrigger(runtime, config),
            new MapMaskTrigger(runtime, config),
            new SkillCdTrigger(runtime, config),
        ];

        return [.. triggers.OrderByDescending(t => t.Priority)];
    }

    /// <summary>
    /// 获取素材图片并缩放
    /// </summary>
    /// <returns></returns>
    /// <exception cref="FileNotFoundException"></exception>
    public static Mat LoadAssetImage(string featName, string assertName, ISystemInfo systemInfo, ImreadModes flags = ImreadModes.Color)
    {
        var assetsFolder = Global.Absolute($@"GameTask\{featName}\Assets\{systemInfo.GameScreenSize.Width}x{systemInfo.GameScreenSize.Height}");
        if (!Directory.Exists(assetsFolder))
        {
            assetsFolder = Global.Absolute($@"GameTask\{featName}\Assets\1920x1080");
        }

        var mat = LoadRepoAssetMat(featName, assertName, flags);
        if (mat == null)
        {
            mat = LoadBundledAssetMat(assetsFolder, featName, assertName, flags);
        }

        if (systemInfo.GameScreenSize.Width != 1920 && systemInfo.AssetScale != 1)
        {
            using (mat)
            {
                return ResizeHelper.Resize(mat, systemInfo.AssetScale);
            }
        }

        return mat;
    }

    /// <summary>
    /// 根据捕获区域宽高加载素材图片并缩放
    /// </summary>
    /// <returns></returns>
    /// <exception cref="FileNotFoundException"></exception>
    public static Mat LoadAssetImage(string featName, string assertName, int captureWidth, int captureHeight, ImreadModes flags = ImreadModes.Color)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(captureWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(captureHeight);

        var assetsFolder = Global.Absolute($@"GameTask\{featName}\Assets\{captureWidth}x{captureHeight}");
        if (!Directory.Exists(assetsFolder))
        {
            assetsFolder = Global.Absolute($@"GameTask\{featName}\Assets\1920x1080");
        }

        var mat = LoadRepoAssetMat(featName, assertName, flags)
                  ?? LoadBundledAssetMat(assetsFolder, featName, assertName, flags);

        if (captureWidth < 1920)
        {
            using (mat)
            {
                return ResizeHelper.Resize(mat, captureWidth / 1920d);
            }
        }

        return mat;
    }

    /// <summary>读取随本体分发的素材。</summary>
    private static Mat LoadBundledAssetMat(string assetsFolder, string featName, string assertName, ImreadModes flags)
    {
        if (!Directory.Exists(assetsFolder))
        {
            throw new FileNotFoundException($"未找到{featName}的素材文件夹");
        }

        var filePath = Path.Combine(assetsFolder, assertName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"未找到{featName}中的{assertName}文件");
        }

        using var stream = File.OpenRead(filePath);
        return Mat.FromStream(stream, flags);
    }

    /// <summary>乐器模板优先使用仓库版本，离线时仍可使用内置素材。</summary>
    private static Mat? LoadRepoAssetMat(string featName, string assertName, ImreadModes flags)
    {
        if (featName != "Music")
        {
            return null;
        }

        const string prefix = "Instruments/";
        var assetPath = assertName.Replace('\\', '/');
        if (!assetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var fileName = assetPath[prefix.Length..];
        if (fileName.Length == 0 || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        var bytes = ScriptRepoUpdater.Instance.ReadAssetFile($"music/instruments/{fileName}");
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            var mat = Mat.ImDecode(bytes, flags);
            if (!mat.Empty())
            {
                return mat;
            }

            mat.Dispose();
        }
        catch (OpenCVException)
        {
            // 仓库图片损坏时，继续使用内置素材。
        }

        return null;
    }
}
