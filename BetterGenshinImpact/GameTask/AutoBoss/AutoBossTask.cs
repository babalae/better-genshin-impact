using BetterGenshinImpact.GameTask.Common.Resin;
using BetterGenshinImpact.GameTask.AutoFight.Strategy;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.BgiVision;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recorder;
using BetterGenshinImpact.Core.Simulator.Extensions;
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
using BetterGenshinImpact.GameTask.Common.Reward;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Notification;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using BetterGenshinImpact.View.Pages.SoloTaskSettings;
using BetterGenshinImpact.ViewModel.SoloTaskSettings;

namespace BetterGenshinImpact.GameTask.AutoBoss;

/// <summary>
/// 自动首领讨伐独立任务，负责按配置前往 Boss、执行战斗策略、领取征讨之花奖励并处理重定位。
/// </summary>
[SoloTaskInfo(TaskId, "自动首领讨伐", Group = "树脂与战斗", Order = 220)]
[SoloTaskSettingsView(typeof(AutoBossSettingsView), typeof(AutoBossSettingsViewModel))]
public class AutoBossTask : ISoloTask<AutoBossParam, ResinClaimData>
{
    /// <summary>本任务支持的树脂使用方式、种类、档位及默认设置。</summary>
    public static ResinUsageCapability ResinCapability => ResinCapabilities.Boss;

    /// <summary>
    /// 任务稳定标识。
    /// </summary>
    public const string TaskId = "auto_boss";

    /// <summary>
    /// 日志与通知中使用的任务名称。
    /// </summary>
    private const string TaskName = "自动首领讨伐";

    private readonly ILogger<AutoBossTask> _logger = App.GetLogger<AutoBossTask>();

    /// <summary>
    /// 本次运行使用的运行环境
    /// </summary>
    private readonly GameRuntime _runtime;

    /// <summary>
    /// 配置根，由创建方传入
    /// </summary>
    private readonly AllConfig _config;

    /// <summary>
    /// 本次运行的任务参数，在 <see cref="RunAsync"/> 开头赋值
    /// </summary>
    private AutoBossParam _taskParam = null!;

    /// <summary>
    /// 解析后的实际战斗策略路径，在 <see cref="Initialize"/> 中赋值
    /// </summary>
    private string _combatStrategyPath = string.Empty;

    /// <summary>
    /// TXT 战斗策略解析结果；使用 JSON/键鼠策略时为 null
    /// </summary>
    private CombatScriptBag? _combatScriptBag;

    /// <summary>
    /// 由统一入口执行的 JSON/键鼠策略路径；文本策略时为 null
    /// </summary>
    private string? _entryCombatStrategyPath;
    private readonly ReturnMainUiTask _returnMainUiTask;
    private readonly Dictionary<string, int> _rewardSummary = new();
    private SwitchPartyTask? _switchPartyTask;
    private CancellationToken _ct;
    /// <summary>首领领奖及战前补充的统一记账，领奖只记录原粹。</summary>
    private ResinClaimSelector _selector = null!;
    /// <summary>本次结束原因，区分战前不足、领奖不足和未找到使用选项。</summary>
    private ResinStopReason _stopReason;
    /// <summary>是否已经开始首领战斗，首次预检不足时返回跳过。</summary>
    private bool _hasBattled;

    /// <summary>材料目标是否已全部达成。</summary>
    private bool _itemStopReached;

    private static readonly TimeSpan OriginalResinRecoveryInterval = TimeSpan.FromMinutes(8);
    private const int MaxQuickUseQuantity = 20;

    private string PathingAssetFolder => Global.Absolute(@"GameTask\AutoBoss\Assets\Pathing");

    /// <summary>
    /// 本运行环境的素材缩放比例
    /// </summary>
    private double AssetScale => _runtime.SystemInfo.AssetScale;

    /// <summary>
    /// 按本运行环境的画面尺寸加载 AutoBoss 素材
    /// </summary>
    /// <param name="objectName">素材名称</param>
    private RecognitionObject LoadRecognitionObject(string objectName)
    {
        return LoadRecognitionObject("AutoBoss", objectName);
    }

    /// <summary>
    /// 按本运行环境的画面尺寸加载指定功能的素材
    /// </summary>
    /// <param name="taskName">素材所属功能</param>
    /// <param name="objectName">素材名称</param>
    private RecognitionObject LoadRecognitionObject(string taskName, string objectName)
    {
        var captureRect = _runtime.SystemInfo.ScaleMax1080PCaptureRect;
        return RecognitionAssets.Get(taskName, objectName, captureRect.Width, captureRect.Height);
    }

    /// <summary>
    /// 构建判断已打开大地图的定位器
    /// </summary>
    /// <param name="page">当前视觉定位页面</param>
    private BvLocator[] BuildBigMapLocators(BvPage page)
    {
        return
        [
            page.Locator(LoadRecognitionObject("QuickTeleport", "MapScaleButton")),
            page.Locator(LoadRecognitionObject("QuickTeleport", "MapSettingsButton"))
        ];
    }

    private sealed record OriginalResinInfo(int Count, int Limit);

    /// <summary>首领战前补充选项；种类用于消耗记账，模板及面板流程沿用原实现。</summary>
    private sealed record SupplementalResinOption(ResinKind Kind, string Name, RecognitionObject Template);

    /// <summary>
    /// 创建自动首领讨伐任务。业务参数在 <see cref="RunAsync"/> 中传入。
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境。</param>
    /// <param name="config">配置根。</param>
    public AutoBossTask(GameRuntime runtime, AllConfig config)
    {
        _runtime = runtime;
        _config = config;
        _returnMainUiTask = new ReturnMainUiTask(runtime);
    }

