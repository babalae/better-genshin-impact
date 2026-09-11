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
        TriggerDictionary.TryAdd("GameExceptionPopup", new GameExceptionPopupTrigger());
        TriggerDictionary.TryAdd("NetworkWatchdog", new NetworkWatchdogTrigger());

        return ConvertToTriggerList();
    }

    public static List<ITaskTrigger> ConvertToTriggerList(bool allEnabled = false, bool skipInit = false)
    {
        if (TriggerDictionary is null)
        {
            return [];
        }

        var loadedTriggers = TriggerDictionary.Values.ToList();

        // skipInit 用于"任务启动清空实时触发器"（ClearTriggers）：
        // 常驻触发器要留在列表里继续工作，但绝不能被重新 Init——
        // Init 会重置各自的时间戳与探测状态（看门狗会因此重启 ping 循环、恢复探针的节流被抹掉），
        // 而配置组会为每个项目调用一次 ClearTriggers，反复 Init 会让"连续失败达阈值"的节奏永远攒不满。
        if (!skipInit)
        {
            loadedTriggers.ForEach(i => i.Init());
        }

        if (allEnabled)
        {
            loadedTriggers.ForEach(i => i.IsEnabled = true);
        }

        loadedTriggers = [.. loadedTriggers.OrderByDescending(i => i.Priority)];
        return loadedTriggers;
    }

    /// <summary>
    /// 清空实时触发器（任务启动时调用）。
    /// 常驻触发器（<see cref="ITaskTrigger.AlwaysActive"/>：异常弹窗处理、断网看门狗）必须跨任务存活——
    /// 它们承担"检测游戏异常并自动恢复"的职责，任务运行期间才是最需要它们的场景；
    /// 若一并清掉，任务期间既没有 OnCapture 驱动，也再没有任何时机把它们放回列表。
    /// </summary>
    public static void ClearTriggers()
    {
        // 先固定本地引用：LoadInitialTriggers() 会整体替换 TriggerDictionary，
        // 若每步都重新读静态属性，快照与删除可能落在两个不同的字典上（删错对象）。
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
        if (TriggerDictionary is { Count: > 0 })
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

        // 异常恢复功能的配置变更副作用（与"游戏是否前台""是否有任务在跑"无关，必须在这里做）：
        // 1) 断网看门狗的 ping 循环独立于截图调度器，功能关闭时会自行停表，
        //    配置一改动就确保循环在跑（游戏失焦时调度器根本不截图，没有 OnCapture 可以保活）；
        // 2) 关掉功能要立刻解挂并停掉在途恢复流程——实时待机（没有任务在跑）时不存在
        //    "任务线程等待循环"这条释放路径，挂起会一直留着，而挂起期间调度器会跳过所有
        //    非常驻触发器（自动拾取/自动剧情/钓鱼等静默失效，界面上没有任何提示）。
        if (TaskContext.Instance().IsInitialized)
        {
            var recovery = TaskContext.Instance().Config.OtherConfig.ExceptionRecoveryConfig;

            // 只在功能开启时保活 ping 循环：关闭时拉起一个只会自停的定时器没有意义
            if (recovery.NetworkDetectionEnabled)
            {
                NetworkWatchdogTrigger.EnsureLoopRunning();
            }

            if (!recovery.PopupRecoveryEnabled)
            {
                GameExceptionPopupTrigger.CancelRunningRecovery();

                // 只有没有在途恢复时才清标志：有在途恢复时由它自己的收尾解除挂起
                // （否则"任务已放行、恢复流程还在点击"）。任务线程的等待循环也会做一次同样的
                // "中止 + 有界等待"后再解挂，这里不阻塞 UI 线程。
                GameExceptionPopupTrigger.ResumeRecoverySuspensionIfIdle();

                // 用户主动关闭功能 = 保护状态归零：再次打开时从干净状态开始
                // （跨任务不再自动清零熔断，所以这里给用户一个确定的复位途径）
                GameExceptionPopupTrigger.ResetProtectionState();
            }

            if (!recovery.NetworkDetectionEnabled)
            {
                ExceptionSuspendSignal.ResumeByNetwork();
            }
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
