using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Input;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.Runtime;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.ResinPlan;

/// <summary>
/// 体力计划的活动识别：打开活动页（F5），按住 W 回到列表顶部（新活动都在顶部），
/// 用 OCR 读取活动列表判断移涌活动，并逐个点开读取“今日剩余多倍产出次数”；
/// 再切换到幽境危战页面判断紊乱爆发期是否仍在进行。
/// 只支持简体中文界面；坐标基于 1080P 参考画布，经 <see cref="ReferenceRect"/> 锚定缩放到当前分辨率。结束后不负责关闭活动页，由调用方回到主界面。
/// </summary>
public sealed class ResinPlanEventDetector
{
    /// <summary>日志中使用的名称。</summary>
    private const string Name = "体力计划·活动识别";

    /// <summary>地脉移涌的关键词。</summary>
    private static readonly string[] LeyLineKeywords = ["地脉移涌"];

    /// <summary>盛材移涌的关键词，包含旧名精通移涌。</summary>
    private static readonly string[] MaterialKeywords = ["盛材移涌", "精通移涌"];

    /// <summary>祝圣移涌的关键词；列表中出现即认为活动存在。</summary>
    private static readonly string[] BlessingKeywords = ["祝圣移涌"];

    /// <summary>按识别顺序排列的移涌活动及其关键词。</summary>
    private static readonly (ResinPlanOverflowEvent Event, string[] Keywords)[] OverflowKeywords =
    [
        (ResinPlanOverflowEvent.LeyLineOverflow, LeyLineKeywords),
        (ResinPlanOverflowEvent.MaterialOverflow, MaterialKeywords),
        (ResinPlanOverflowEvent.BlessingOverflow, BlessingKeywords)
    ];

    /// <summary>移涌活动详情中剩余双倍次数提示的关键词。</summary>
    private const string RemainingKeyword = "多倍产出";

    /// <summary>当天双倍次数已用完时的文字。</summary>
    private const string RemainingUsedUpKeyword = "已用完";

    /// <summary>“剩余/上限”格式的次数，例如 3/3。</summary>
    private static readonly Regex RemainingPattern = new(@"(\d+)\s*[/／]\s*(\d+)", RegexOptions.Compiled);

    /// <summary>幽境危战在活动列表与详情标题中的名称。</summary>
    private const string StygianKeyword = "幽境危战";

    /// <summary>紊乱爆发期提示的关键词。</summary>
    private const string BurstKeyword = "紊乱爆发期";

    /// <summary>紊乱爆发期结束时与关键词同时出现的文字。</summary>
    private const string BurstEndedKeyword = "已结束";

    /// <summary>按住 W 回到列表顶部的时长（毫秒）。</summary>
    private const int ScrollToTopMs = 4000;

    /// <summary>列表顶部看不到幽境危战时，按 S 逐项向下查找的最多次数。</summary>
    private const int MaxStepDown = 15;

    // 以下区域均为 1080P 参考画布坐标，使用时经 ReferenceRect 按 Auto 锚定换算到当前截图

    /// <summary>左上角“活动一览”标题区域（1080P）。</summary>
    private static readonly Rect MenuTitleRoi = new(125, 142, 113, 28);

    /// <summary>左侧活动列表区域（1080P）。</summary>
    private static readonly Rect ListRoi = new(195, 201, 296, 654);

    /// <summary>右侧活动详情主标题区域（1080P）。</summary>
    private static readonly Rect DetailTitleRoi = new(1135, 278, 265, 75);

    /// <summary>危战页面中部“紊乱爆发期”提示区域（1080P），与自动幽境危战的判断位置一致。</summary>
    private static readonly Rect BurstRoi = new(614, 734, 307, 108);

    /// <summary>移涌活动详情中“今日剩余多倍产出次数”所在横条（1080P）。</summary>
    private static readonly Rect RemainingRoi = new(1140, 695, 580, 85);

    /// <summary>本次运行使用的运行环境。</summary>
    private readonly GameRuntime _runtime;

    /// <summary>创建活动识别。</summary>
    /// <param name="runtime">本次运行使用的运行环境。</param>
    public ResinPlanEventDetector(GameRuntime runtime)
    {
        _runtime = runtime;
    }

