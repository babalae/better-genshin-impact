using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.Core.Config;

public enum OverlayMetricItem
{
    GameFps,
    ProcessingCost,
    PeakProcessingCost,
    CaptureCost,
    TriggerCost,
    SkippedTicks,
    CpuUsage,
    GpuUsage,
    MemoryUsage
}

public static class OverlayMetricItemDefaults
{
    // 显式顺序同时决定遮罩三行三列显示顺序和设置页复选框顺序，不要随手改回 Enum.GetValues。
    public static IReadOnlyList<OverlayMetricItem> AllItems { get; } =
    [
        OverlayMetricItem.GameFps,
        OverlayMetricItem.ProcessingCost,
        OverlayMetricItem.PeakProcessingCost,
        OverlayMetricItem.CaptureCost,
        OverlayMetricItem.TriggerCost,
        OverlayMetricItem.SkippedTicks,
        OverlayMetricItem.GpuUsage,
        OverlayMetricItem.CpuUsage,
        OverlayMetricItem.MemoryUsage
    ];

    public static Dictionary<string, bool> CreateDefaultItems()
    {
        return AllItems.ToDictionary(item => item.ToString(), IsEnabledByDefault);
    }

    public static bool IsEnabledByDefault(OverlayMetricItem item)
    {
        // 默认只开启低风险的常用实时指标；硬件传感器项由用户按需开启，读不到时也不会占位显示。
        return item is OverlayMetricItem.GameFps
            or OverlayMetricItem.ProcessingCost
            or OverlayMetricItem.PeakProcessingCost
            or OverlayMetricItem.CaptureCost
            or OverlayMetricItem.SkippedTicks
            or OverlayMetricItem.MemoryUsage;
    }

    public static string GetDisplayName(OverlayMetricItem item)
    {
        return item switch
        {
            OverlayMetricItem.GameFps => "游戏帧率",
            OverlayMetricItem.ProcessingCost => "处理耗时",
            OverlayMetricItem.PeakProcessingCost => "峰值耗时",
            OverlayMetricItem.CaptureCost => "截图耗时",
            OverlayMetricItem.TriggerCost => "触发器耗时",
            OverlayMetricItem.SkippedTicks => "跳过次数",
            OverlayMetricItem.CpuUsage => "CPU占用",
            OverlayMetricItem.GpuUsage => "显卡占用",
            OverlayMetricItem.MemoryUsage => "内存占用",
            _ => item.ToString()
        };
    }

    public static string GetToolTipText(OverlayMetricItem item)
    {
        return item switch
        {
            OverlayMetricItem.GameFps => "游戏当前渲染帧率。",
            OverlayMetricItem.ProcessingCost => "BetterGI 每轮截图、识别和触发器处理的总耗时。",
            OverlayMetricItem.PeakProcessingCost => "最近 5 秒内 BetterGI 单轮处理耗时的峰值。",
            OverlayMetricItem.CaptureCost => "单次获取游戏画面的耗时。",
            OverlayMetricItem.TriggerCost => "本轮实际执行触发器的总耗时。",
            OverlayMetricItem.SkippedTicks => "上一轮未结束导致本秒跳过的调度次数。",
            OverlayMetricItem.CpuUsage => "CPU 总占用率，读取不到时自动隐藏。",
            OverlayMetricItem.GpuUsage => "显卡核心占用率，读取不到时自动隐藏。",
            OverlayMetricItem.MemoryUsage => "系统内存占用率，读取不到时自动隐藏。",
            _ => string.Empty
        };
    }
}
