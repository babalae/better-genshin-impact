using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Infrastructure.Storage;

namespace BetterGenshinImpact.Service;

/// <summary>
/// 登记程序自身的日志、截图、调试图片和用户数据目录。日志与用户数据只展示占用，不提供清理
/// </summary>
public sealed class AppStorageAreaProvider : IStorageAreaProvider
{
    /// <summary>
    /// 任务排查用的调试图片目录（相对程序目录）
    /// </summary>
    private static readonly string[] DebugImageDirectories =
    [
        @"log\DebugMine",
        @"log\RewardRecognition",
        @"log\InventoryCountComparison",
    ];

    /// <inheritdoc />
    public IEnumerable<StorageArea> GetAreas()
    {
        var logDirectory = Global.Absolute("log");

        // 运行日志由 Serilog 自动滚动保留，这里只展示占用
        yield return new StorageArea("diagnostics:log", "运行日志", StorageGroup.Diagnostics, ListLogFiles(logDirectory))
        {
            Description = "自动保留最近 21 天，排查问题时需要",
            Timing = StorageCleanupTiming.None,
        };

        yield return new StorageArea("diagnostics:screenshot", "截图", StorageGroup.Diagnostics,
            [Global.Absolute(@"log\screenshot")])
        {
            Description = "通过截图快捷键保存的图片",
            CleanupEffect = "删除后无法找回",
            RequiresConfirmation = true,
            IncludedInQuickCleanup = false,
        };

        yield return new StorageArea("diagnostics:debug-images", "调试图片", StorageGroup.Diagnostics,
            DebugImageDirectories.Select(Global.Absolute).ToList())
        {
            Description = "挖矿、奖励识别、物品数量比对等任务保存的识别截图",
            CleanupEffect = "只用于排查问题，删除不影响任务运行",
            IncludedInQuickCleanup = false,
        };

        yield return new StorageArea("user:data", "脚本、配置、录制等", StorageGroup.UserData, [Global.Absolute("User")])
        {
            Description = "你的配置、脚本、地图追踪、键鼠录制和任务数据",
            Timing = StorageCleanupTiming.None,
        };
    }

    /// <summary>
    /// 列出 Serilog 生成的主日志文件；目录不存在或无法访问时为空
    /// </summary>
    private static IReadOnlyList<string> ListLogFiles(string logDirectory)
    {
        try
        {
            return Directory.Exists(logDirectory)
                ? Directory.GetFiles(logDirectory, "better-genshin-impact*.log")
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
