using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoBoss.Assets;
using BetterGenshinImpact.GameTask.AutoFight;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoFight.Script;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Notification;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoBoss;

/// <summary>
/// 自动首领讨伐独立任务，负责按配置前往 Boss、执行战斗策略、领取征讨之花奖励并处理重定位。
/// </summary>
public class AutoBossTask : ISoloTask
{
    public string Name => "自动首领讨伐";

    private readonly ILogger<AutoBossTask> _logger = App.GetLogger<AutoBossTask>();
    private readonly AutoBossParam _taskParam;
    private readonly CombatScriptBag _combatScriptBag;
    private readonly ReturnMainUiTask _returnMainUiTask = new();
    private SwitchPartyTask? _switchPartyTask;
    private CancellationToken _ct;

    private static readonly TimeSpan OriginalResinRecoveryInterval = TimeSpan.FromMinutes(8);
    private const int MaxQuickUseQuantity = 20;

    private string PathingAssetFolder => Global.Absolute(@"GameTask\AutoBoss\Assets\Pathing");

    private double AssetScale => TaskContext.Instance().SystemInfo.AssetScale;

    private sealed record OriginalResinInfo(int Count, int Limit);

    private sealed record SupplementalResinOption(string Name, RecognitionObject Template);

    /// <summary>
    /// 创建自动首领讨伐任务，并根据任务参数预解析战斗策略。
    /// </summary>
    /// <param name="taskParam">自动首领讨伐参数，包含 Boss、队伍、战斗策略、讨伐次数和补充树脂开关。</param>
    public AutoBossTask(AutoBossParam taskParam)
    {
        _taskParam = taskParam;
        if (string.IsNullOrWhiteSpace(_taskParam.CombatStrategyPath))
        {
            _taskParam.SetCombatStrategyPath(_taskParam.StrategyName);
        }

        _combatScriptBag = CombatScriptParser.ReadAndParse(_taskParam.CombatStrategyPath);
    }

    /// <summary>
    /// 启动自动首领讨伐任务，包含参数校验、分辨率校验、死亡重试和最终输入状态释放。
    /// </summary>
    /// <param name="ct">任务取消令牌。</param>
    public async Task Start(CancellationToken ct)
    {
        _ct = ct;
        Validate();
        LogScreenResolution();

        Notify.Event("AutoBoss").Success($"{Name}启动");
        try
        {
            var retryCount = 0;
            while (true)
            {
                try
                {
                    await RunBossLoop();
                    break;
                }
                catch (RetryException e) when (retryCount < _taskParam.ReviveRetryCount)
                {
                    retryCount++;
                    _logger.LogWarning("{Name}：第 {Retry}/{MaxRetry} 次重试当前首领讨伐，原因：{Reason}", Name, retryCount, _taskParam.ReviveRetryCount, e.Message);
                    await Delay(2000, _ct);
                }
                catch (RetryException e)
                {
                    _logger.LogWarning("{Name}：角色死亡后重试次数已达上限 {MaxRetry}，结束任务，原因：{Reason}", Name, _taskParam.ReviveRetryCount, e.Message);
                    throw;
                }
            }
        }
        finally
        {
            Simulation.ReleaseAllKey();
            Simulation.SendInput.Mouse.LeftButtonUp();
            Notify.Event("AutoBoss").Success($"{Name}结束");
        }
    }

    /// <summary>
    /// 执行完整讨伐循环：准备环境、前往首领、战斗、寻找奖励、领奖并决定是否继续下一轮。
    /// </summary>
    private async Task RunBossLoop()
    {
        await Prepare();

        var rewardCount = 0;
        var shouldNavigateToBoss = true;
        while (ShouldContinueBeforeRound(rewardCount))
        {
            _ct.ThrowIfCancellationRequested();
            _logger.LogInformation("{Name}：开始第 {Round} 次讨伐 {Boss}", Name, rewardCount + 1, _taskParam.BossName);

            if (!await EnsureResinBeforeRound())
            {
                _logger.LogInformation("{Name}：原粹树脂不足或补充失败，结束任务", Name);
                break;
            }

            if (shouldNavigateToBoss)
            {
                await NavigateToBoss();
            }

            await RunAutoFight();
            await NavigateToReward();

            var rewardSuccess = await TakeReward();
            if (!rewardSuccess)
            {
                _logger.LogInformation("{Name}：原粹树脂不足或无法领取奖励，结束任务", Name);
                break;
            }

            rewardCount++;
            if (!ShouldContinueBeforeRound(rewardCount))
            {
                break;
            }

            if (_taskParam.ReturnToStatueAfterEachRound)
            {
                _logger.LogInformation("{Name}：返回七天神像", Name);
                await new TpTask(_ct).TpToStatueOfTheSeven();
                await Delay(3000, _ct);
                shouldNavigateToBoss = true;
            }
            else
            {
                await RepositionAfterFight();
                shouldNavigateToBoss = false;
            }
        }
    }

    /// <summary>
    /// 判断本轮开始前是否还有继续讨伐的次数。
    /// </summary>
    /// <param name="rewardCount">本次任务已成功领取奖励的次数。</param>
    /// <returns>树脂耗尽模式始终返回 true；指定次数模式下未达到目标次数时返回 true。</returns>
    private bool ShouldContinueBeforeRound(int rewardCount)
    {
        return !_taskParam.SpecifyRunCount || rewardCount < _taskParam.RunCount;
    }

    /// <summary>
    /// 战前预检原粹树脂是否足够领取一次世界 Boss 奖励；必要时尝试使用已开启的补充树脂入口。
    /// </summary>
    /// <returns>树脂足够或预检失败需要交由领奖弹窗兜底时返回 true；确认不足且无法补充时返回 false。</returns>
    private async Task<bool> EnsureResinBeforeRound()
    {
        try
        {
            await OpenBigMapForResinCheck();

            OriginalResinInfo originalResin;
            try
            {
                originalResin = await RecognizeOriginalResinInfoFromBigMap();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogWarning("{Name}：战前原粹树脂预检失败，将继续通过领奖界面兜底，原因：{Reason}", Name, e.Message);
                return true;
            }

            if (originalResin.Count >= 40)
            {
                return true;
            }

            _logger.LogInformation("{Name}：当前原粹树脂 {Count}/{Limit}，不足以领取首领奖励", Name, originalResin.Count, originalResin.Limit);
            if (!_taskParam.SpecifyRunCount)
            {
                return false;
            }

            if (!_taskParam.UseTransientResin && !_taskParam.UseFragileResin)
            {
                _logger.LogInformation("{Name}：指定讨伐次数模式未开启补充树脂，停止讨伐", Name);
                return false;
            }

            if (await TryUseSupplementalResinBeforeRound(originalResin))
            {
                return true;
            }

            _logger.LogInformation("{Name}：补充树脂未成功，停止讨伐", Name);
            return false;
        }
        finally
        {
            await _returnMainUiTask.Start(_ct);
        }
    }

