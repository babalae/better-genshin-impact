using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Script.Dependence.Model.TimerConfig;
using BetterGenshinImpact.GameTask.AutoFight.Assets;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Assets;
using BetterGenshinImpact.GameTask.AutoPick.Assets;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.GameLoading;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Placeholder;
using BetterGenshinImpact.GameTask.QuickTeleport.Assets;
using BetterGenshinImpact.View.Drawable;
using OpenCvSharp;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.GameTask.AutoSkip;
using BetterGenshinImpact.GameTask.ExceptionRecovery;
using BetterGenshinImpact.GameTask.MapMask;
using BetterGenshinImpact.GameTask.SkillCd;
using System;

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

        // 常驻触发器（ITaskTrigger.AlwaysActive）**跨会话复用同一实例**：
        // 任务结束（TaskRunner.End）也会走这里整体重建字典，若这里 new 新实例，进行中的状态
        // （例如"弹窗已点掉、正在等「点击进入」"）会被静默重置、退避记忆也会丢。
        // 这与 ClearTriggers 保留常驻触发器是同一件事的两半。
        // 光复用实例还不够：ConvertToTriggerList 也必须**不**对它调用 Init，否则状态照样被清掉
        // （那里已把常驻触发器排除在 Init 之外——两处是同一套约定，改动时必须同时改）。
        var previous = TriggerDictionary;
        TriggerDictionary = new ConcurrentDictionary<string, ITaskTrigger>();

        TriggerDictionary.TryAdd("RecognitionTest", new TestTrigger());
        TriggerDictionary.TryAdd("GameLoading", new GameLoadingTrigger());
        TriggerDictionary.TryAdd("AutoPick", new AutoPick.AutoPickTrigger());
        TriggerDictionary.TryAdd("QuickTeleport", new QuickTeleport.QuickTeleportTrigger());
        TriggerDictionary.TryAdd("AutoSkip", new AutoSkip.AutoSkipTrigger());
        TriggerDictionary.TryAdd("AutoFish", new AutoFishing.AutoFishingTrigger());
        TriggerDictionary.TryAdd("AutoEat", new AutoEat.AutoEatTrigger());
        TriggerDictionary.TryAdd("MapMask", new MapMaskTrigger());
        TriggerDictionary.TryAdd("SkillCd", new SkillCdTrigger());
        TriggerDictionary.TryAdd("GameExceptionPopup",
            ReuseAlwaysActive(previous, "GameExceptionPopup") ?? new GameExceptionPopupTrigger());

        return ConvertToTriggerList();
    }

    /// <summary>取上一个字典里同名的常驻触发器：存在且声明了 <see cref="ITaskTrigger.AlwaysActive"/> 时复用同一实例。</summary>
    private static ITaskTrigger? ReuseAlwaysActive(ConcurrentDictionary<string, ITaskTrigger>? previous, string name)
    {
        if (previous != null && previous.TryGetValue(name, out var old) && old.AlwaysActive)
        {
            return old;
        }

        return null;
    }

    /// <param name="allEnabled">是否把所有触发器都置为启用（自动秘境/配置组/脚本 API 的 AddTrigger 会用到）。</param>
    /// <param name="skipInit">
    /// 是否跳过 Init()。用于"任务启动清空实时触发器"：这些触发器要留在列表里继续工作，
    /// 但不能被重新 Init——配置组会为每个项目调用一次 <see cref="ClearTriggers"/>。
    /// </param>
    public static List<ITaskTrigger> ConvertToTriggerList(bool allEnabled = false, bool skipInit = false)
    {
        if (TriggerDictionary is null)
        {
            return [];
        }

        var loadedTriggers = TriggerDictionary.Values.ToList();

        if (!skipInit)
        {
            // 常驻触发器（<see cref="ITaskTrigger.AlwaysActive"/>）**不在这里 Init**：
            // 它们跨任务存活，而重建列表有多条路径（任务启停、ClearTriggers、AddTrigger），
            // 逐条判断"本次用到的是新建实例还是复用实例"极易漏一处，漏掉就等于把运行状态静默重置。
            // 因此把它们的生命周期收口到一个地方：**只有实时触发会话启动时**
            // （TaskTriggerDispatcher.Start 里显式调用 Init），那是唯一确定"新会话开始"的时机。
            // 这个约定的前提是：常驻触发器新建时字段已由自身初始化（Init 只是再置一遍同样的默认值）。
            loadedTriggers.ForEach(i =>
            {
                if (!i.AlwaysActive)
                {
                    i.Init();
                }
            });
        }

        if (allEnabled)
        {
            loadedTriggers.ForEach(i => i.IsEnabled = true);
        }

        loadedTriggers = [.. loadedTriggers.OrderByDescending(i => i.Priority)];
        return loadedTriggers;
    }

    /// <summary>
    /// 清空实时触发器（任务启动/任务结束都会调用）。
    /// 常驻触发器（<see cref="ITaskTrigger.AlwaysActive"/>：游戏异常弹窗处理）必须跨任务存活——
    /// 任务是它最需要工作的场景；若一并清掉，任务期间既没有 OnCapture 驱动，
    /// 也再没有任何时机把它放回列表（任务结束才会 LoadInitialTriggers）。
    /// </summary>
    public static void ClearTriggers()
    {
        // 先固定本地引用：LoadInitialTriggers() 会整体替换 TriggerDictionary，
        // 若每步都重新读静态属性，快照与删除可能落在两个不同的字典上。
        var dict = TriggerDictionary;
        if (dict is null)
        {
            return;
        }

        foreach (var name in dict.Where(kv => !kv.Value.AlwaysActive).Select(kv => kv.Key).ToList())
        {
            dict.TryRemove(name, out _);
        }
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
        // 只在"存在非常驻触发器"时才刷：下面的 ClearAll 会清掉 VisionContext 的画布，
        // 进而影响正在绘制的任务标注；而任务运行期间实时触发器已被清空（只剩常驻触发器），
        // 此时没有需要在这里刷新的触发器配置。
        // 注意**不能**断言"任务运行期间字典里一定只剩常驻触发器"——任务内的脚本/任务会走
        // AddTrigger 把 AutoPick/AutoSkip/AutoEat 加回来。本判据只要求"存在可刷新的触发器"，
        // 与上述两种情形都自洽。
        if (TriggerDictionary?.Any(kv => !kv.Value.AlwaysActive) == true)
        {
            TriggerDictionary.GetValueOrDefault("AutoPick")?.Init();
            TriggerDictionary.GetValueOrDefault("AutoSkip")?.Init();
            TriggerDictionary.GetValueOrDefault("AutoFish")?.Init();
            TriggerDictionary.GetValueOrDefault("QuickTeleport")?.Init();
            // TriggerDictionary.GetValueOrDefault("GameLoading")?.Init();
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
        // RecognitionAssets.ClearAll();
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
}
