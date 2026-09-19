using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.GameTask.QuickSereniteaPot;
using BetterGenshinImpact.Helpers;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

public partial class TpTask
{
    // 长等待只供尘歌壶入口使用，普通 SwitchArea 保留原来的时序和重试行为。
    internal async Task<bool> TrySwitchSereniteaPotArea(Action<string, ImageRegion>? saveFrame = null)
    {
        const string areaName = "尘歌壶";
        await SereniteaPotTaskControl.Delay(0, ct);
        GameCaptureRegion.GameRegionClick((rect, scale) => (rect.Width - 160 * scale, rect.Height - 60 * scale));
        var localizedName = stringLocalizer.WithCultureGet(cultureInfo, areaName);
        Rect? candidateRect = null;
        Rect? clickRect = null;
        var sample = 0;
        var watch = Stopwatch.StartNew();
        var found = await MapAreaSwitchWaiter.WaitAsync(() =>
        {
            using var capture = CaptureToRectArea(forceNew: true);
            var candidates = FindSwitchAreaCandidates(capture);
            var match = candidates.OrderByDescending(r => r.Y)
                .FirstOrDefault(r => IsSwitchAreaCandidateMatch(r.Text, localizedName, areaName));
            if (match != null)
            {
                candidateRect = new Rect(match.X, match.Y, match.Width, match.Height);
                clickRect = match.ConvertPositionToGameCaptureRegion(0, 0, match.Width, match.Height);
            }
            Logger.LogInformation("尘歌壶区域菜单 #{Sample}，耗时={Elapsed}ms，候选={Candidates}",
                ++sample, watch.ElapsedMilliseconds, FormatSwitchAreaCandidateTexts(candidates));
            if (sample <= 3 || match != null) saveFrame?.Invoke($"area-candidates-{sample}", capture);
            return match != null;
        }, SereniteaPotTaskControl.Delay, ct);
        if (!found || candidateRect == null || clickRect == null) return false;

        ct.ThrowIfCancellationRequested();
        var click = clickRect.Value;
        GameCaptureRegion.GameRegionClick((_, _) => (click.X + click.Width / 2d, click.Y + click.Height / 2d));
        sample = 0;
        var applied = await MapAreaSwitchWaiter.WaitAsync(() =>
        {
            using var capture = CaptureToRectArea(forceNew: true);
            var stillVisible = FindSwitchAreaCandidates(capture).Any(candidate =>
                IsSwitchAreaCandidateMatch(candidate.Text, localizedName, areaName) &&
                IsSameSwitchAreaCandidatePosition(candidateRect.Value, candidate));
            if (++sample <= 3) saveFrame?.Invoke($"area-applied-{sample}", capture);
            return !stillVisible && Bv.IsInBigMapUi(capture);
        }, SereniteaPotTaskControl.Delay, ct, SwitchAreaSelectionStableChecks);
        if (!applied) return false;
        RememberAreaSwitchCenterPoint(areaName);
        Logger.LogInformation("切换到区域：{Country}", areaName);
        return true;
    }
}