    /// <summary>
    /// 使用用户配置的打开地图按键进入大地图，识别右上角原粹树脂数量，并在结束后回到主界面。
    /// </summary>
    /// <returns>识别到的原粹树脂数量；识别失败时返回 null。</returns>
    private async Task<int?> TryRecognizeOriginalResinCountInBigMap()
    {
        try
        {
            await OpenBigMapForResinCheck();
            try
            {
                var originalResin = await RecognizeOriginalResinInfoFromBigMap();
                return originalResin.Count;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogWarning("{Name}：战前原粹树脂预检失败，将继续通过领奖界面兜底，原因：{Reason}", Name, e.Message);
                return null;
            }
        }
        finally
        {
            await _returnMainUiTask.Start(_ct);
        }
    }

    /// <summary>
    /// 释放输入并打开大地图界面，用于在右上角读取原粹树脂数量。
    /// </summary>
    private async Task OpenBigMapForResinCheck()
    {
        await new TpTask(_ct).OpenBigMapUi();
    }

    /// <summary>
    /// AutoBoss 专用大地图原粹树脂识别：点击右上角树脂图标后，通过全部恢复时间反推剩余树脂。
    /// </summary>
    /// <returns>当前剩余原粹树脂。</returns>
    private async Task<OriginalResinInfo> RecognizeOriginalResinInfoFromBigMap()
    {
        using var capture = CaptureToRectArea();
        using var resinIconSearchRegion = capture.DeriveCrop(ScaleRect(1200, 25, 250, 50));
        var resinIconRegion = resinIconSearchRegion.Find(AutoBossAssets.Instance.OriginalResinTopIconRo);
        if (resinIconRegion.IsEmpty())
        {
            throw new InvalidOperationException("未找到原粹树脂图标");
        }

        var iconLeft = resinIconSearchRegion.X + resinIconRegion.Left;
        var iconRight = resinIconSearchRegion.X + resinIconRegion.Right;
        var iconBottom = resinIconSearchRegion.Y + resinIconRegion.Bottom;

        resinIconRegion.Click();
        await Delay(500, _ct);

        using var clickedCapture = CaptureToRectArea();
        var resinLimit = RecognizeOriginalResinLimit(clickedCapture, iconRight);
        var fullRecoveryTime = RecognizeFullRecoveryTime(clickedCapture, iconLeft, iconBottom);
        var missingResin = (int)Math.Ceiling(fullRecoveryTime.TotalSeconds / OriginalResinRecoveryInterval.TotalSeconds);
        if (missingResin > resinLimit)
        {
            throw new InvalidOperationException($"计算缺失树脂 {missingResin} 超过树脂上限 {resinLimit}");
        }

        var originalResin = resinLimit - missingResin;
        _logger.LogInformation("{Name}：剩余树脂 {Count}",Name, originalResin);
        return new OriginalResinInfo(originalResin, resinLimit);
    }

    /// <summary>
    /// 按原右上角 OCR 区域读取当前/上限文本，并取数字串最后三位作为树脂上限。
    /// </summary>
    private int RecognizeOriginalResinLimit(ImageRegion capture, int resinIconRight)
    {
        var countRect = new Rect(resinIconRight + ScaleX(25), ScaleY(37), ScaleX(110), ScaleY(24));
        using var countRegion = capture.DeriveCrop(countRect);
        var countText = OcrFactory.Paddle.OcrWithoutDetector(countRegion.SrcMat);
        var digits = Regex.Replace(StringUtils.ConvertFullWidthNumToHalfWidth(countText), @"\D", "");
        if (digits.Length < 3)
        {
            throw new FormatException($"原粹树脂上限 OCR 失败：{countText}");
        }

        var limitText = digits[^3..];
        if (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out var resinLimit) || resinLimit <= 0)
        {
            throw new FormatException($"原粹树脂上限解析失败：{countText}");
        }

