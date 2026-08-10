using System.Threading;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Party;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoFight.Model;

/// <summary>
/// Detects UI states that interrupt an active combat session.
/// This belongs to combat orchestration and must not be called by <see cref="Avatar"/>.
/// </summary>
public static class CombatStateDetector
{
    public static void ThrowIfInterrupted(CancellationToken ct, bool detectSwimming)
    {
        using var region = CaptureToRectArea();
        ThrowIfInterrupted(region, ct, detectSwimming);
    }

    public static void ThrowIfInterrupted(ImageRegion region, CancellationToken ct, bool detectSwimming)
    {
        if (Bv.IsInRevivePrompt(region))
        {
            Logger.LogWarning("战斗过程检测到复苏界面，结束当前战斗并交由任务编排层处理");
            throw new CombatInterruptionException(
                CombatInterruptionReason.Defeated,
                "检测到复苏界面，存在角色被击败");
        }

        if (!detectSwimming || !SwimmingConfirm(region))
        {
            return;
        }

        // 二次确认，避免单帧颜色误判。
        Delay(800, ct).GetAwaiter().GetResult();
        using var confirmRegion = CaptureToRectArea();
        if (!SwimmingConfirm(confirmRegion))
        {
            return;
        }

        Logger.LogWarning("战斗过程检测到游泳");
        throw new CombatInterruptionException(
            CombatInterruptionReason.Swimming,
            "战斗过程检测到游泳");
    }

    private static bool SwimmingConfirm(ImageRegion region)
    {
        using var cropped = region.DeriveCrop(1819, 1025, 9, 11);
        using var mask = OpenCvCommonHelper.Threshold(
            cropped.SrcMat,
            new Scalar(242, 223, 39),
            new Scalar(255, 233, 44));
        using var labels = new Mat();
        using var stats = new Mat();
        using var centroids = new Mat();

        var numLabels = Cv2.ConnectedComponentsWithStats(
            mask,
            labels,
            stats,
            centroids,
            connectivity: PixelConnectivity.Connectivity4,
            ltype: MatType.CV_32S);

        return numLabels > 1;
    }
}
