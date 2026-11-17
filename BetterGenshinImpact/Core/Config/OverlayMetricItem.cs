using System;
using System.Collections.Generic;
using System.Linq;
using BetterGenshinImpact.Service.I18n;

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
    MemoryUsage,
    BgiMemoryUsage,
    BgiCpuUsage
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
        OverlayMetricItem.MemoryUsage,
        OverlayMetricItem.BgiMemoryUsage,
        OverlayMetricItem.BgiCpuUsage
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
            or OverlayMetricItem.BgiMemoryUsage
            or OverlayMetricItem.MemoryUsage;
    }

    public static string GetDisplayName(OverlayMetricItem item)
    {
        return item switch
        {
            OverlayMetricItem.GameFps => I18nService.Instance.Translate("游戏帧率"),
            OverlayMetricItem.ProcessingCost => I18nService.Instance.Translate("处理耗时"),
            OverlayMetricItem.PeakProcessingCost => I18nService.Instance.Translate("峰值耗时"),
            OverlayMetricItem.CaptureCost => I18nService.Instance.Translate("截图耗时"),
            OverlayMetricItem.TriggerCost => I18nService.Instance.Translate("触发器耗时"),
            OverlayMetricItem.SkippedTicks => I18nService.Instance.Translate("跳过次数"),
            OverlayMetricItem.CpuUsage => I18nService.Instance.Translate("CPU占用"),
            OverlayMetricItem.GpuUsage => I18nService.Instance.Translate("显卡占用"),
            OverlayMetricItem.MemoryUsage => I18nService.Instance.Translate("内存占用"),
            OverlayMetricItem.BgiMemoryUsage => I18nService.Instance.Translate("BGI内存"),
            OverlayMetricItem.BgiCpuUsage => I18nService.Instance.Translate("BGI CPU"),
            _ => item.ToString()
        };
    }

    public static string GetToolTipText(OverlayMetricItem item)
    {
        return item switch
        {
            OverlayMetricItem.GameFps => I18nService.Instance.Translate("游戏当前渲染帧率。"),
            OverlayMetricItem.ProcessingCost => I18nService.Instance.Translate("BetterGI 每轮截图、识别和触发器处理的总耗时。"),
            OverlayMetricItem.PeakProcessingCost => I18nService.Instance.Translate("最近 5 秒内 BetterGI 单轮处理耗时的峰值。"),
            OverlayMetricItem.CaptureCost => I18nService.Instance.Translate("单次获取游戏画面的耗时。"),
            OverlayMetricItem.TriggerCost => I18nService.Instance.Translate("本轮实际执行触发器的总耗时。"),
            OverlayMetricItem.SkippedTicks => I18nService.Instance.Translate("上一轮未结束导致本秒跳过的调度次数。"),
            OverlayMetricItem.CpuUsage => I18nService.Instance.Translate("CPU 总占用率，读取不到时自动隐藏。"),
            OverlayMetricItem.GpuUsage => I18nService.Instance.Translate("原神当前使用显卡的整体占用率，按最繁忙 GPU 引擎计算；不可用时自动隐藏。"),
            OverlayMetricItem.MemoryUsage => I18nService.Instance.Translate("系统内存占用率，读取不到时自动隐藏。"),
            OverlayMetricItem.BgiMemoryUsage => I18nService.Instance.Translate("BetterGI 进程自身占用的私有内存。"),
            OverlayMetricItem.BgiCpuUsage => I18nService.Instance.Translate("BetterGI 进程自身占用的 CPU 使用率。"),
            _ => string.Empty
        };
    }
}