    /// <summary>
    /// 运行自动首领讨伐任务，包含参数校验、分辨率校验、死亡重试和最终输入状态释放。
    /// 参数无效或策略不存在时返回失败结果；流程正常结束返回未确认结果并附带奖励汇总；取消时抛出 OperationCanceledException。
    /// </summary>
    /// <param name="param">自动首领讨伐参数，包含 Boss、队伍、战斗策略、讨伐次数和补充树脂开关。</param>
    /// <param name="ct">任务取消令牌。</param>
    /// <returns>任务结果，数据为本次识别到的奖励汇总。</returns>
    public async Task<SoloTaskResult<ResinClaimData>> RunAsync(AutoBossParam param, CancellationToken ct)
    {
        _taskParam = param;
        _ct = ct;
        _rewardSummary.Clear();
        _itemStopReached = false;
        if (param.ResinUsage.Validate(ResinCapability) is { } problem)
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, problem);
        _selector = new ResinClaimSelector(param.ResinUsage, ResinCapability);
        _stopReason = ResinStopReason.NoUsableOption;
        _hasBattled = false;

        // 1. 业务参数校验，可预见的参数问题直接返回失败结果
        var failure = ValidateParam(param, _runtime) ?? Initialize(param);
        if (failure != null)
        {
            _logger.LogError("{Name}：{Msg}", TaskName, failure.Message);
            return failure;
        }

        if (_taskParam.TargetItems.Count > 0)
        {
            _logger.LogInformation("{Name}：材料目标 {Targets}，达到后提前结束", TaskName,
                string.Join("，", _taskParam.TargetItems.Select(x => $"{x.ItemName}×{x.TargetCount}")));
        }

        // 2. 运行环境校验：路线资源与分辨率
        ValidateRouteFiles();
        LogScreenResolution();