    /// <summary>
    /// 识别当前开放的移涌活动和紊乱爆发期。业务异常只记录在结果中，取消与运行环境停止向外抛出。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>识别结果；活动页打不开或列表读不到时 <see cref="ResinPlanEventReport.Recognized"/> 为 false。</returns>
    public async Task<ResinPlanEventReport> DetectAsync(CancellationToken ct)
    {
        var report = new ResinPlanEventReport();
        try
        {
            // 1. 从主界面打开活动页
            await new ReturnMainUiTask(_runtime).Start(ct);
            if (!await OpenEventsMenuAsync(ct))
            {
                report.Message = "未能打开活动页。";
                Logger.LogWarning("{Name}：{Message}", Name, report.Message);
                return report;
            }

            // 2. 按住 W 回到列表顶部，新活动排在最上面
            await ScrollToTopAsync(ct);

            // 3. 读取列表判断移涌活动
            List<string> texts;
            using (var ra = _runtime.CaptureToRectArea())
            {
                texts = ra.FindMulti(RecognitionObject.Ocr(ra.ReferenceRoi(ListRoi))).Select(item => item.Text).ToList();
            }

            if (texts.Count == 0)
            {
                report.Message = "活动列表没有识别到文字。";
                Logger.LogWarning("{Name}：{Message}", Name, report.Message);
                return report;
            }

            report.Recognized = true;
            Logger.LogInformation("{Name}：活动列表 {Items}", Name, string.Join("、", texts));

            // 4. 逐个点开识别到的移涌活动，读取今日剩余双倍次数
            foreach (var (kind, keywords) in OverflowKeywords)
            {
                if (!texts.Any(text => keywords.Any(text.Contains)))
                {
                    continue;
                }

                var status = new ResinPlanOverflowStatus { Event = kind };
                if (await OpenListItemAsync(keywords, ct))
                {
                    (status.RemainingClaims, status.DailyClaims) = await ReadRemainingClaimsAsync(ct);
                }

                report.Overflows.Add(status);
                Logger.LogInformation("{Name}：{Event} 今日剩余双倍次数 {Remaining}", Name, DisplayName(kind),
                    FormatRemaining(status));
            }

            // 5. 切换到幽境危战页面，读取紊乱爆发期提示
            report.StygianBurst = await DetectStygianBurstAsync(ct);
            Logger.LogInformation("{Name}：移涌活动 {Overflows}；幽境危战紊乱爆发期 {Burst}", Name,
                report.Overflows.Count == 0 ? "无" : string.Join("、", report.Overflows.Select(o => DisplayName(o.Event))),
                DisplayName(report.StygianBurst));
            return report;
        }
        catch (Exception e) when (e is not NormalEndException and not OperationCanceledException)
        {
            report.Recognized = false;
            report.Message = e.Message;
            Logger.LogWarning("{Name}：识别失败：{Message}", Name, e.Message);
            return report;
        }
    }

    /// <summary>移涌活动的中文名称。</summary>
    /// <param name="value">移涌活动。</param>
    /// <returns>中文名称。</returns>
    public static string DisplayName(ResinPlanOverflowEvent value) => value switch
    {
        ResinPlanOverflowEvent.LeyLineOverflow => "地脉移涌",
        ResinPlanOverflowEvent.MaterialOverflow => "盛材移涌",
        ResinPlanOverflowEvent.BlessingOverflow => "祝圣移涌",
        _ => value.ToString()
    };

    /// <summary>紊乱爆发期状态的中文说明。</summary>
    /// <param name="value">紊乱爆发期状态。</param>
    /// <returns>中文说明。</returns>
    public static string DisplayName(StygianBurstState value) => value switch
    {
        StygianBurstState.Active => "进行中",
        StygianBurstState.Ended => "已结束",
        StygianBurstState.NotFound => "未找到幽境危战",
        _ => "未识别"
    };

    /// <summary>按 F5 打开活动页，等待左上角出现“活动一览”；第一次没打开时再按一次。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否已打开活动页。</returns>
    private async Task<bool> OpenEventsMenuAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            InputHub.Foreground.SimulateAction(GIActions.OpenTheEventsMenu);
            // 活动页有打开动画，每 300 毫秒检查一次，最多约 3 秒
            for (var i = 0; i < 10; i++)
            {
                await _runtime.Delay(300, ct);
                if (ContainsText(MenuTitleRoi, "活动一览"))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>按住 W 让选中项回到列表顶部，松开后等待列表停稳。</summary>
    /// <param name="ct">取消令牌。</param>
    private async Task ScrollToTopAsync(CancellationToken ct)
    {
        InputHub.Foreground.Keyboard.KeyDown(User32.VK.VK_W);
        try
        {
            await _runtime.Delay(ScrollToTopMs, ct);
        }
        finally
        {
            // 取消时也要松开按键，避免回到主界面后角色一直前进
            InputHub.Foreground.Keyboard.KeyUp(User32.VK.VK_W);
        }

        await _runtime.Delay(500, ct);
    }

    /// <summary>
    /// 在当前可见的活动列表中点击包含关键词的一项，并等待右侧详情标题切换到该活动。
    /// 每次点击前重新截图，避免列表因前一次点击滚动后坐标失效。
    /// </summary>
    /// <param name="keywords">活动名称关键词。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否已切换到该活动的详情。</returns>
    private async Task<bool> OpenListItemAsync(string[] keywords, CancellationToken ct)
    {
        using (var ra = _runtime.CaptureToRectArea())
        {
            var item = ra.FindMulti(RecognitionObject.Ocr(ra.ReferenceRoi(ListRoi)))
                .FirstOrDefault(region => keywords.Any(region.Text.Contains));
            if (item is null)
            {
                return false;
            }

            item.Click();
        }

        return await WaitDetailTitleAsync(keywords, ct);
    }

    /// <summary>
    /// 读取移涌活动详情中的“今日剩余多倍产出次数”：显示“已用完”记为 0，显示“剩余/上限”时取两个数。
    /// 详情有切换动画，读不到时稍后重试，仍读不到返回 null。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>剩余次数与每日上限；未读取到的值为 null。</returns>
    private async Task<(int? Remaining, int? Daily)> ReadRemainingClaimsAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await _runtime.Delay(300, ct);
            List<string> texts;
            using (var ra = _runtime.CaptureToRectArea())
            {
                texts = ra.FindMulti(RecognitionObject.Ocr(ra.ReferenceRoi(RemainingRoi))).Select(item => item.Text).ToList();
            }

            // 提示文字与次数可能被识别为两段，合并后再判断
            var joined = string.Concat(texts);
            Logger.LogDebug("{Name}：剩余双倍次数区域 {Texts}", Name, string.Join("、", texts));
            if (!joined.Contains(RemainingKeyword))
            {
                continue;
            }

            if (joined.Contains(RemainingUsedUpKeyword))
            {
                return (0, null);
            }

            if (RemainingPattern.Match(joined) is { Success: true } match)
            {
                return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
            }
        }

