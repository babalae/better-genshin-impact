using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Script.Dependence.Model.TimerConfig;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFishing.Assets;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Assets;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.AutoWood.Assets;
using BetterGenshinImpact.GameTask.AutoEat.Assets;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.GameLoading;
using BetterGenshinImpact.GameTask.GameLoading.Assets;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Placeholder;
using BetterGenshinImpact.GameTask.QuickSereniteaPot.Assets;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.View.Drawable;
using OpenCvSharp;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoDomain.Assets;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.GameTask.SkillCd;

namespace BetterGenshinImpact.GameTask;

internal class GameTaskManager
{
    public static ConcurrentDictionary<string, ITaskTrigger>? TriggerDictionary { get; set; }

    /// <summary>
    /// 一定要在任务上下文初始化完毕后使用
    /// </summary>
    /// <returns></returns>
    public static List<ITaskTrigger> LoadInitialTriggers()
    {
        ReloadAssets();
        TriggerDictionary = new ConcurrentDictionary<string, ITaskTrigger>();

        TriggerDictionary.TryAdd("RecognitionTest", new TestTrigger());
        TriggerDictionary.TryAdd("GameLoading", new GameLoadingTrigger());
        TriggerDictionary.TryAdd("AutoPick", new AutoPick.AutoPickTrigger());
        TriggerDictionary.TryAdd("QuickTeleport", new QuickTeleport.QuickTeleportTrigger());
        TriggerDictionary.TryAdd("AutoSkip", new AutoSkip.AutoSkipTrigger());
        TriggerDictionary.TryAdd("AutoFish", new AutoFishing.AutoFishingTrigger());
        TriggerDictionary.TryAdd("AutoCook", new AutoCook.AutoCookTrigger());
        TriggerDictionary.TryAdd("AutoEat", new AutoEat.AutoEatTrigger());
        TriggerDictionary.TryAdd("MapMask", new MapMaskTrigger());
        TriggerDictionary.TryAdd("SkillCd", new SkillCdTrigger());

        return ConvertToTriggerList();
    }

    public static List<ITaskTrigger> ConvertToTriggerList(bool allEnabled = false)
    {
        if (TriggerDictionary is null)
        {
            return [];
        }

        var loadedTriggers = TriggerDictionary.Values.ToList();

        loadedTriggers.ForEach(i => i.Init());
        if (allEnabled)
        {
            loadedTriggers.ForEach(i => i.IsEnabled = true);
        }

        loadedTriggers = [.. loadedTriggers.OrderByDescending(i => i.Priority)];
        return loadedTriggers;
    }

    public static void ClearTriggers()
    {
        TriggerDictionary?.Clear();
    }

    /// <summary>
    /// 通过名称添加触发器
    /// </summary>
    /// <param name="name"></param>
    /// <param name="externalConfig"></param>
    public static bool AddTrigger(string name, object? externalConfig)
    {
        TriggerDictionary ??= new ConcurrentDictionary<string, ITaskTrigger>();

        ITaskTrigger? trigger = null;
        string? triggerName = null;
        switch (name)
        {
            case "AutoPick":
                triggerName = "AutoPick";
                trigger = new AutoPick.AutoPickTrigger(externalConfig as AutoPickExternalConfig);
                break;
            case "AutoSkip":
                triggerName = "AutoSkip";
                trigger = externalConfig is null ? new AutoSkip.AutoSkipTrigger() : new AutoSkip.AutoSkipTrigger(externalConfig as AutoSkipConfig);
                break;
            case "AutoEat":
                triggerName = "AutoEat";
                trigger = new AutoEat.AutoEatTrigger();
                break;
        }

        if (triggerName == null || trigger == null)
        {
            return false;
        }
        TriggerDictionary[triggerName] = trigger;
        return true;
    }

    public static void RefreshTriggerConfigs()
    {
        if (TriggerDictionary is { Count: > 0 })
        {
            TriggerDictionary.GetValueOrDefault("AutoPick")?.Init();
            TriggerDictionary.GetValueOrDefault("AutoSkip")?.Init();
            TriggerDictionary.GetValueOrDefault("AutoFish")?.Init();
            TriggerDictionary.GetValueOrDefault("QuickTeleport")?.Init();
            // TriggerDictionary.GetValueOrDefault("GameLoading")?.Init();
            TriggerDictionary.GetValueOrDefault("AutoCook")?.Init();
            TriggerDictionary.GetValueOrDefault("AutoEat")?.Init();
            TriggerDictionary.GetValueOrDefault("MapMask")?.Init();
            TriggerDictionary.GetValueOrDefault("SkillCd")?.Init();
            // 清理画布
            VisionContext.Instance().DrawContent.ClearAll();
        }

        ReloadAssets();
    }

    public static void ReloadAssets()
    {
        AutoPickAssets.DestroyInstance();
        AutoSkipAssets.DestroyInstance();
        AutoFishingAssets.DestroyInstance();
        QuickTeleportAssets.DestroyInstance();
        AutoWoodAssets.DestroyInstance();
        AutoGeniusInvokationAssets.DestroyInstance();
        AutoFightAssets.DestroyInstance();
        ElementAssets.DestroyInstance();
        QuickSereniteaPotAssets.DestroyInstance();
        GameLoadingAssets.DestroyInstance();
        MapLazyAssets.DestroyInstance();
        AutoEatAssets.DestroyInstance();
        AutoDomainAssets.DestroyInstance();
    }

    /// <summary>
    /// 获取素材图片并缩放
    /// todo 支持多语言
    /// </summary>
    /// <param name="featName">任务名称</param>
    /// <param name="assertName">素材文件名</param>
    /// <param name="flags"></param>
    /// <returns></returns>
    /// <exception cref="FileNotFoundException"></exception>
    public static Mat LoadAssetImage(string featName, string assertName, ImreadModes flags = ImreadModes.Color)
    {
        return LoadAssetImage(featName, assertName, TaskContext.Instance().SystemInfo, flags);
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

        if (!Directory.Exists(assetsFolder))
        {
            throw new FileNotFoundException($"未找到{featName}的素材文件夹");
        }

        var filePath = Path.Combine(assetsFolder, assertName);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"未找到{featName}中的{assertName}文件");
        }

        var mat = Mat.FromStream(File.OpenRead(filePath), flags);
        if (systemInfo.GameScreenSize.Width != 1920)
        {
            mat = ResizeHelper.Resize(mat, systemInfo.AssetScale);
        }

        return mat;
    }
}