        Notify.Event("AutoBoss").Success($"{TaskName}启动");
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
                    _logger.LogWarning("{Name}：第 {Retry}/{MaxRetry} 次重试当前首领讨伐，原因：{Reason}", TaskName, retryCount, _taskParam.ReviveRetryCount, e.Message);
                    await _runtime.Delay(2000, _ct);
                }
                catch (RetryException e)
                {
                    _logger.LogWarning("{Name}：角色死亡后重试次数已达上限 {MaxRetry}，结束任务，原因：{Reason}", TaskName, _taskParam.ReviveRetryCount, e.Message);
                    throw;
                }
            }
        }
        finally
        {
            InputHub.ReleaseAll();
            InputHub.Foreground.Mouse.LeftButtonUp();
            Notify.Event("AutoBoss").Success($"{TaskName}结束");
        }

        // 讨伐次数或材料目标任一达成都按目标完成结算并返回成功；树脂不足等提前结束按未确认结束返回，并附带奖励汇总。
        return ResinClaimResults.Complete(_selector,
            _selector.IsTargetReached || _itemStopReached ? ResinStopReason.TargetReached : _stopReason,
            "首领讨伐流程已结束", _hasBattled, RewardSummary.From(_rewardSummary), _itemStopReached);
    }

    /// <summary>
    /// 按本次参数解析战斗策略：为空时按策略名称生成路径；不是已存在的路径时用 <see cref="CombatStrategyResolver"/> 解析。
    /// </summary>
    /// <param name="param">本次运行的任务参数。</param>
    /// <returns>策略不存在或不受支持时返回失败结果；成功返回 null。</returns>
    private SoloTaskResult? Initialize(AutoBossParam param)
    {
        _combatScriptBag = null;
        _entryCombatStrategyPath = null;

        // 1. 未设置策略路径时按策略名称生成（与原构造函数行为一致）
        if (string.IsNullOrWhiteSpace(param.CombatStrategyPath))
        {
            param.SetCombatStrategyPath(param.StrategyName);
        }

        // 2. 不是已存在的路径（例如任务计划保存的是策略名）时先解析
        var strategy = CombatStrategyResolver.Parse(param.CombatStrategyPath);
        if (!strategy.Exists)
            return SoloTaskResult.Failed(SoloTaskReasons.StrategyNotFound, $"战斗策略不存在：{param.CombatStrategyPath}");
        var strategyPath = strategy.Path;

        // 自动连招策略没有对应的文件，首领讨伐不支持
        if (strategy.Kind == CombatStrategyKind.Combo)
        {
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, "自动首领讨伐不支持自动连招策略。");
        }

        _combatStrategyPath = strategyPath;

        // 3. 按策略类型选择战斗引擎：统一入口（JSON、键鼠）/ TXT
        if (strategy.Kind is not (CombatStrategyKind.Txt or CombatStrategyKind.AutoSelect))
        {
            _entryCombatStrategyPath = strategyPath;
            _logger.LogInformation("自动首领讨伐：使用统一战斗入口执行所选策略");
        }
        else
        {
            _combatScriptBag = CombatScriptParser.ReadAndParse(strategyPath);
        }

        return null;
    }

    /// <summary>
    /// 执行完整讨伐循环：准备环境、前往首领、战斗、寻找奖励、领奖并决定是否继续下一轮。
    /// </summary>
    private async Task RunBossLoop()
    {
        // 1.切换队伍
        await Prepare();
        
        var shouldNavigateToBoss = true;
        //2.根据剩余次数判断是否继续
        while (ShouldContinueBeforeRound())
        {
            _ct.ThrowIfCancellationRequested();
            _logger.LogInformation("{Name}：开始第 {Round} 次讨伐 {Boss}", TaskName, _selector.ClaimCount + 1, _taskParam.BossName);
            
            //3.树脂不足则退出
            if (!await EnsureResinBeforeRound())
            {
                _stopReason = ResinStopReason.ResinExhausted;
                _logger.LogInformation("{Name}：原粹树脂不足或补充失败，结束任务", TaskName);
                break;
            }
            
            //4.首次讨伐 or 回过七天神像 则需要重新寻路到首领
            if (shouldNavigateToBoss)
            {
                await NavigateToBoss();
            }
            
            //5.开始战斗
            _hasBattled = true;
            await RunAutoFight();
            
            //6.寻路到征讨之花
            await NavigateToReward();
            
            //7.交互征讨之花
            var rewardSuccess = await TakeReward();
            if (!rewardSuccess)
            {
                _logger.LogInformation("{Name}：原粹树脂不足或无法领取奖励，结束任务", TaskName);
                break;
            }

            _taskParam.RewardClaimedCallback?.Invoke();
            if (!ShouldContinueBeforeRound())
            {
                break;
            }
            
            if (_taskParam.ReturnToStatueAfterEachRound)
            {
                _logger.LogInformation("{Name}：返回七天神像", TaskName);
                await Pluggable.Get<ITpTaskFactory>().Create(_runtime, _config, _ct).TpToStatueOfTheSeven();
                await _runtime.Delay(3000, _ct);
                shouldNavigateToBoss = true;
            }
            else
            {
                // 就近回到首领附近继续讨伐
                await RepositionAfterFight();
                shouldNavigateToBoss = false;
            }
        }
    }

    /// <summary>
    /// 判断本轮开始前是否还有继续讨伐的次数：树脂设置目标未达成且材料目标未达成时继续。
    /// </summary>
    private bool ShouldContinueBeforeRound()
    {
        return !_selector.IsTargetReached && !_itemStopReached;
    }

    /// <summary>
    /// 战前预检原粹树脂是否足够领取一次世界 Boss 奖励；必要时尝试使用已开启的补充树脂入口。
    /// </summary>
    /// <returns>树脂足够或预检失败需要交由领奖弹窗兜底时返回 true；确认不足且无法补充时返回 false。</returns>
    private async Task<bool> EnsureResinBeforeRound()
    {
        try
        {
            await _returnMainUiTask.Start(_ct);

            OriginalResinInfo originalResin;
            try
            {
                originalResin = await RecognizeOriginalResinInfoFromBigMap();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogWarning("{Name}：战前原粹树脂预检失败，将继续通过领奖界面兜底，原因：{Reason}", TaskName, e.Message);
                return true;
            }

            if (originalResin.Count >= 40)
            {
                return true;
            }

            _logger.LogInformation("{Name}：当前原粹树脂 {Count}/{Limit}，不足以领取首领奖励", TaskName, originalResin.Count, originalResin.Limit);
            // 耗尽和指定次数都允许战前补充，只由本次设置与能力决定。
            if (_selector.ReplenishKinds.Count == 0)
            {
                _logger.LogInformation("{Name}：未开启可用的补充树脂，停止讨伐", TaskName);
                return false;
            }

            if (await TryUseSupplementalResinBeforeRound(originalResin))
            {
                return true;
            }

            _logger.LogInformation("{Name}：补充树脂未成功，停止讨伐", TaskName);
            return false;
        }
        finally
        {
            await _returnMainUiTask.Start(_ct);
        }
    }

    /// <summary>
    /// 打开大地图并通过树脂详情中的全部恢复时间反推当前原粹树脂。
    /// </summary>
    private async Task<OriginalResinInfo> RecognizeOriginalResinInfoFromBigMap()
    {
        var page = new BvPage(_runtime, _ct);
        //树脂图标
        // 当新老深渊都有战绩时，左边界需要放宽到 900
        var resinIconLocator = page
            .Locator(LoadRecognitionObject("OriginalResinTopIcon"))
            .WithRoi(ScaleRect(900, 25, 880, 50));
        
        await page.Flow()
            .Do((Action)(() =>
            {
                using var capture = _runtime.CaptureToRectArea();
                if (!Bv.IsInBigMapUi(capture))
                {
                    InputHub.Foreground.SimulateAction(GIActions.OpenMap);
                }
            }))
            .UntilAny(BuildBigMapLocators(page))
            .WaitUntil(resinIconLocator)
            // 树脂详情弹窗随图标左移，扫描区左边界与新图标 ROI 对齐，右边界保持不变
            .Click().UntilAnyText(new[] { "全部恢复", "原粹树脂已完全恢复" }, ScaleRect(900, 75, 900, 200))
            .Run();

        //根据树脂图标位置偏移，计算出体力恢复时间的弹窗位置
        var resinIconRegion = (await resinIconLocator.WaitFor()).First();
        var detailRect = new Rect(resinIconRegion.Left - 13, resinIconRegion.Bottom + 29, 220, 150);
        
        //根据弹窗位置 OCR 出当前树脂上限和全部恢复时间，反推当前原粹树脂
        using var clickedCapture = _runtime.CaptureToRectArea();
        var resinLimit = RecognizeOriginalResinLimit(clickedCapture, resinIconRegion.Right);
        var fullRecoveryTime = RecognizeFullRecoveryTime(clickedCapture, detailRect);
        var missingResin = (int)Math.Ceiling(fullRecoveryTime.TotalSeconds / OriginalResinRecoveryInterval.TotalSeconds);
        if (missingResin > resinLimit)
        {
            throw new InvalidOperationException($"计算缺失树脂 {missingResin} 超过树脂上限 {resinLimit}");
        }

        var originalResin = new OriginalResinInfo(resinLimit - missingResin, resinLimit);
        _logger.LogInformation("{Name}：剩余树脂 {Count}", TaskName, originalResin.Count);
        return originalResin;
    }

    /// <summary>
    /// 按原右上角 OCR 区域读取当前/上限文本，并取数字串最后三位作为树脂上限。
    /// </summary>
    private int RecognizeOriginalResinLimit(ImageRegion capture, int resinIconRight)
    {
        var countRect = new Rect(resinIconRight + ScaleX(25), ScaleY(37), ScaleX(120), ScaleY(24));
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

        _logger.LogDebug("{Name}：原粹树脂上限 OCR：{Text} -> {Limit}", TaskName, countText, resinLimit);
        return resinLimit;
    }

    /// <summary>
    /// 读取树脂详情弹窗中的全部恢复时间；已完全恢复时返回零时长。
    /// </summary>
    private TimeSpan RecognizeFullRecoveryTime(ImageRegion capture, Rect detailRect)
    {
        using var detailRegion = capture.DeriveCrop(detailRect);
        var result = OcrFactory.Paddle.OcrResult(detailRegion.SrcMat);
        var text = string.Concat(result.Regions
            .OrderBy(region => region.Rect.Center.Y)
            .ThenBy(region => region.Rect.Center.X)
            .Select(region => region.Text));
        var normalizedText = NormalizeRecoveryText(text);
        _logger.LogDebug("{Name}：原粹树脂恢复详情 OCR：{Text}", TaskName, normalizedText);

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
        var page = new BvPage(_runtime, _ct);
        var currentResin = originalResin;
        var unavailableResins = new HashSet<string>(StringComparer.Ordinal);

        while (currentResin.Count < 40)
        {
            var targetQuantity = CalculateSupplementalResinTargetQuantity(currentResin);
            if (targetQuantity <= 0)
            {
                _logger.LogInformation("{Name}：当前原粹树脂 {Count}/{Limit}，使用任意补充树脂都会超过上限，停止补充", TaskName, currentResin.Count, currentResin.Limit);
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

                _selector.RecordConsumed(resin.Kind, usedQuantity);
                usedAnyResin = true;
                _logger.LogInformation("{Name}：已使用 {Quantity} 个{ResinName}补充原粹树脂", TaskName, usedQuantity, resin.Name);
                await _runtime.Delay(800, _ct);

                try
                {
                    currentResin = await RecognizeOriginalResinInfoFromBigMap();
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _logger.LogWarning("{Name}：补充后重新识别原粹树脂失败，停止讨伐，原因：{Reason}", TaskName, e.Message);
                    return false;
                }

                _logger.LogInformation("{Name}：补充后原粹树脂 {Count}/{Limit}", TaskName, currentResin.Count, currentResin.Limit);
                if (currentResin.Count >= 40)
                {
                    return true;
                }

                break;
            }

            if (!usedAnyResin)
            {
                _logger.LogInformation("{Name}：未找到可用的须臾树脂或脆弱树脂", TaskName);
                return false;
            }
        }

        return true;
    }

    /// <summary>按选择器提供的须臾、脆弱顺序建立战前补充选项，不将补充视为领奖。</summary>
    private List<SupplementalResinOption> BuildSupplementalResinOptions()
    {
        return _selector.ReplenishKinds.Select(kind => new SupplementalResinOption(kind,
            ResinUsageText.DisplayName(kind), LoadRecognitionObject(kind == ResinKind.Transient
                ? "TransientResinInSupplementPane" : "FragileResinInSupplementPane"))).ToList();
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

        var openButtonLocator = page.Locator(LoadRecognitionObject("OpenResinSupplementPaneButton"))
            // 浓缩树脂为 0 时顶栏不显示浓缩条目，"+"按钮随原粹药丸整体左移，搜索区与图标 ROI 保持一致
            .WithRoi(ScaleRect(900, 25, 880, 50))
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
            _logger.LogWarning("{Name}：未能打开补充原粹树脂面板，原因：{Reason}", TaskName, e.Message);
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
            _logger.LogInformation("{Name}：补充面板未找到{ResinName}图标，视为不可用", TaskName, resin.Name);
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
            _logger.LogWarning("{Name}：未能确认已选中{ResinName}，原因：{Reason}", TaskName, resin.Name, e.Message);
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

        await _runtime.Delay(500, _ct);
        var quickUseRegions = await page.Locator("快捷使用", ScaleRect(875, 269, 184, 63)).TryWaitFor(1500);
        if (quickUseRegions.Count > 0)
        {
            return await TryUseQuickSupplementalResin(page, resin, targetQuantity);
        }

        if (await TryCloseSupplementObtainDialog(page))
        {
            return 1;
        }

        _logger.LogWarning("{Name}：点击{ResinName}使用后未识别到快捷使用或获得界面", TaskName, resin.Name);
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
            _logger.LogWarning("{Name}：快捷使用面板未识别到{ResinName}可用数量", TaskName, resin.Name);
            return 0;
        }

        var actualQuantity = await AdjustQuickUseQuantity(page, targetQuickQuantity);
        if (actualQuantity <= 0 || actualQuantity > targetQuickQuantity)
        {
            _logger.LogWarning("{Name}：快捷使用{ResinName}数量无效，目标 {TargetQuantity}，识别 {ActualQuantity}", TaskName, resin.Name, targetQuickQuantity, actualQuantity);
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
        _logger.LogDebug("{Name}：快捷使用可用数量 OCR：{Text} -> {Quantity}", TaskName, text, availableQuantity);
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
        var increaseLocator = page.Locator(LoadRecognitionObject("IncreaseResinUsageQuantityButton"))
            .WithRoi(ScaleRect(1265, 620, 59, 55))
            .WithRetryInterval(500);

        var actualQuantity = 0;
        var lastQuantity = -1;
        var unchangedTimes = 0;
        var retryTimes = Math.Max(6, targetQuantity * 3);
        var completed = await NewRetry.WaitForAction(_runtime, () =>
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
            _logger.LogDebug("{Name}：快捷使用数量调整超时，目标 {TargetQuantity}，当前 {ActualQuantity}", TaskName, targetQuantity, actualQuantity);
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
            _logger.LogDebug("{Name}：快捷使用数量 OCR 未包含使用数量：{Text}", TaskName, text);
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
            _logger.LogDebug("{Name}：快捷使用数量 OCR 解析失败：{Text}", TaskName, text);
            return null;
        }

        _logger.LogDebug("{Name}：快捷使用数量 OCR：{Text} -> {Quantity}", TaskName, text, quantity);
        return quantity;
    }

    private async Task<bool> TryCloseSupplementObtainDialog(BvPage page)
    {
        try
        {
            var obtainRegion = (await page.Locator("获得", ScaleRect(924, 279, 74, 38)).WaitFor(5000)).First();
            obtainRegion.Click();
            await _runtime.Delay(800, _ct);
            return true;
        }
        catch (TimeoutException e)
        {
            _logger.LogWarning("{Name}：未识别到补充树脂获得界面，原因：{Reason}", TaskName, e.Message);
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
            _logger.LogWarning("{Name}：未能点击文本按钮 {Text}，原因：{Reason}", TaskName, text, e.Message);
            return false;
        }
    }

    private string RecognizeTextWithoutDetector(Rect rect)
    {
        using var capture = _runtime.CaptureToRectArea();
        using var region = capture.DeriveCrop(rect);
        return OcrFactory.Paddle.OcrWithoutDetector(region.SrcMat);
    }

    private string RecognizeSortedOcrText(Rect rect)
    {
        using var capture = _runtime.CaptureToRectArea();
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
        _runtime.Sleep(200, _ct);
        return combatCommands;
    }

    /// <summary>
    /// 使用所选自动战斗策略执行首领战斗，并复用自动战斗的结束检测。
    /// </summary>
    private async Task RunAutoFight()
    {
        _logger.LogInformation("{Name}：执行战斗策略", TaskName);

        if (_entryCombatStrategyPath != null)
        {
            await StartEntryFight();
        }
        else
        {
            var combatScenes = CombatScenes.GetCombatScenesWithRetry(_runtime, _config);
            FindCombatScriptAndSwitchAvatar(combatScenes);

            var taskParam = BuildAutoFightParamForBoss();
            SoloTaskResult<NoData>? fightResult = null;
            try
            {
                // 组合调用使用 RunAsync，保留子任务异常向外传播的语义
                fightResult = await new AutoFightEntryTask(_runtime, _config).RunAsync(taskParam, _ct);
            }
            catch (NormalEndException e)
            {
                _logger.LogInformation("战斗操作中断：{Msg}", e.Message);
            }
            finally
            {
                combatScenes.AfterTask();
            }

            // 原来战斗任务的业务失败以异常形式中断首领讨伐，现在以结果返回，同样中断并携带子任务的失败原因
            if (fightResult is { Kind: SoloTaskResultKind.Failed })
            {
                _logger.LogWarning("{Name}：战斗任务失败：{Msg}", TaskName, fightResult.Message);
                throw new SoloTaskAbortException(new SoloTaskResult(fightResult.Kind, fightResult.Message, fightResult.ReasonCode));
            }
        }
    }

    /// <summary>
    /// JSON/键鼠策略战斗入口：委托给AutoFightEntryTask，由其自身处理战斗结束检测，
    /// 抑制战后拾取逻辑。
    /// </summary>
    private async Task StartEntryFight()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_ct);

        var jsonParam = new AutoFightParam
        {
            CombatStrategyPath = _entryCombatStrategyPath!,
            FightFinishDetectEnabled = true,
            ExpBasedPickupEnabled = false,
            KazuhaPickupEnabled = false,
            PickDropsAfterFightEnabled = false,
            Timeout = _taskParam.Timeout,
        };

        var jsonTask = new AutoFightEntryTask(_runtime, _config);

        try
        {
            // 组合调用使用 RunAsync；JSON 战斗原来吞掉异常只记日志，失败结果同样只记日志
            var result = await jsonTask.RunAsync(jsonParam, cts.Token);
            if (result.Kind == SoloTaskResultKind.Failed)
            {
                _logger.LogWarning("JSON/键鼠战斗任务失败：{Msg}", result.Message);
            }
        }
        catch (OperationCanceledException) when (_ct.IsCancellationRequested) { throw; }
        catch (RetryException) { throw; }
        catch (NormalEndException e)
        {
            _logger.LogInformation("战斗操作中断：{Msg}", e.Message);
        }
        catch (Exception e)
        {
            _logger.LogWarning("JSON/键鼠战斗任务异常：{Msg}", e.Message);
        }
    }

    /// <summary>
    /// 构造 AutoBoss 专用自动战斗参数：复用战斗检测配置，但不执行任何战后拾取。
    /// </summary>
    private AutoFightParam BuildAutoFightParamForBoss()
    {
        // 参数显式提供战斗配置时使用它，否则沿用全局自动战斗配置
        var taskParam = new AutoFightParam(_combatStrategyPath, _taskParam.FightConfig ?? _config.AutoFightConfig)
        {
            FightFinishDetectEnabled = true,
            PickDropsAfterFightEnabled = false,
            KazuhaPickupEnabled = false,
            QinDoublePickUp = false,
            ExpBasedPickupEnabled = false,
            KazuhaPartyName = string.Empty,
            BattleThresholdForLoot = -1,
            OnlyPickEliteDropsMode = "DisableAutoPickupForNonElite",
            Timeout = _taskParam.Timeout
        };
        return taskParam;
    }

    /// <summary>
    /// 校验自动首领讨伐的业务参数：Boss、讨伐次数、补充树脂开关、重试次数和战斗超时。
    /// </summary>
    /// <param name="param">本次运行的任务参数。</param>
    /// <returns>参数无效时返回失败结果；有效时返回 null。</returns>
    private static SoloTaskResult? ValidateParam(AutoBossParam param, GameRuntime runtime)
    {
        if (string.IsNullOrWhiteSpace(param.BossName))
        {
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, "请选择首领。");
        }

        if (!AutoBossData.IsSupportedBoss(param.BossName))
        {
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, $"暂不支持首领：{param.BossName}");
        }

        if (param.ReviveRetryCount < 0)
        {
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, "角色死亡后重试次数不能小于 0");
        }

        // 树脂方式、次数和补充开关由任务入口按能力统一校验。

        if (param.Timeout < 1)
        {
            return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, "战斗超时必须大于 0 秒");
        }

        if (param.TargetItems.Count > 0)
        {
            if (!TargetItemCatalog.Instance.TryGetBossProfile(param.BossName, out var profile))
            {
                return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, $"当前首领没有奖励数据：{param.BossName}");
            }

            try
            {
                TargetItemCatalog.Instance.ValidateBossSettings(profile, param.TargetItems);
                RewardResultRecognizer.Instance.EnsureSupported(runtime);
            }
            catch (Exception e) when (e is TargetItemConfigurationException or InvalidOperationException)
            {
                return SoloTaskResult.Failed(SoloTaskReasons.InvalidParameter, e.Message);
            }
        }

        return null;
    }

    /// <summary>
    /// 校验当前首领启动任务前必须存在的路线文件。
    /// </summary>
    /// <exception cref="FileNotFoundException">必需路线文件不存在时抛出。</exception>
    private void ValidateRouteFiles()
    {
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
        var gameScreenSize = SystemControl.GetGameScreenRect(_runtime.Window.Handle);
        if (gameScreenSize.Width * 9 != gameScreenSize.Height * 16)
        {
            _logger.LogError("游戏窗口分辨率不是 16:9 ！当前分辨率为 {Width}x{Height}", gameScreenSize.Width, gameScreenSize.Height);
            throw new Exception("游戏窗口分辨率不是 16:9");
        }

        if (gameScreenSize.Width < 1920 || gameScreenSize.Height < 1080)
        {
            Logger.LogWarning("游戏窗口分辨率小于 1920x1080 ！当前分辨率为 {Width}x{Height} , 小于 1920x1080 的分辨率的游戏可能无法正常使用自动首领功能 !",
                gameScreenSize.Width, gameScreenSize.Height);
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

        _logger.LogInformation("{Name}：切换队伍 {TeamName}", TaskName, _taskParam.TeamName);
        _switchPartyTask ??= new SwitchPartyTask(_runtime);
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
        _logger.LogInformation("{Name}：前往 {Boss}", TaskName, _taskParam.BossName);
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
        var executor = new PathExecutor(_runtime, _config, _ct)
        {
            PartyConfig = BuildPathingPartyConfig()
        };
        // 首领流程保留原有后续识别和战斗判定，本次寻路结果不替代业务判断。
        _ = await executor.Pathing(task);
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
        await KeyMouseMacroPlayer.PlayMacro(_runtime, json, _ct, false);
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
        using var navigationCts = CancellationTokenSource.CreateLinkedTokenSource(_ct);
        var page = new BvPage(_runtime, navigationCts.Token);

        _logger.LogInformation("{Name}：开始寻找征讨之花", TaskName);

        var navigationStopwatch = Stopwatch.StartNew();
        var navigationTimeout = TimeSpan.FromSeconds(20);
        var adjustCameraTask = AdjustRewardCameraTask(page, navigationCts);
        var moveToRewardTask = MoveToRewardTask(page, navigationCts);
        var monitorRewardPromptTask = MonitorRewardPromptTask(page, navigationCts);

        try
        {
            while (true)
            {
                _ct.ThrowIfCancellationRequested();
                if (navigationStopwatch.Elapsed >= navigationTimeout)
                {
                    throw new TimeoutException("超时未找到征讨之花领奖界面");
                }

                var completedTask = await Task.WhenAny(Task.Delay(500, _ct),adjustCameraTask,moveToRewardTask,monitorRewardPromptTask);

                if (completedTask == monitorRewardPromptTask)
                {
                    await monitorRewardPromptTask;
                    _logger.LogInformation("{Name}：已进入征讨之花领奖界面", TaskName);
                    return;
                }

                if (completedTask == adjustCameraTask || completedTask == moveToRewardTask)
                {
                    navigationCts.Cancel();
                    await completedTask;
                    throw new TimeoutException("前往征讨之花任务异常结束");
                }
            }
        }
        finally
        {
            navigationCts.Cancel();
            try
            {
                await Task.WhenAll(adjustCameraTask, moveToRewardTask, monitorRewardPromptTask);
            }
            catch
            {
                // The main loop observes task failures; shutdown also cancels background tasks.
            }

            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            InputHub.Foreground.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
            InputHub.Foreground.SimulateAction(GIActions.MoveRight, KeyType.KeyUp);
        }
    }

    private bool HasRewardInteractionPrompt(BvPage page)
    {
        var rewardRect = ScaleRect(1210, 300, 200, 400);
        return page.Ocr(rewardRect).Any(region => region.Text.Contains("接触征讨之花", StringComparison.Ordinal));
    }

    private bool HasRewardPanelPrompt(BvPage page)
    {
        var rewardPanelRect = ScaleRect(850, 740, 250, 35);
        return page.Ocr(rewardPanelRect).Any(region =>
            region.Text.Contains("使用原粹树脂", StringComparison.Ordinal)
            || region.Text.Contains("补充原粹树脂", StringComparison.Ordinal));
    }

    private async Task MonitorRewardPromptTask(BvPage page, CancellationTokenSource navigationCts)
    {
        var ct = navigationCts.Token;
        var lastInteractAt = DateTime.MinValue;

        while (!ct.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();

            if (HasRewardPanelPrompt(page))
            {
                return;
            }

            if (HasRewardInteractionPrompt(page))
            {
                if (DateTime.UtcNow - lastInteractAt >= TimeSpan.FromMilliseconds(300))
                {
                    InputHub.Foreground.SimulateAction(GIActions.PickUpOrInteract);
                    lastInteractAt = DateTime.UtcNow;
                }
            }

            await _runtime.Delay(300, ct);
        }
    }

    private async Task AdjustRewardCameraTask(BvPage page, CancellationTokenSource navigationCts)
    {
        var ct = navigationCts.Token;
        var captureRect = _runtime.SystemInfo.ScaleMax1080PCaptureRect;
        //将图标控制在屏幕中间，大约截图宽度的45%-55%之间
        var minTargetX = captureRect.Width * 0.45;
        var maxTargetX = captureRect.Width * 0.55;
        var centerX = captureRect.Width / 2.0;
        var halfHeight = captureRect.Height / 2.0;

        while (!ct.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            var boxRegions = page.Locator(LoadRecognitionObject("RewardBox")).FindAll();
            if (boxRegions.Count < 1)
            {
                _logger.LogWarning("{Name}：未找到征讨之花图标，调整视角重试", TaskName);
                InputHub.Foreground.Mouse.MoveMouseBy(ScaleX(200), 0);
                await _runtime.Delay(250, ct);
                continue;
            }

            var icon = boxRegions[0];

            if (icon.Y > halfHeight)
            {
                InputHub.Foreground.Mouse.MoveMouseBy(0, (int)Math.Round(halfHeight));
                await _runtime.Delay(125, ct);
                InputHub.Foreground.Mouse.MoveMouseBy((int)Math.Round(centerX), 0);
                await _runtime.Delay(125, ct);
                continue;
            }

            if (icon.X < minTargetX || icon.X > maxTargetX)
            {
                InputHub.Foreground.Mouse.MoveMouseBy((int)Math.Round(icon.X - centerX), 0);
            }

            await _runtime.Delay(250, ct);
        }
    }

    private async Task MoveToRewardTask(BvPage page, CancellationTokenSource navigationCts)
    {
        var ct = navigationCts.Token;
        var climbRect = ScaleRect(1686, 1030, 60, 23);
        var jumpCount = 0;
        var isMovingForward = false;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ct.ThrowIfCancellationRequested();
                if (page.Locator("Space", climbRect).IsExist())
                {
                    if (isMovingForward)
                    {
                        InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                        isMovingForward = false;
                    }

                    _logger.LogInformation("{Name}：检测到攀爬状态，尝试脱离", TaskName);
                    InputHub.Foreground.SimulateAction(GIActions.Drop);
                    await _runtime.Delay(1000, ct);
                    InputHub.Foreground.SimulateAction(GIActions.MoveLeft, KeyType.KeyDown);
                    await _runtime.Delay(800, ct);
                    InputHub.Foreground.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
                    continue;
                }

                if (!isMovingForward)
                {
                    InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                    isMovingForward = true;
                }

                await _runtime.Delay(1000, ct);
                jumpCount++;
                if (jumpCount % 2 == 0)
                {
                    InputHub.Foreground.SimulateAction(GIActions.Jump);
                    await _runtime.Delay(100, ct);
                }

                InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
                isMovingForward = false;
                await _runtime.Delay(200, ct);
            }
        }
        finally
        {
            InputHub.Foreground.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            InputHub.Foreground.SimulateAction(GIActions.MoveLeft, KeyType.KeyUp);
        }
    }

    /// <summary>
    /// 与征讨之花交互，并在世界 Boss 专用领奖界面点击“使用原粹树脂”领取奖励。
    /// </summary>
    /// <returns>成功领取奖励并回到主界面时返回 true；原粹树脂不足或无法领取时返回 false。</returns>
    private async Task<bool> TakeReward()
    {
        var page = new BvPage(_runtime, _ct);

        if (!await TryUseOriginalResinOnRewardPrompt(page))
        {
            await CloseResinSupplementPrompt(page);
            await _returnMainUiTask.Start(_ct);
            return false;
        }

        // 世界首领奖励只消耗原粹，补充记录与领奖记录分开。
        _selector.RecordClaim(ResinKind.Original, 40);
        await TryRecognizeRewardResult(page);
        await CloseRewardResult();
        Notify.Event("AutoBoss").Success($"{TaskName}奖励领取");
        return true;
    }

    private async Task TryRecognizeRewardResult(BvPage page)
    {
        if (!_taskParam.RewardRecognitionEnabled && _taskParam.TargetItems.Count == 0)
        {
            return;
        }

        try
        {
            if (!await WaitForRewardResultReady(page))
            {
                _logger.LogWarning("{Name}：奖励结果页未检测到“点击空白区域继续”，已跳过本轮奖励识别", TaskName);
                return;
            }

            // 使用多页识别（自动检测是否需要翻页）
            _logger.LogInformation("{Name}：开始奖励识别", TaskName);
            var rewards = RewardResultRecognizer.Instance.RecognizeMultiPage(_runtime);

            if (rewards.Count == 0)
            {
                _logger.LogWarning("{Name}：本轮奖励识别结果为空", TaskName);
                return;
            }

            RewardResultRecognizer.MergeIntoSummary(_rewardSummary, rewards);
            _logger.LogInformation("{Name}：本轮奖励识别结果 {Rewards}", TaskName,
                string.Join(", ", rewards.Select(r => $"{r.Key} x{r.Value}")));

            if (_taskParam.TargetItems.Count > 0)
            {
                if (!TargetItemCatalog.Instance.TryGetBossProfile(_taskParam.BossName, out _))
                {
                    _logger.LogWarning("{Name}：当前首领没有奖励数据，跳过材料目标进度计算", TaskName);
                    return;
                }

                var progress = TargetItemCatalog.Instance.CalculateBoss(_taskParam.TargetItems, _rewardSummary);
                _itemStopReached = progress.AllSatisfied;
                LogTargetProgress("材料目标", progress);

                if (_itemStopReached)
                {
                    _logger.LogInformation("{Name}：材料目标已全部达成", TaskName);
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "{Name}：奖励识别失败，已跳过本轮奖励汇总", TaskName);
        }
    }

    /// <summary>
    /// 打印一组材料目标的进度。
    /// </summary>
    private void LogTargetProgress(string label, TargetProgress progress)
    {
        _logger.LogInformation("{Name}：{Label}进度 {Targets}", TaskName, label,
            string.Join("，", progress.Targets.Select(x => $"{x.ItemName} {x.CountedCount}/{x.TargetCount}")));
    }

    private async Task<bool> WaitForRewardResultReady(BvPage page)
    {
        var closeRect = ScaleRect(850, 960, 220, 35);

        for (var i = 0; i < 20; i++)
        {
            var closeRegion = page.Ocr(closeRect)
                .FirstOrDefault(r => r.Text.Contains("点击空白区域继续", StringComparison.Ordinal));
            if (closeRegion != null)
            {
                return true;
            }

            await _runtime.Delay(300, _ct);
        }

        return false;
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
            await _runtime.Delay(1000, _ct);
            return true;
        }
        catch (TimeoutException e)
        {
            var supplementRegions = await page.Locator("补充原粹树脂", useRect).TryWaitFor(1000);
            if (supplementRegions.Count > 0)
            {
                _stopReason = ResinStopReason.ResinExhausted;
                _logger.LogInformation("{Name}：领奖界面提示补充原粹树脂，当前原粹树脂不足", TaskName);
            }
            else
            {
                _stopReason = ResinStopReason.NoUsableOption;
                _logger.LogWarning("{Name}：未能在世界 Boss 领奖界面点击“使用原粹树脂”，原因：{Reason}", TaskName, e.Message);
            }

            return false;
        }
    }

    /// <summary>
    /// 关闭补充原粹树脂提示
    /// </summary>
    private async Task CloseResinSupplementPrompt(BvPage page)
    {
        var supplementRect = ScaleRect(850, 730, 250, 60);

        for (var i = 0; i < 50; i++)
        {
            _ct.ThrowIfCancellationRequested();
            var promptExists = page.Ocr(supplementRect).Any(region =>
                region.Text.Contains("补充", StringComparison.Ordinal)
                || region.Text.Contains("原粹", StringComparison.Ordinal)
                || region.Text.Contains("树脂", StringComparison.Ordinal));
            if (!promptExists)
            {
                return;
            }

            page.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
            await page.Wait(300);
        }

        throw new TimeoutException("关闭补充原粹树脂提示超时");
    }

    /// <summary>
    /// 关闭领奖结果页面，等待回到主界面。
    /// </summary>
    /// <exception cref="Exception">多次尝试后仍未回到主界面时抛出。</exception>
    private async Task CloseRewardResult()
    {
        var page = new BvPage(_runtime, _ct);
        var closeRect = ScaleRect(850, 960, 220, 35);

        for (var i = 0; i < 20; i++)
        {
            using var capture = _runtime.CaptureToRectArea();
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

            await _runtime.Delay(300, _ct);
        }

        using var finalCapture = _runtime.CaptureToRectArea();
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
            _logger.LogInformation("{Name}：战后重新靠近交互点", TaskName);
            await RunPathingFile($"{_taskParam.BossName}战斗后快速前往.json");
            return;
        }

        if (AutoBossData.IsNoPathingSupportBoss(_taskParam.BossName))
        {
            _logger.LogInformation("{Name}：重新执行特殊路线靠近首领", TaskName);
            await NavigateToBoss();
            return;
        }

        if (AutoBossData.ShouldRerunRoute(_taskParam.BossName))
        {
            _logger.LogInformation("{Name}：重新执行完整路线靠近首领", TaskName);
            await RunPathingFile($"{_taskParam.BossName}前往.json");
            return;
        }

        _logger.LogInformation("{Name}：重新靠近首领位置并等待 4 秒", TaskName);
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
        await _runtime.Delay(4000, _ct);
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
