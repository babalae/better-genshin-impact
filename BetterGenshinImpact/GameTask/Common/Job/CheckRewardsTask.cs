using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoTrackPath;
using BetterGenshinImpact.GameTask.Model.Area;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Service.Notification;
using BetterGenshinImpact.Service.Notification.Model.Enum;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using static BetterGenshinImpact.GameTask.Common.TaskControl;


namespace BetterGenshinImpact.GameTask.Common.Job;

/// <summary>
/// 检查奖励并通知的任务
/// </summary>
public class CheckRewardsTask
{
    private readonly ILogger<CheckRewardsTask> _logger = App.GetLogger<CheckRewardsTask>();

    private readonly string _dailyRewardsClaimedLocalizedString;
    private readonly string _dailyCommissionRewardsString;
    private readonly string _commissionsButtonString;

    public CheckRewardsTask()
    {
        IStringLocalizer<CheckRewardsTask> stringLocalizer = App.GetService<IStringLocalizer<CheckRewardsTask>>() ?? throw new NullReferenceException();
        CultureInfo cultureInfo = new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName);
        this._dailyRewardsClaimedLocalizedString = stringLocalizer.WithCultureGet(cultureInfo, "今日奖励已领取");
        this._dailyCommissionRewardsString = stringLocalizer.WithCultureGet(cultureInfo, "每日委托奖励");
        this._commissionsButtonString = stringLocalizer.WithCultureGet(cultureInfo, "委托");
    }

    public string Name => "检查奖励并通知的任务";

    private static RecognitionObject GetConfirmRa(bool isOcrMatch = false,params string[] targetText)
    {
        using var screenArea = CaptureToRectArea();
        var x = (int)(screenArea.Width * 0.1);
        var y = (int)(screenArea.Height * 0.1);
        var width = (int)(screenArea.Width * 0.3);
        var height = (int)(screenArea.Height * 0.7);

        return isOcrMatch ? RecognitionObject.OcrMatch(x, y, width, height, targetText) :
            RecognitionObject.Ocr(x, y, width, height);
    }

    public async Task Start(CancellationToken ct)
    {
        try
        {
            if (TaskContext.Instance().Config.NotificationConfig.DragonEndSummaryEnabled)
            {
                await StartSummaryAsync(ct);
            }
            else
            {
                await StartOriginalAsync(ct);
            }
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "检查奖励并通知的任务异常");
            Logger.LogError("检查奖励并通知的任务异常: {Msg}", e.Message);
        }
    }

    private async Task StartOriginalAsync(CancellationToken ct)
    {
        await new ReturnMainUiTask().Start(ct);

        _ = await NewRetry.WaitForElementAppear(
            GetConfirmRa(true,_dailyCommissionRewardsString),
            ()=>
            {
                Simulation.SendInput.SimulateAction(GIActions.OpenAdventurerHandbook);
                using var screen = CaptureToRectArea();
                var ra = screen.FindMulti(GetConfirmRa())
                    .FirstOrDefault(btn => Regex.IsMatch(btn.Text.Trim(), $"^(?:{_commissionsButtonString})$", RegexOptions.IgnoreCase));
                    ra?.Click();
            },ct,4,1000);

        // OCR识别每日是否完成
        var done = await NewRetry.WaitForElementAppear(
            GetConfirmRa(true,_dailyRewardsClaimedLocalizedString),null,
            ct,4,500);
        if (done)
        {
            Logger.LogInformation("检查每日奖励结果：{Msg}", "今日奖励已领取");
            Notify.Event(NotificationEvent.DailyReward).Success("检查每日奖励：已领取");
        }
        else
        {
            Logger.LogWarning("检查每日奖励结果：{Msg}，请手动检查！", "未领取");
            Notify.Event(NotificationEvent.DailyReward).Error("检查到每日奖励未领取，请手动查看！");
        }
        await Delay(200, ct);
        await new ReturnMainUiTask().Start(ct);
    }

    /// <summary>
    /// 一条龙结束汇总：打开大地图识别原粹树脂、打开冒险之证识别委托奖励领取状态，
    /// 两块截图上下拼接后随通知发送。任何一步失败只降级文本内容，不中断流程。
    /// </summary>
    private async Task StartSummaryAsync(CancellationToken ct)
    {
        var assetScale = TaskContext.Instance().SystemInfo.AssetScale;

        // 体力：打开大地图识别顶栏原粹树脂并截取图标+数字条
        var resinOk = false;
        var resinText = "体力识别失败";
        Mat? resinStrip = null;
        try
        {
            await new TpTask(ct).OpenBigMapUi();
            await Delay(300, ct);
            using var capture = CaptureToRectArea();
            var resin = ResinRecognition.RecognizeInBigMapTopBar(capture);
            if (resin != null)
            {
                var result = resin.Value;
                resinOk = result.Condensed.HasValue;
                var condensedText = result.Condensed is int condensed
                    ? $"浓缩树脂 {condensed}"
                    : "浓缩树脂识别失败";
                resinText = $"{condensedText}；原粹树脂 {result.Current}/{result.Max}";
                using var mapCloseButton = capture.Find(RecognitionAssets.Get(
                    "QuickTeleport", "MapCloseButton", capture));
                if (mapCloseButton.IsEmpty())
                {
                    Logger.LogWarning("汇总通知：大地图关闭按钮识别失败，树脂截图使用默认右边界");
                }

                using var resinStripRegion = capture.DeriveCrop(BuildResinStripRect(
                    result.OriginalIconRect, result.CondensedIconRect,
                    mapCloseButton.IsEmpty() ? null : mapCloseButton.Left, assetScale));
                resinStrip = resinStripRegion.SrcMat.Clone();
                if (!result.Condensed.HasValue)
                {
                    Logger.LogWarning("汇总通知：大地图浓缩树脂识别失败");
                }
            }
            else
            {
                Logger.LogWarning("汇总通知：大地图原粹树脂识别失败");
            }
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "汇总通知：读取体力异常");
            Logger.LogWarning("汇总通知：读取体力失败: {Msg}", e.Message);
        }
        finally
        {
            await SafeReturnMainUiAsync(ct);
        }

        // 委托：打开冒险之证委托页识别奖励领取状态并截取左侧页签+奖励区域
        bool? commissionClaimed = null;
        var commissionText = "委托状态识别失败";
        Mat? commissionStrip = null;
        try
        {
            await new ReturnMainUiTask().Start(ct);
            await Delay(200, ct);

            var opened = await NewRetry.WaitForElementAppear(
                GetConfirmRa(true, "每日委托奖励"),
                () =>
                {
                    Simulation.SendInput.SimulateAction(GIActions.OpenAdventurerHandbook);
                    using var screen = CaptureToRectArea();
                    var ra = screen.FindMulti(GetConfirmRa())
                        .FirstOrDefault(btn => btn.Text == "委托");
                    ra?.Click();
                }, ct, 4, 1000);

            if (!opened)
            {
                Logger.LogWarning("汇总通知：打开冒险之证委托页失败");
            }
            else
            {
                commissionClaimed = await NewRetry.WaitForElementAppear(
                    GetConfirmRa(true, _dailyRewardsClaimedLocalizedString), null,
                    ct, 4, 500);
                commissionText = commissionClaimed == true ? "每日委托奖励：已领取" : "每日委托奖励：未领取";

                using var handbookCapture = CaptureToRectArea();
                commissionStrip = handbookCapture.DeriveCrop(new Rect(
                    (int)(366 * assetScale), (int)(161 * assetScale),
                    (int)(1260 * assetScale), (int)(749 * assetScale))).SrcMat.Clone();
            }
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "汇总通知：读取委托状态异常");
            Logger.LogWarning("汇总通知：读取委托状态失败: {Msg}", e.Message);
        }
        finally
        {
            await SafeReturnMainUiAsync(ct);
        }

        // 拼接两块截图：上下排列、等宽白底补齐、中间灰色分隔线
        Image<Rgb24>? stitched = null;
        try
        {
            stitched = StitchStrips(resinStrip, commissionStrip);
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "汇总通知：拼接截图异常");
            Logger.LogWarning("汇总通知：拼接截图失败: {Msg}", e.Message);
        }
        finally
        {
            resinStrip?.Dispose();
            commissionStrip?.Dispose();
        }

        var message = $"{resinText}；{commissionText}";
        Logger.LogInformation("一条龙结束汇总：{Msg}", message);

        var data = Notify.Event(NotificationEvent.DailyReward);
        data.Screenshot = stitched;
        if (!resinOk || commissionClaimed == null)
        {
            data.Result = NotificationEventResult.PartialSuccess;
            data.Send(message);
        }
        else if (commissionClaimed == true)
        {
            data.Success(message);
        }
        else
        {
            data.Fail(message);
        }
    }

    /// <summary>
    /// 以两种树脂图标为锚点，构造覆盖浓缩树脂与原粹树脂的截图条矩形。
    /// </summary>
    private static Rect BuildResinStripRect(
        Rect originalIconRect, Rect? condensedIconRect, int? mapCloseButtonLeft, double assetScale)
    {
        var left = condensedIconRect is Rect condensed
            ? Math.Max(0, condensed.Left - (int)(25 * assetScale))
            : Math.Max(0, originalIconRect.Left - (int)(200 * assetScale));
        var topIcon = condensedIconRect is Rect topCondensed
            ? Math.Min(originalIconRect.Top, topCondensed.Top)
            : originalIconRect.Top;
        var bottomIcon = condensedIconRect is Rect bottomCondensed
            ? Math.Max(originalIconRect.Bottom, bottomCondensed.Bottom)
            : originalIconRect.Bottom;
        var top = Math.Max(0, topIcon - (int)(15 * assetScale));
        var right = mapCloseButtonLeft is int closeButtonLeft
            ? closeButtonLeft - (int)(10 * assetScale)
            : originalIconRect.Right + (int)(165 * assetScale);
        var bottom = bottomIcon + (int)(15 * assetScale);
        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// 上下拼接两块截图，等宽白底补齐并在中间画灰色分隔线；只有一块时直接返回该块。
    /// </summary>
    private static Image<Rgb24>? StitchStrips(Mat? top, Mat? bottom)
    {
        if (top == null && bottom == null)
        {
            return null;
        }

        if (top == null || bottom == null)
        {
            using var single = (top ?? bottom)!.Clone();
            using var singleRegion = new ImageRegion(single, 0, 0);
            return singleRegion.CacheImage.Clone();
        }

        var width = Math.Max(top.Width, bottom.Width);
        using var paddedTop = PadWhiteToWidth(top, width);
        using var paddedBottom = PadWhiteToWidth(bottom, width);
        Cv2.Line(paddedTop, 0, paddedTop.Rows - 1, paddedTop.Cols - 1, paddedTop.Rows - 1, new Scalar(128, 128, 128), 2);
        using var result = new Mat();
        Cv2.VConcat(paddedTop, paddedBottom, result);
        using var resultRegion = new ImageRegion(result, 0, 0);
        return resultRegion.CacheImage.Clone();
    }

    private static Mat PadWhiteToWidth(Mat src, int width)
    {
        var left = (width - src.Width) / 2;
        var right = width - src.Width - left;
        var padded = new Mat();
        Cv2.CopyMakeBorder(src, padded, 0, 0, left, right, BorderTypes.Constant, Scalar.White);
        return padded;
    }

    private static async Task SafeReturnMainUiAsync(CancellationToken ct)
    {
        try
        {
            await new ReturnMainUiTask().Start(ct);
        }
        catch (Exception e)
        {
            Logger.LogDebug(e, "汇总通知：回到主界面失败");
        }
    }
}
