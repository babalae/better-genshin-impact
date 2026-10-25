using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.GameTask.AutoDomain;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace BetterGenshinImpact.GameTask.Common.Resin;

/// <summary>秘境、幽境、地脉花领奖弹窗的行匹配、点击与档位操作；首领不使用此流程。</summary>
public static class ResinRewardPrompt
{
    /// <summary>解析“使用”按钮的本地化模式；资源文件按 AutoDomainTask 命名，不能更换本地化器类型。</summary>
    public static string ResolveUseButtonPattern(IStringLocalizer<AutoDomainTask> localizer, CultureInfo culture)
        => localizer.WithCultureGet(culture, "使用");

    /// <summary>集中将树脂种类转为中文资源 key，再按原秘境资源解析匹配串。</summary>
    public static string ResolveResinNamePattern(ResinKind kind, IStringLocalizer<AutoDomainTask> localizer, CultureInfo culture)
        => localizer.WithCultureGet(culture, ResinUsageText.DisplayName(kind));

    /// <summary>获取秘境本地化器；三个任务共用现有资源，不创建新的资源包。</summary>
    private static IStringLocalizer<AutoDomainTask> Localizer()
        => App.GetService<IStringLocalizer<AutoDomainTask>>() ?? throw new InvalidOperationException("未初始化秘境本地化服务。");

    /// <summary>检测明确的库存不足提示；不把该提示当作其他树脂行不可用的依据。</summary>
    public static bool HasShortageHint(IReadOnlyList<Region> regions, CultureInfo culture,
        IStringLocalizer<AutoDomainTask>? localizer = null)
    {
        localizer ??= Localizer();
        var insufficient = localizer.WithCultureGet(culture, "数量不足");
        var replenish = localizer.WithCultureGet(culture, "补充原粹树脂");
        return regions.Any(region => Regex.IsMatch(region.Text, insufficient) || Regex.IsMatch(region.Text, replenish)
            || (culture.TwoLetterISOLanguageName == "zh" && region.Text.Contains("补充", StringComparison.Ordinal)));
    }

    /// <summary>按行识别树脂与右侧“使用”按钮；仅出现“补充”的原粹行不属于可领奖选项。</summary>
    /// <param name="regionList">弹窗 OCR 结果，可包含被拆分的名称和数量。</param>
    /// <param name="buttonMinX">使用按钮所在右半区的最小横坐标。</param>
    /// <param name="culture">游戏语言。</param>
    /// <param name="localizer">原秘境资源本地化器，测试可注入替身。</param>
    public static IReadOnlySet<ResinKind> DetectOffered(IReadOnlyList<Region> regionList, double buttonMinX,
        CultureInfo culture, IStringLocalizer<AutoDomainTask>? localizer = null)
    {
        localizer ??= Localizer();
        return ResinKinds.InOrder.Where(kind => FindUseRow(regionList, kind, buttonMinX, localizer, culture).Button is not null).ToHashSet();
    }

    /// <summary>匹配树脂名称和同行按钮；同行拼接也兼容 OCR 将名称与消耗量拆开的情况。</summary>
    private static (Region? Button, string Text) FindUseRow(IReadOnlyList<Region> regions, ResinKind kind,
        double buttonMinX, IStringLocalizer<AutoDomainTask> localizer, CultureInfo culture)
    {
        var use = ResolveUseButtonPattern(localizer, culture);
        var resin = ResolveResinNamePattern(kind, localizer, culture);
        foreach (var button in regions.Where(region => region.X > buttonMinX && Regex.IsMatch(region.Text, use)))
        {
            var text = string.Concat(regions.Where(region => IsHeightOverlap(region, button)).OrderBy(region => region.X).Select(region => region.Text));
            if (Regex.IsMatch(text, resin)) return (button, text);
        }
        return (null, string.Empty);
    }

    /// <summary>在完整截图中识别并点击指定树脂的使用按钮。</summary>
    /// <returns>是否成功点击及原粹实际点数；非原粹的点数为 0。</returns>
    public static (bool success, int originalSpent) PressUseResin(GameRuntime runtime, ImageRegion capture,
        ResinKind kind, ILogger logger, string logPrefix, CancellationToken ct, CultureInfo culture)
    {
        var regions = capture.FindMulti(RecognitionObject.Ocr(capture.Width * 0.25, capture.Height * 0.2, capture.Width * 0.5, capture.Height * 0.6));
        return PressUseResin(runtime, regions, kind, logger, logPrefix, ct, culture);
    }