        _logger.LogDebug("{Name}：原粹树脂上限 OCR：{Text} -> {Limit}", Name, countText, resinLimit);
        return resinLimit;
    }

    /// <summary>
    /// 读取树脂详情弹窗中的全部恢复时间；已完全恢复时返回零时长。
    /// </summary>
    private TimeSpan RecognizeFullRecoveryTime(ImageRegion capture, int resinIconLeft, int resinIconBottom)
    {
        // 该偏移来自截图实际像素，不随 AssetScale 缩放。
        var detailRect = new Rect(
            resinIconLeft - 23,
            resinIconBottom + 29,
            220,
            150);
        using var detailRegion = capture.DeriveCrop(detailRect);
        var result = OcrFactory.Paddle.OcrResult(detailRegion.SrcMat);
        var text = string.Concat(result.Regions
            .OrderBy(region => region.Rect.Center.Y)
            .ThenBy(region => region.Rect.Center.X)
            .Select(region => region.Text));
        var normalizedText = NormalizeRecoveryText(text);
        _logger.LogDebug("{Name}：原粹树脂恢复详情 OCR：{Text}", Name, normalizedText);

        return ExtractFullRecoveryTime(normalizedText);
    }

    private static string NormalizeRecoveryText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = StringUtils.ConvertFullWidthNumToHalfWidth(text).Replace('：', ':');
        return Regex.Replace(normalized, @"\s+", "");
    }

    private static TimeSpan ExtractFullRecoveryTime(string text)
    {
        if (text.Contains("原粹树脂已完全恢复", StringComparison.Ordinal))
        {
            return TimeSpan.Zero;
        }

        var fullRecoveryMatch = Regex.Match(text, @"全部恢复(?<time>\d{1,3}:\d{2}:\d{2})");
        if (fullRecoveryMatch.Success)
        {
            return ParseRecoveryTime(fullRecoveryMatch.Groups["time"].Value);
        }

        var timeMatches = Regex.Matches(text, @"\d{1,3}:\d{2}:\d{2}");
        if (timeMatches.Count == 0)
        {
            throw new FormatException($"未识别到全部恢复时间：{text}");
        }

        return ParseRecoveryTime(timeMatches[timeMatches.Count - 1].Value);
    }

    private static TimeSpan ParseRecoveryTime(string timeText)
    {
        var parts = timeText.Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || minutes < 0
            || minutes > 59
            || seconds < 0
            || seconds > 59)
        {
            throw new FormatException($"树脂恢复时间格式无效：{timeText}");
        }

        return new TimeSpan(hours, minutes, seconds);
    }

    private static string FormatRecoveryTime(TimeSpan time)
    {
        return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}";
    }

    /// <summary>
    /// 当前处于大地图树脂详情弹窗时，尝试使用须臾或脆弱树脂补充原粹树脂。
    /// </summary>
    private async Task<bool> TryUseSupplementalResinBeforeRound(OriginalResinInfo originalResin)
    {
        var page = new BvPage(_ct);
        var currentResin = originalResin;
        var unavailableResins = new HashSet<string>(StringComparer.Ordinal);

        while (currentResin.Count < 40)
        {
            var targetQuantity = CalculateSupplementalResinTargetQuantity(currentResin);
            if (targetQuantity <= 0)
            {
                _logger.LogInformation("{Name}：当前原粹树脂 {Count}/{Limit}，使用任意补充树脂都会超过上限，停止补充", Name, currentResin.Count, currentResin.Limit);
                return false;
            }

            if (!await TryOpenResinSupplementPane(page))
            {
                return false;
            }

            var usedAnyResin = false;
            foreach (var resin in BuildSupplementalResinOptions())
            {
                if (unavailableResins.Contains(resin.Name))
                {
                    continue;
                }

                if (!await TrySelectSupplementalResin(page, resin))
                {
                    unavailableResins.Add(resin.Name);
                    continue;
                }

                var usedQuantity = await TryUseSelectedSupplementalResin(page, resin, targetQuantity);
                if (usedQuantity <= 0)
                {
                    return false;
                }

                usedAnyResin = true;
                _logger.LogInformation("{Name}：已使用 {Quantity} 个{ResinName}补充原粹树脂", Name, usedQuantity, resin.Name);
                await Delay(800, _ct);

                try
                {
                    currentResin = await RecognizeOriginalResinInfoFromBigMap();
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _logger.LogWarning("{Name}：补充后重新识别原粹树脂失败，停止讨伐，原因：{Reason}", Name, e.Message);
                    return false;
                }

                _logger.LogInformation("{Name}：补充后原粹树脂 {Count}/{Limit}", Name, currentResin.Count, currentResin.Limit);
                if (currentResin.Count >= 40)
                {
                    return true;
                }

                break;
            }

            if (!usedAnyResin)
            {
                _logger.LogInformation("{Name}：未找到可用的须臾树脂或脆弱树脂", Name);
                return false;
            }
        }

        return true;
    }

    private List<SupplementalResinOption> BuildSupplementalResinOptions()
    {
        var options = new List<SupplementalResinOption>();
        if (_taskParam.UseTransientResin)
        {
            options.Add(new SupplementalResinOption("须臾树脂", AutoBossAssets.Instance.TransientResinInSupplementPaneRo));
        }

        if (_taskParam.UseFragileResin)
        {
            options.Add(new SupplementalResinOption("脆弱树脂", AutoBossAssets.Instance.FragileResinInSupplementPaneRo));
        }

        return options;
    }

    private static int CalculateSupplementalResinTargetQuantity(OriginalResinInfo originalResin)
    {
        return Math.Max(0, (originalResin.Limit - originalResin.Count) / 60);
    }

    private async Task<bool> TryOpenResinSupplementPane(BvPage page)
    {
        var titleRect = ScaleRect(834, 247, 256, 60);
        var titleLocator = page.Locator("补充原粹树脂", titleRect).WithRetryInterval(300);
        if ((await titleLocator.TryWaitFor(500)).Count > 0)
        {
            return true;
        }

        var openButtonLocator = page.Locator(AutoBossAssets.Instance.OpenResinSupplementPaneButtonRo)
            .WithRoi(ScaleRect(1200, 25, 250, 50))
            .WithRetryInterval(300);

        try
        {
            await titleLocator
                .WithRetryAction(_ =>
                {
                    var buttons = openButtonLocator.FindAll();
                    if (buttons.Count > 0)
                    {
                        buttons[0].Click();
                    }
                })
                .WaitFor(5000);
            return true;
        }
        catch (TimeoutException e)
        {
            _logger.LogWarning("{Name}：未能打开补充原粹树脂面板，原因：{Reason}", Name, e.Message);
            return false;
        }
    }

    private async Task<bool> TrySelectSupplementalResin(BvPage page, SupplementalResinOption resin)
    {
        var iconLocator = page.Locator(resin.Template)
            .WithRoi(ScaleRect(644, 378, 620, 192))
            .WithRetryInterval(300);

        var icons = await iconLocator.TryWaitFor(1500);
        if (icons.Count == 0)
        {
            _logger.LogInformation("{Name}：补充面板未找到{ResinName}图标，视为不可用", Name, resin.Name);
            return false;
        }

        icons[0].Click();
        try
        {
            await page.Locator(resin.Name, ScaleRect(906, 587, 110, 31))
                .WithRetryInterval(300)
                .WithRetryAction(_ =>
                {
                    var currentIcons = iconLocator.FindAll();
                    if (currentIcons.Count > 0)
                    {
                        currentIcons[0].Click();
                    }
                })
                .WaitFor(3000);
            return true;
        }
        catch (TimeoutException e)
        {
            _logger.LogWarning("{Name}：未能确认已选中{ResinName}，原因：{Reason}", Name, resin.Name, e.Message);
            return false;
        }
    }

    private async Task<int> TryUseSelectedSupplementalResin(BvPage page, SupplementalResinOption resin, int targetQuantity)
    {
        if (targetQuantity <= 0)
        {
            return 0;
        }

        if (!await TryClickTextButton(page, "使用", ScaleRect(1163, 761, 61, 38), 3000))
        {
            return 0;
        }

        await Delay(500, _ct);
        var quickUseRegions = await page.Locator("快捷使用", ScaleRect(875, 269, 184, 63)).TryWaitFor(1500);
        if (quickUseRegions.Count > 0)
        {
            return await TryUseQuickSupplementalResin(page, resin, targetQuantity);
        }

        if (await TryCloseSupplementObtainDialog(page))
        {
            return 1;
        }

        _logger.LogWarning("{Name}：点击{ResinName}使用后未识别到快捷使用或获得界面", Name, resin.Name);
        return 0;
    }

    /// <summary>
    /// 在快捷使用面板中按目标数量使用补充树脂，最多一次使用 <see cref="MaxQuickUseQuantity"/> 个。
    /// </summary>
    /// <param name="page">当前视觉定位页面。</param>
    /// <param name="resin">待使用的补充树脂选项。</param>
    /// <param name="targetQuantity">本轮希望使用的树脂数量。</param>
    /// <returns>成功使用的数量；失败或无法识别时返回 0。</returns>
    private async Task<int> TryUseQuickSupplementalResin(BvPage page, SupplementalResinOption resin, int targetQuantity)
    {
        var availableQuantity = RecognizeQuickUseAvailableCount();
        var targetQuickQuantity = Math.Min(Math.Min(targetQuantity, availableQuantity), MaxQuickUseQuantity);
        if (targetQuickQuantity <= 0)
        {
            _logger.LogWarning("{Name}：快捷使用面板未识别到{ResinName}可用数量", Name, resin.Name);
            return 0;
        }

        var actualQuantity = await AdjustQuickUseQuantity(page, targetQuickQuantity);
        if (actualQuantity <= 0 || actualQuantity > targetQuickQuantity)
        {
            _logger.LogWarning("{Name}：快捷使用{ResinName}数量无效，目标 {TargetQuantity}，识别 {ActualQuantity}", Name, resin.Name, targetQuickQuantity, actualQuantity);
            return 0;
        }

        if (!await TryClickTextButton(page, "使用", ScaleRect(1152, 740, 64, 35), 3000))
        {
            return 0;
        }

        if (!await TryCloseSupplementObtainDialog(page))
        {
            return 0;
        }

        return actualQuantity;
    }

    /// <summary>
    /// 识别快捷使用面板中的可用数量，并限制到单次快捷使用上限。
    /// </summary>
    /// <returns>可用于本次快捷使用的数量，上限为 <see cref="MaxQuickUseQuantity"/>。</returns>
    private int RecognizeQuickUseAvailableCount()
    {
        var text = NormalizeSupplementText(RecognizeTextWithoutDetector(ScaleRect(1191, 633, 72, 29)));
        var matches = Regex.Matches(text, @"\d+");
        var maxValue = 0;
        foreach (Match match in matches)
        {
            if (int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                maxValue = Math.Max(maxValue, value);
            }
        }

        var availableQuantity = Math.Min(maxValue, MaxQuickUseQuantity);
        _logger.LogDebug("{Name}：快捷使用可用数量 OCR：{Text} -> {Quantity}", Name, text, availableQuantity);
        return availableQuantity;
    }

    /// <summary>
    /// 通过点击加号把快捷使用数量调整到目标值，无法继续增加时返回当前识别数量。
    /// </summary>
    /// <param name="page">当前视觉定位页面。</param>
    /// <param name="targetQuantity">目标快捷使用数量。</param>
    /// <returns>最终识别到的快捷使用数量。</returns>
    private async Task<int> AdjustQuickUseQuantity(BvPage page, int targetQuantity)
    {
        var increaseLocator = page.Locator(AutoBossAssets.Instance.IncreaseResinUsageQuantityButtonRo)
            .WithRoi(ScaleRect(1265, 620, 59, 55))
            .WithRetryInterval(500);

        var actualQuantity = 0;
        var lastQuantity = -1;
        var unchangedTimes = 0;
        var retryTimes = Math.Max(6, targetQuantity * 3);
        var completed = await NewRetry.WaitForAction(() =>
        {
            var quantity = TryRecognizeQuickUseQuantity();
            if (quantity == null)
            {
                return false;
            }

            actualQuantity = quantity.Value;
            if (actualQuantity >= targetQuantity)
            {
                return true;
            }

            if (actualQuantity == lastQuantity)
            {
                unchangedTimes++;
            }
            else
            {
                unchangedTimes = 0;
            }

            lastQuantity = actualQuantity;
            if (unchangedTimes >= 3)
            {
                return true;
            }

            var increaseButtons = increaseLocator.FindAll();
            if (increaseButtons.Count == 0)
            {
                return true;
            }

            increaseButtons[0].Click();
            return false;
        }, _ct, retryTimes, 500);

        if (!completed)
        {
            _logger.LogDebug("{Name}：快捷使用数量调整超时，目标 {TargetQuantity}，当前 {ActualQuantity}", Name, targetQuantity, actualQuantity);
        }

        return actualQuantity;
    }

    /// <summary>
    /// 从快捷使用面板 OCR 文本中解析当前使用数量。
    /// </summary>
    /// <returns>成功解析时返回当前数量；文本不完整或解析失败时返回 null。</returns>
    private int? TryRecognizeQuickUseQuantity()
    {
        var text = NormalizeSupplementText(RecognizeSortedOcrText(ScaleRect(915, 540, 93, 81)));
        if (!text.Contains("使用数量", StringComparison.Ordinal))
        {
            _logger.LogDebug("{Name}：快捷使用数量 OCR 未包含使用数量：{Text}", Name, text);
            return null;
        }

        var match = Regex.Match(text, @"使用数量\D*(\d+)");
        if (!match.Success)
        {
            match = Regex.Match(text, @"\d+");
        }

        var quantityText = match.Groups.Count > 1 && match.Groups[1].Success ? match.Groups[1].Value : match.Value;
        if (!match.Success || !int.TryParse(quantityText, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity))
        {
            _logger.LogDebug("{Name}：快捷使用数量 OCR 解析失败：{Text}", Name, text);
            return null;
        }

        _logger.LogDebug("{Name}：快捷使用数量 OCR：{Text} -> {Quantity}", Name, text, quantity);
        return quantity;
    }

    private async Task<bool> TryCloseSupplementObtainDialog(BvPage page)
    {
        try
        {
            var obtainRegion = (await page.Locator("获得", ScaleRect(924, 279, 74, 38)).WaitFor(5000)).First();
            obtainRegion.Click();
            await Delay(800, _ct);
            return true;
        }
        catch (TimeoutException e)
        {
            _logger.LogWarning("{Name}：未识别到补充树脂获得界面，原因：{Reason}", Name, e.Message);
            return false;
        }
    }

    private async Task<bool> TryClickTextButton(BvPage page, string text, Rect rect, int timeout)
    {
        try
        {
            await page.Locator(text, rect).Click(timeout);
            return true;
        }
        catch (TimeoutException e)
        {
            _logger.LogWarning("{Name}：未能点击文本按钮 {Text}，原因：{Reason}", Name, text, e.Message);
            return false;
        }
    }

    private string RecognizeTextWithoutDetector(Rect rect)
    {
        using var capture = CaptureToRectArea();
        using var region = capture.DeriveCrop(rect);
        return OcrFactory.Paddle.OcrWithoutDetector(region.SrcMat);
    }

    private string RecognizeSortedOcrText(Rect rect)
    {
        using var capture = CaptureToRectArea();
        using var region = capture.DeriveCrop(rect);
        var result = OcrFactory.Paddle.OcrResult(region.SrcMat);
        return string.Concat(result.Regions
            .OrderBy(r => r.Rect.Center.Y)
            .ThenBy(r => r.Rect.Center.X)
            .Select(r => r.Text));
    }

    private static string NormalizeSupplementText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = StringUtils.ConvertFullWidthNumToHalfWidth(text)
            .Replace('：', ':')
            .Replace('﹕', ':');
        return Regex.Replace(normalized, @"\s+", "");
    }

    /// <summary>
    /// 多次尝试识别当前队伍角色并初始化战斗场景。
    /// </summary>
    /// <returns>已成功识别队伍的战斗场景。</returns>
    /// <exception cref="Exception">连续多次识别队伍失败时抛出。</exception>
    private CombatScenes GetCombatScenesWithRetry()
    {
        const int maxRetries = 5;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            _ct.ThrowIfCancellationRequested();
            var combatScenes = new CombatScenes().InitializeTeam(CaptureToRectArea());
            if (combatScenes.CheckTeamInitialized())
            {
                return combatScenes;
            }

            if (attempt < maxRetries)
            {
                Sleep(1000, _ct);
            }
        }

        throw new Exception("识别队伍角色失败（已重试 5 次）");
    }

    /// <summary>
    /// 根据当前队伍匹配战斗脚本，并切换到脚本中的首个角色。
    /// </summary>
    /// <param name="combatScenes">已初始化的战斗场景。</param>
    /// <returns>匹配到的战斗指令列表。</returns>
    /// <exception cref="Exception">没有可用战斗脚本时抛出。</exception>
    private List<CombatCommand> FindCombatScriptAndSwitchAvatar(CombatScenes combatScenes)
    {
        var combatCommands = _combatScriptBag.FindCombatScript(combatScenes.GetAvatars());
        if (combatCommands.Count == 0)
        {
            throw new Exception("没有可用战斗脚本");
        }

        var avatar = combatScenes.SelectAvatar(combatCommands[0].Name);
        avatar?.SwitchWithoutCts();
        Sleep(200, _ct);
        return combatCommands;
    }

    /// <summary>
    /// 使用所选自动战斗策略执行首领战斗，并周期性检测战斗是否结束。
    /// </summary>
    private async Task RunAutoFight()
    {
        _logger.LogInformation("{Name}：执行战斗策略", Name);
        var combatScenes = GetCombatScenesWithRetry();
        var combatCommands = FindCombatScriptAndSwitchAvatar(combatScenes);
        var delayTime = ParseCheckEndDelay(TaskContext.Instance().Config.AutoFightConfig.FinishDetectConfig.CheckEndDelay);
        var detectDelayTime = ParseBeforeDetectDelay(TaskContext.Instance().Config.AutoFightConfig.FinishDetectConfig.BeforeDetectDelay);
        CombatCommand? lastCommand = null;

        combatScenes.BeforeTask(_ct);
        AutoFightTask.FightStatusFlag = true;
        AutoFightSeek.RotationCount = 0;
        try
        {
            while (!_ct.IsCancellationRequested)
            {
                foreach (var command in combatCommands)
                {
                    _ct.ThrowIfCancellationRequested();
                    command.Execute(combatScenes, lastCommand);
                    lastCommand = command;
                }

                if (await CheckFightFinish(delayTime, detectDelayTime))
                {
                    return;
                }
            }
        }
        catch (NormalEndException e)
        {
            _logger.LogInformation("战斗操作中断：{Msg}", e.Message);
        }
        finally
        {
            combatScenes.AfterTask();
            Simulation.ReleaseAllKey();
            AutoFightTask.FightStatusFlag = false;
        }
    }

    /// <summary>
    /// 检测首领战斗是否已经结束，优先使用自动战斗的旋转找怪检测，必要时打开编队界面做兜底判断。
    /// </summary>
    /// <param name="delayTime">每轮检测前的等待毫秒数。</param>
    /// <param name="detectDelayTime">打开检测界面后的等待毫秒数。</param>
    /// <returns>识别到战斗结束时返回 true，否则返回 false。</returns>
    private async Task<bool> CheckFightFinish(int delayTime, int detectDelayTime)
    {
        var finishDetectConfig = TaskContext.Instance().Config.AutoFightConfig.FinishDetectConfig;
        if (finishDetectConfig.RotateFindEnemyEnabled)
        {
            bool? result;
            try
            {
                result = await AutoFightSeek.SeekAndFightAsync(
                    _logger,
                    detectDelayTime,
                    delayTime,
                    _ct,
                    rotaryFactor: finishDetectConfig.RotaryFactor);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SeekAndFightAsync 方法发生异常");
                result = false;
            }

            AutoFightSeek.RotationCount = result == null ? AutoFightSeek.RotationCount + 1 : 0;
            if (result != null)
            {
                return result.Value;
            }
        }

        if (!finishDetectConfig.RotateFindEnemyEnabled)
        {
            await Delay(delayTime, _ct);
        }

        _logger.LogInformation("打开编队界面检查战斗是否结束");
        Simulation.SendInput.SimulateAction(GIActions.OpenPartySetupScreen);
        await Delay(detectDelayTime, _ct);

        using var ra = CaptureToRectArea();
        var progressColor = ra.SrcMat.At<Vec3b>(50, 790);
        var whiteTile = ra.SrcMat.At<Vec3b>(50, 768);
        Simulation.SendInput.SimulateAction(GIActions.Drop);
        if (IsWhite(whiteTile.Item2, whiteTile.Item1, whiteTile.Item0) &&
            IsYellow(progressColor.Item2, progressColor.Item1, progressColor.Item0))
        {
            _logger.LogInformation("识别到战斗结束");
            Simulation.SendInput.SimulateAction(GIActions.OpenPartySetupScreen);
            return true;
        }

        _logger.LogInformation("未识别到战斗结束: yellow{B},{G},{R};white{WhiteB},{WhiteG},{WhiteR}",
            progressColor.Item0, progressColor.Item1, progressColor.Item2,
            whiteTile.Item0, whiteTile.Item1, whiteTile.Item2);
        return false;
    }

    /// <summary>
    /// 解析自动战斗配置中的结束检测等待时间。
    /// </summary>
    /// <param name="input">自动战斗配置字符串，单位为秒。</param>
    /// <returns>毫秒格式的等待时间；无法解析时返回默认值。</returns>
    private static int ParseCheckEndDelay(string input)
    {
        const int defaultDelay = 1500;
        if (string.IsNullOrWhiteSpace(input))
        {
            return defaultDelay;
        }

        foreach (var segment in input.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split(',');
            if (parts.Length == 1 &&
                double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                return Math.Max(0, (int)(seconds * 1000));
            }
        }

        return defaultDelay;
    }

    /// <summary>
    /// 解析打开检测界面后的等待时间。
    /// </summary>
    /// <param name="input">自动战斗配置字符串，单位为秒。</param>
    /// <returns>毫秒格式的等待时间；无法解析时返回默认值。</returns>
    private static int ParseBeforeDetectDelay(string input)
    {
        return double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            ? Math.Max(0, (int)(seconds * 1000))
            : 450;
    }

    /// <summary>
    /// 判断 RGB 颜色是否位于战斗结束进度条使用的黄色范围。
    /// </summary>
    /// <param name="r">红色通道。</param>
    /// <param name="g">绿色通道。</param>
    /// <param name="b">蓝色通道。</param>
    /// <returns>颜色落在黄色阈值内时返回 true。</returns>
    private static bool IsYellow(int r, int g, int b)
    {
        return r is >= 200 and <= 255 &&
               g is >= 200 and <= 255 &&
               b is >= 0 and <= 100;
    }

    /// <summary>
    /// 判断 RGB 颜色是否位于白色范围，用于辅助确认编队界面的战斗结束状态。
    /// </summary>
    /// <param name="r">红色通道。</param>
    /// <param name="g">绿色通道。</param>
    /// <param name="b">蓝色通道。</param>
    /// <returns>颜色落在白色阈值内时返回 true。</returns>
    private static bool IsWhite(int r, int g, int b)
    {
        return r is >= 240 and <= 255 &&
               g is >= 240 and <= 255 &&
               b is >= 240 and <= 255;
    }

    /// <summary>
    /// 校验自动首领讨伐启动所需的 Boss、战斗策略、讨伐次数、补充树脂开关、重试次数和路线文件。
    /// </summary>
    /// <exception cref="Exception">配置缺失、Boss 不支持、策略不存在或重试次数非法时抛出。</exception>
    /// <exception cref="FileNotFoundException">必需路线文件不存在时抛出。</exception>
    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(_taskParam.BossName))
        {
            throw new Exception("请选择需要讨伐的首领");
        }

        if (!AutoBossData.IsSupportedBoss(_taskParam.BossName))
        {
            throw new Exception($"暂不支持首领：{_taskParam.BossName}");
        }

        if (_taskParam.ReviveRetryCount < 0)
        {
            throw new Exception("角色死亡后重试次数不能小于 0");
        }

        if (_taskParam.SpecifyRunCount && _taskParam.RunCount < 1)
        {
            throw new Exception("指定讨伐次数必须大于 0");
        }

        if (!_taskParam.SpecifyRunCount && (_taskParam.UseTransientResin || _taskParam.UseFragileResin))
        {
            throw new Exception("只有指定讨伐次数模式才能开启须臾树脂或脆弱树脂补充");
        }

        if (string.IsNullOrWhiteSpace(_taskParam.CombatStrategyPath))
        {
            _taskParam.SetCombatStrategyPath(_taskParam.StrategyName);
        }

        if (!File.Exists(_taskParam.CombatStrategyPath) && !Directory.Exists(_taskParam.CombatStrategyPath))
        {
            throw new Exception("当前选择的自动战斗策略文件不存在");
        }

        foreach (var route in GetRequiredRouteFiles(_taskParam.BossName))
        {
            if (!File.Exists(BuildPathingAssetPath(route)))
            {
                throw new FileNotFoundException($"未找到首领路线文件：{route}", BuildPathingAssetPath(route));
            }
        }
    }

    /// <summary>
    /// 获取指定 Boss 启动任务前必须存在的路线文件名。
    /// </summary>
    /// <param name="bossName">Boss 名称。</param>
    /// <returns>需要校验的路线文件名序列。</returns>
    private static IEnumerable<string> GetRequiredRouteFiles(string bossName)
    {
        if (AutoBossData.IsNoPathingSupportBoss(bossName))
        {
            yield return $"{bossName}强制传送.json";
            yield return $"{bossName}键鼠前往.json";
            yield break;
        }

        yield return $"{bossName}前往.json";

        if (AutoBossData.IsTalkToStartBoss(bossName))
        {
            yield return $"{bossName}战斗后快速前往.json";
        }
    }

    /// <summary>
    /// 校验当前游戏窗口是否满足自动首领讨伐所需的 16:9 分辨率。
    /// </summary>
    /// <exception cref="Exception">游戏窗口不是 16:9 时抛出。</exception>
    private void LogScreenResolution()
    {
        var gameScreenSize = SystemControl.GetGameScreenRect(TaskContext.Instance().GameHandle);
        if (gameScreenSize.Width * 9 != gameScreenSize.Height * 16)
        {
            _logger.LogError("游戏窗口分辨率不是 16:9 ！当前分辨率为 {Width}x{Height}", gameScreenSize.Width, gameScreenSize.Height);
            throw new Exception("游戏窗口分辨率不是 16:9");
        }

        if (gameScreenSize.Width < 1920 || gameScreenSize.Height < 1080)
        {
            _logger.LogWarning("游戏窗口分辨率小于 1920x1080 ！当前分辨率为 {Width}x{Height}", gameScreenSize.Width, gameScreenSize.Height);
        }
    }

    /// <summary>
    /// 回到主界面并按配置尝试切换到指定队伍。
    /// </summary>
    private async Task Prepare()
    {
        await _returnMainUiTask.Start(_ct);

        if (string.IsNullOrWhiteSpace(_taskParam.TeamName))
        {
            return;
        }

        _logger.LogInformation("{Name}：切换队伍 {TeamName}", Name, _taskParam.TeamName);
        _switchPartyTask ??= new SwitchPartyTask();
        var success = await _switchPartyTask.Start(_taskParam.TeamName, _ct);
        if (!success)
        {
            throw new InvalidOperationException($"切换队伍失败：{_taskParam.TeamName}");
        }
    }

    /// <summary>
    /// 按 Boss 类型执行前往路线，特殊 Boss 使用强制传送加键鼠宏路线。
    /// </summary>
    private async Task NavigateToBoss()
    {
        _logger.LogInformation("{Name}：前往 {Boss}", Name, _taskParam.BossName);
        if (AutoBossData.IsNoPathingSupportBoss(_taskParam.BossName))
        {
            await RunPathingFile($"{_taskParam.BossName}强制传送.json");
            await RunKeyMouseFile($"{_taskParam.BossName}键鼠前往.json");
            return;
        }

        await RunPathingFile($"{_taskParam.BossName}前往.json");
    }

    /// <summary>
    /// 加载并执行 AutoBoss 资产目录中的寻路 JSON 文件。
    /// </summary>
    /// <param name="fileName">路线文件名。</param>
    private async Task RunPathingFile(string fileName)
    {
        await _returnMainUiTask.Start(_ct);
        var task = PathingTask.BuildFromFilePath(BuildPathingAssetPath(fileName)) ?? throw new Exception($"路径文件解析失败：{fileName}");
        await RunPathingTask(task);
    }

    /// <summary>
    /// 使用自动首领讨伐的队伍配置执行寻路任务。
    /// </summary>
    /// <param name="task">已经解析好的寻路任务。</param>
    private async Task RunPathingTask(PathingTask task)
    {
        var executor = new PathExecutor(_ct)
        {
            PartyConfig = BuildPathingPartyConfig()
        };
        await executor.Pathing(task);
    }

    /// <summary>
    /// 构建首领路线执行时使用的队伍配置，禁止寻路过程自动切队和自动战斗。
    /// </summary>
    /// <returns>寻路任务队伍配置。</returns>
    private static PathingPartyConfig BuildPathingPartyConfig()
    {
        var partyConfig = PathingPartyConfig.BuildDefault();
        partyConfig.SkipPartySwitch = true;
        partyConfig.AutoFightEnabled = false;
        return partyConfig;
    }

    /// <summary>
    /// 执行 AutoBoss 资产目录中的键鼠宏路线文件。
    /// </summary>
    /// <param name="fileName">键鼠宏文件名。</param>
    private async Task RunKeyMouseFile(string fileName)
    {
        await _returnMainUiTask.Start(_ct);
        var json = await File.ReadAllTextAsync(BuildPathingAssetPath(fileName), _ct);
        await KeyMouseMacroPlayer.PlayMacro(json, _ct, false);
    }

    /// <summary>
    /// 拼接 AutoBoss 路线资产的绝对路径。
    /// </summary>
    /// <param name="fileName">路线或键鼠宏文件名。</param>
    /// <returns>资源文件绝对路径。</returns>
    private string BuildPathingAssetPath(string fileName)
    {
        return Path.Combine(PathingAssetFolder, fileName);
    }

    /// <summary>
    /// 战斗结束后通过模板和 OCR 寻找“接触征讨之花”，并控制角色移动到可交互范围。
    /// </summary>
    /// <exception cref="TimeoutException">长时间无法找到或靠近征讨之花时抛出。</exception>
    private async Task NavigateToReward()
    {
        var page = new BvPage(_ct);
        var rewardRect = ScaleRect(1210, 515, 200, 50);
        var climbRect = ScaleRect(1686, 1030, 60, 23);
        var advanceCount = 0;
        var adjustCount = 0;
        var isMovingForward = false;
        const int forwardCheckInterval = 100;
        const int maxAdvanceCount = 200;

        Simulation.SendInput.Mouse.MiddleButtonClick();
        await Delay(800, _ct);
        Simulation.SendInput.Mouse.MoveMouseBy(0, 1030);
        await Delay(400, _ct);
        Simulation.SendInput.Mouse.MoveMouseBy(0, 920);
        await Delay(400, _ct);
        Simulation.SendInput.Mouse.MoveMouseBy(0, 710);
        _logger.LogInformation("{Name}：开始寻找征讨之花", Name);

        try
        {
            while (true)
            {
                _ct.ThrowIfCancellationRequested();
                if (page.Locator("接触征讨之花", rewardRect).IsExist())
                {
                    _logger.LogInformation("{Name}：已到达征讨之花", Name);
                    return;
                }

                if (advanceCount > maxAdvanceCount)
                {
                    throw new TimeoutException("前进到征讨之花超时");
                }

                if (page.Locator("Space", climbRect).IsExist())
                {
                    if (isMovingForward)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                        isMovingForward = false;
                    }

                    _logger.LogInformation("{Name}：检测到攀爬状态，尝试脱离", Name);
                    Simulation.SendInput.SimulateAction(GIActions.Drop);
                    await Delay(1000, _ct);
                    Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown);
                    await Delay(800, _ct);
                    Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                    isMovingForward = true;
                    await Delay(800, _ct);
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                    isMovingForward = false;
                    continue;
                }

                var boxRegions = page.Locator(AutoBossAssets.Instance.RewardBoxRo).FindAll();
                if (boxRegions.Count < 1)
                {
                    if (isMovingForward)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                        isMovingForward = false;
                    }

                    adjustCount++;
                    if (adjustCount > 50)
                    {
                        throw new TimeoutException("调整视野寻找征讨之花超时");
                    }

                    _logger.LogDebug("{Name}：未找到征讨之花图标，调整视角重试", Name);
                    Simulation.SendInput.Mouse.MoveMouseBy(200, 0);
                    await Delay(500, _ct);
                    continue;
                }

                var icon = boxRegions[0];
                if (icon.X >= ScaleX(900) && icon.X <= ScaleX(1020) && icon.Y < ScaleY(540))
                {
                    adjustCount = 0;
                    if (!isMovingForward)
                    {
                        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                        isMovingForward = true;
                    }

                    advanceCount++;
                    await Delay(forwardCheckInterval, _ct);
                    continue;
                }

                if (isMovingForward)
                {
                    Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                    isMovingForward = false;
                }

                adjustCount++;
                if (adjustCount > 50)
                {
                    throw new TimeoutException("调整视野寻找征讨之花超时");
                }

                if (icon.Y >= ScaleY(520))
                {
                    Simulation.SendInput.Mouse.MoveMouseBy(0, 920);
                }

                var distanceToCenter = icon.X - ScaleX(960);
                Simulation.SendInput.Mouse.MoveMouseBy((int)(Math.Round((double)distanceToCenter) * 0.8), 0);
                await Delay(100, _ct);
            }
        }
        finally
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            Simulation.SendInput.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
        }
    }

    /// <summary>
    /// 与征讨之花交互，并在世界 Boss 专用领奖界面点击“使用原粹树脂”领取奖励。
    /// </summary>
    /// <returns>成功领取奖励并回到主界面时返回 true；原粹树脂不足或无法领取时返回 false。</returns>
    private async Task<bool> TakeReward()
    {
        var page = new BvPage(_ct);
        var rewardRect = ScaleRect(1210, 515, 200, 50);
        await InteractWithRewardFlower(page, rewardRect);

        if (!await TryUseOriginalResinOnRewardPrompt(page))
        {
            await _returnMainUiTask.Start(_ct);
            return false;
        }

        await CloseRewardResult();
        Notify.Event("AutoBoss").Success($"{Name}奖励领取");
        return true;
    }

    /// <summary>
    /// 按照 JS 脚本的方式反复按交互键，直到“接触征讨之花”从屏幕上消失。
    /// </summary>
    /// <param name="page">当前视觉定位页面。</param>
    /// <param name="rewardRect">“接触征讨之花”的 OCR 识别区域。</param>
    private async Task InteractWithRewardFlower(BvPage page, Rect rewardRect)
    {
        await page.Locator("接触征讨之花", rewardRect).WaitFor(5000);
        await page.Locator("接触征讨之花", rewardRect)
            .WithRetryAction(_ => Simulation.SendInput.SimulateAction(GIActions.PickUpOrInteract))
            .WaitForDisappear(5000);
        await Delay(800, _ct);
    }

    /// <summary>
    /// 在世界 Boss 领奖界面的固定区域点击“使用原粹树脂”。
    /// </summary>
    /// <param name="page">当前视觉定位页面。</param>
    /// <returns>成功点击并等待使用按钮消失时返回 true；识别到补充原粹树脂或超时时返回 false。</returns>
    private async Task<bool> TryUseOriginalResinOnRewardPrompt(BvPage page)
    {
        var useRect = ScaleRect(850, 740, 250, 35);

        try
        {
            await page.Locator("使用原粹树脂", useRect).ClickUntilDisappears(3000);
            await Delay(1000, _ct);
            return true;
        }
        catch (TimeoutException e)
        {
            var supplementRegions = await page.Locator("补充原粹树脂", useRect).TryWaitFor(1000);
            if (supplementRegions.Count > 0)
            {
                _logger.LogInformation("{Name}：领奖界面提示补充原粹树脂，当前原粹树脂不足", Name);
            }
            else
            {
                _logger.LogWarning("{Name}：未能在世界 Boss 领奖界面点击“使用原粹树脂”，原因：{Reason}", Name, e.Message);
            }

            return false;
        }
    }

    /// <summary>
    /// 关闭领奖结果页面，等待回到主界面。
    /// </summary>
    /// <exception cref="Exception">多次尝试后仍未回到主界面时抛出。</exception>
    private async Task CloseRewardResult()
    {
        var page = new BvPage(_ct);
        var closeRect = ScaleRect(850, 960, 220, 35);

        for (var i = 0; i < 20; i++)
        {
            using var capture = CaptureToRectArea();
            if (Bv.IsInMainUi(capture))
            {
                return;
            }

            var closeRegion = capture.FindMulti(RecognitionObject.Ocr(closeRect))
                .FirstOrDefault(r => r.Text.Contains("点击空白区域继续", StringComparison.Ordinal));
            if (closeRegion != null)
            {
                closeRegion.Click();
            }
            else if (i > 5)
            {
                page.Click(960, 540);
            }

            await Delay(300, _ct);
        }

        using var finalCapture = CaptureToRectArea();
        if (!Bv.IsInMainUi(finalCapture))
        {
            throw new Exception("领取奖励后未成功回到主界面");
        }
    }

    /// <summary>
    /// 领奖后为下一轮讨伐重新定位：交互启动 Boss 走战后路线，特殊 Boss 重跑特殊路线，普通 Boss 回到路线终点。
    /// </summary>
    private async Task RepositionAfterFight()
    {
        if (AutoBossData.IsTalkToStartBoss(_taskParam.BossName))
        {
            _logger.LogInformation("{Name}：战后重新靠近交互点", Name);
            await RunPathingFile($"{_taskParam.BossName}战斗后快速前往.json");
            return;
        }

        if (AutoBossData.IsNoPathingSupportBoss(_taskParam.BossName))
        {
            _logger.LogInformation("{Name}：重新执行特殊路线靠近首领", Name);
            await NavigateToBoss();
            return;
        }

        _logger.LogInformation("{Name}：重新靠近首领位置并等待 4 秒", Name);
        var routePath = BuildPathingAssetPath($"{_taskParam.BossName}前往.json");
        var originalTask = PathingTask.BuildFromFilePath(routePath) ?? throw new Exception($"路径文件解析失败：{routePath}");
        var lastPosition = originalTask.Positions.LastOrDefault() ?? throw new Exception("路线文件缺少路径点");
        var repositionTask = new PathingTask
        {
            Info = originalTask.Info,
            Config = originalTask.Config,
            Positions = [lastPosition]
        };

        await RunPathingTask(repositionTask);
        await Delay(4000, _ct);
    }

    /// <summary>
    /// 将 1080P 基准矩形缩放到当前资源比例。
    /// </summary>
    /// <param name="x">基准 X 坐标。</param>
    /// <param name="y">基准 Y 坐标。</param>
    /// <param name="width">基准宽度。</param>
    /// <param name="height">基准高度。</param>
    /// <returns>缩放后的矩形。</returns>
    private Rect ScaleRect(int x, int y, int width, int height)
    {
        return new Rect(ScaleX(x), ScaleY(y), ScaleX(width), ScaleY(height));
    }

    /// <summary>
    /// 将 1080P 基准 X 方向长度缩放到当前资源比例。
    /// </summary>
    /// <param name="value">基准长度或坐标。</param>
    /// <returns>缩放后的整数值。</returns>
    private int ScaleX(int value)
    {
        return (int)Math.Round(value * AssetScale);
    }

    /// <summary>
    /// 将 1080P 基准 Y 方向长度缩放到当前资源比例。
    /// </summary>
    /// <param name="value">基准长度或坐标。</param>
    /// <returns>缩放后的整数值。</returns>
    private int ScaleY(int value)
    {
        return (int)Math.Round(value * AssetScale);
    }

}