        return (null, null);
    }

    /// <summary>剩余双倍次数的日志文字。</summary>
    /// <param name="status">移涌活动识别结果。</param>
    /// <returns>例如“3/3”“0（已用完）”“未识别”。</returns>
    public static string FormatRemaining(ResinPlanOverflowStatus status) => status switch
    {
        { RemainingClaims: null } => "未识别",
        { RemainingClaims: 0, DailyClaims: null } => "0（已用完）",
        { DailyClaims: { } daily } => $"{status.RemainingClaims}/{daily}",
        _ => status.RemainingClaims.ToString()!
    };

    /// <summary>
    /// 切换到幽境危战页面并读取紊乱爆发期提示。列表中看不到危战时，回到列表顶部后按 S 逐项向下，直到详情标题为幽境危战。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>紊乱爆发期状态。</returns>
    private async Task<StygianBurstState> DetectStygianBurstAsync(CancellationToken ct)
    {
        // 1. 切换到危战详情：列表可见时直接点击，否则从顶部逐项向下
        string[] keywords = [StygianKeyword];
        var opened = await OpenListItemAsync(keywords, ct);
        if (!opened)
        {
            // 前面点开移涌活动后选中项可能已不在顶部，先回到顶部再向下查找
            await ScrollToTopAsync(ct);
        }

        for (var step = 0; !opened && step < MaxStepDown; step++)
        {
            InputHub.Foreground.Keyboard.KeyPress(User32.VK.VK_S);
            await _runtime.Delay(400, ct);
            opened = ContainsText(DetailTitleRoi, keywords);
        }

        if (!opened)
        {
            Logger.LogWarning("{Name}：活动列表中没有找到幽境危战", Name);
            return StygianBurstState.NotFound;
        }

        // 2. 详情切换有过渡动画，稍等后读取提示
        await _runtime.Delay(500, ct);
        using var ra = _runtime.CaptureToRectArea();
        var texts = ra.FindMulti(RecognitionObject.Ocr(ra.ReferenceRoi(BurstRoi))).Select(item => item.Text).ToList();
        var joined = string.Concat(texts);
        Logger.LogDebug("{Name}：危战提示区域 {Texts}", Name, string.Join("、", texts));
        if (!joined.Contains(BurstKeyword))
        {
            return StygianBurstState.Unknown;
        }

        return joined.Contains(BurstEndedKeyword) ? StygianBurstState.Ended : StygianBurstState.Active;
    }

    /// <summary>点击列表项后等待右侧标题变为指定活动，最多约 3 秒。</summary>
    /// <param name="keywords">活动名称关键词。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否已切换到该活动的详情。</returns>
    private async Task<bool> WaitDetailTitleAsync(string[] keywords, CancellationToken ct)
    {
        for (var i = 0; i < 10; i++)
        {
            await _runtime.Delay(300, ct);
            if (ContainsText(DetailTitleRoi, keywords))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>截一帧并判断指定区域是否包含任一关键词。</summary>
    /// <param name="roi">1080P 下的区域。</param>
    /// <param name="keywords">要查找的文字。</param>
    /// <returns>是否包含。</returns>
    private bool ContainsText(Rect roi, params string[] keywords)
    {
        using var ra = _runtime.CaptureToRectArea();
        return ra.FindMulti(RecognitionObject.Ocr(ra.ReferenceRoi(roi))).Any(item => keywords.Any(item.Text.Contains));
    }
}