    /// <summary>在已识别的弹框文字中点击同行“使用”；保留原双击间隔，并等待游戏响应后再由调用方识图。</summary>
    public static (bool success, int originalSpent) PressUseResin(GameRuntime runtime, IReadOnlyList<Region> regions,
        ResinKind kind, ILogger logger, string logPrefix, CancellationToken ct, CultureInfo culture)
    {
        var row = FindUseRow(regions, kind, runtime.SystemInfo.ScaleMax1080PCaptureRect.Width / 2d, Localizer(), culture);
        if (row.Button is null)
        {
            logger.LogWarning("{LogPrefix}：未找到 {ResinName} 的使用按键", logPrefix, ResinUsageText.DisplayName(kind));
            return (false, 0);
        }
        var spent = GetResinNum(new Region { Text = row.Text }, kind, logger, logPrefix);
        // 解决水龙王按下左键后没松开，然后后续点击按下就没反应了。使用双击。
        row.Button.Click();
        runtime.Sleep(60, ct);
        row.Button.Click();
        runtime.Sleep(60, ct);
        logger.LogInformation("{LogPrefix}：使用 {ResinName}，消耗：{Num}", logPrefix, ResinUsageText.DisplayName(kind), kind == ResinKind.Original ? spent : 1);
        return (true, spent);
    }

    /// <summary>读取同行原粹消耗；无法识别时沿用原逻辑默认 20，其他树脂按个数由选择器记账。</summary>
    public static int GetResinNum(Region region, ResinKind kind, ILogger logger, string logPrefix)
    {
        if (kind != ResinKind.Original) return 0;
        if (Regex.IsMatch(region.Text, @"(?<!\d)20(?!\d)")) return 20;
        if (Regex.IsMatch(region.Text, @"(?<!\d)40(?!\d)")) return 40;
        logger.LogWarning("{LogPrefix}：未识别到原粹树脂消耗数量，默认按 20 计算", logPrefix);
        return 20;
    }

    /// <summary>判断两个区域在垂直方向上是否有重叠。</summary>
    public static bool IsHeightOverlap(Region region1, Region region2)
    {
        // 检查区域是否在垂直方向上重叠。
        return region1.Top <= region2.Bottom && region1.Bottom >= region2.Top;
    }

    /// <summary>复用原秘境模板切换 20/40 档位；每次点击后至少等待原有的 500 ms 重试间隔。</summary>
    /// <param name="runtime">本次运行环境。</param>
    /// <param name="expectedNum">目标档位，20 或 40。</param>
    /// <param name="logger">任务日志。</param>
    /// <param name="logPrefix">任务名称。</param>
    /// <param name="ct">取消令牌。</param>
    public static bool SwitchOriginalTier(GameRuntime runtime, int expectedNum, ILogger logger, string logPrefix, CancellationToken ct)
    {
        var culture = new CultureInfo(ConfigService.Config!.OtherConfig.GameCultureInfoName);
        var pattern = ResolveResinNamePattern(ResinKind.Original, Localizer(), culture);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var capture = runtime.CaptureToRectArea();
            var regions = capture.FindMulti(RecognitionObject.Ocr(capture.Width * 0.25, capture.Height * 0.2, capture.Width * 0.5, capture.Height * 0.6));
            var name = regions.FirstOrDefault(region => Regex.IsMatch(region.Text, pattern));
            var line = name is null ? string.Empty : string.Concat(regions.Where(region => IsHeightOverlap(region, name)).Select(region => region.Text));
            if (Regex.IsMatch(line, $@"(?<!\d){expectedNum}(?!\d)")) return true;

            // 切换 20/40 原粹树脂的按钮是暗的时，不能按成功处理。
            using var disabled = capture.Find(RecognitionAssets.Get("AutoDomain", "ResinSwitchBtnNoActive", capture.Width, capture.Height));
            if (disabled.IsExist())
            {
                logger.LogWarning("{LogPrefix}：原粹档位按钮不可用，目标：{Num}", logPrefix, expectedNum);
                return false;
            }
            // 切换 20/40 原粹树脂的按钮是亮的时，点击后重新截图核对，不能用点击前画面确认切档。
            using var clickable = capture.Find(RecognitionAssets.Get("AutoDomain", "ResinSwitchBtn", capture.Width, capture.Height));
            if (clickable.IsExist()) clickable.Click();
            runtime.Sleep(500, ct);
        }
        logger.LogWarning("{LogPrefix}：切换原粹树脂档位重试超时，目标：{Num}", logPrefix, expectedNum);
        return false;
    }
}
