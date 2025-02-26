﻿using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Script.Dependence.Model.TimerConfig;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoFishing.Assets;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Assets;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.AutoSkip.Assets;
using BetterGenshinImpact.GameTask.AutoWood.Assets;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.GameLoading;
using BetterGenshinImpact.GameTask.GameLoading.Assets;
using BetterGenshinImpact.GameTask.Placeholder;
using BetterGenshinImpact.GameTask.QuickSereniteaPot.Assets;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.View.Drawable;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using OpenCvSharp;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
    public static void AddTrigger(string name, object? externalConfig)
    {
        TriggerDictionary ??= new ConcurrentDictionary<string, ITaskTrigger>();
        TriggerDictionary.Clear(); //TODO 有问题，不应该清理

        if (name == "AutoPick")
        {
            TriggerDictionary.TryAdd("AutoPick", new AutoPick.AutoPickTrigger(externalConfig as AutoPickExternalConfig));
        }
        else if (name == "AutoSkip")
        {
            TriggerDictionary.TryAdd("AutoSkip", new AutoSkip.AutoSkipTrigger());
        }
        // else if (name == "AutoFish")
        // {
        //     TriggerDictionary.Add("AutoFish", new AutoFishing.AutoFishingTrigger());
        // }
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
            // 清理画布
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(new object(), "RemoveAllButton", new object(), ""));
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
        var info = TaskContext.Instance().SystemInfo;
        return LoadAssetImage(featName, assertName, info.GameScreenSize.Width, info.GameScreenSize.Height, info.AssetScale, flags);
    }

    /// <summary>
    /// 这个重载是为了和TaskContext.Instance().SystemInfo解耦
    /// todo: 更系统的分层
    /// </summary>
    /// <returns></returns>
    /// <exception cref="FileNotFoundException"></exception>
    public static Mat LoadAssetImage(string featName, string assertName, int width, int height, double assetScale, ImreadModes flags = ImreadModes.Color)
    {
        var assetsFolder = Global.Absolute($@"GameTask\{featName}\Assets\{width}x{height}");
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
        if (width != 1920)
        {
            mat = ResizeHelper.Resize(mat, assetScale);
        }

        return mat;
    }
}
