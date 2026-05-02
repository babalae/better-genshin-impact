using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.Common.BgiVision;

public enum ChatUiState
{
    Closed,
    PanelOpen,
    InputOpen
}

public readonly record struct ChatUiDetectionResult(
    ChatUiState State,
    bool HasBackButton,
    bool HasMoreButton,
    bool HasAddConversationButton,
    int BottomCircleCount,
    bool HasSendButton)
{
    public bool HasInputControls => BottomCircleCount >= 2 || HasSendButton;

    public string ToDebugSummary()
    {
        return $"back={HasBackButton}, more={HasMoreButton}, add={HasAddConversationButton}, circles={BottomCircleCount}, send={HasSendButton}";
    }
}

public static partial class Bv
{
    public static ChatUiDetectionResult DetectChatUi(ImageRegion region)
    {
        using var backButton = region.Find(ElementAssets.Instance.ChatBackButtonRo);
        var hasBackButton = backButton.IsExist();
        var hasMoreButton = HasChatMoreButton(region);
        var hasAddConversationButton = HasChatAddConversationButton(region);
        var bottomCircleCount = CountChatBottomCircleButtons(region);
        var hasSendButton = HasChatSendButton(region);
        var hasInputControls = bottomCircleCount >= 2 || hasSendButton;

        if (!hasBackButton || !hasAddConversationButton)
        {
            return new ChatUiDetectionResult(
                ChatUiState.Closed,
                hasBackButton,
                hasMoreButton,
                hasAddConversationButton,
                bottomCircleCount,
                hasSendButton);
        }

        if (hasInputControls)
        {
            return new ChatUiDetectionResult(
                ChatUiState.InputOpen,
                hasBackButton,
                hasMoreButton,
                hasAddConversationButton,
                bottomCircleCount,
                hasSendButton);
        }

        var state = hasMoreButton ? ChatUiState.PanelOpen : ChatUiState.Closed;
        return new ChatUiDetectionResult(
            state,
            hasBackButton,
            hasMoreButton,
            hasAddConversationButton,
            bottomCircleCount,
            hasSendButton);
    }

    public static ChatUiState DetectChatUiState(ImageRegion region)
    {
        return DetectChatUi(region).State;
    }

    private static bool HasChatMoreButton(ImageRegion region)
    {
        var scale = GetChatUiScale(region);
        using var roi = region.DeriveCrop(region.Width - (int)Math.Round(280 * scale), 0, (int)Math.Round(250 * scale), (int)Math.Round(140 * scale));
        return HasEllipsisDots(roi.SrcMat, scale, detectDarkDots: true) || HasEllipsisDots(roi.SrcMat, scale, detectDarkDots: false);
    }

    private static bool HasChatAddConversationButton(ImageRegion region)
    {
        var scale = GetChatUiScale(region);
        using var roi = region.DeriveCrop(0, region.Height - (int)Math.Round(260 * scale), (int)Math.Round(320 * scale), (int)Math.Round(260 * scale));
        return HasBrightRoundedButton(roi.SrcMat, scale, minWidth: 28, maxWidth: 92, minHeight: 28, maxHeight: 92, minAspect: 0.72, maxAspect: 1.28);
    }

    private static int CountChatBottomCircleButtons(ImageRegion region)
    {
        var scale = GetChatUiScale(region);
        using var roi = region.DeriveCrop((int)Math.Round(620 * scale), region.Height - (int)Math.Round(220 * scale), (int)Math.Round(760 * scale), (int)Math.Round(180 * scale));
        return CountBrightRoundedButtons(roi.SrcMat, scale, minWidth: 26, maxWidth: 92, minHeight: 26, maxHeight: 92, minAspect: 0.72, maxAspect: 1.28);
    }

    private static bool HasChatSendButton(ImageRegion region)
    {
        var scale = GetChatUiScale(region);
        using var roi = region.DeriveCrop((int)Math.Round(820 * scale), region.Height - (int)Math.Round(220 * scale), (int)Math.Round(500 * scale), (int)Math.Round(180 * scale));
        return HasBrightRoundedButton(roi.SrcMat, scale, minWidth: 90, maxWidth: 260, minHeight: 26, maxHeight: 92, minAspect: 1.45, maxAspect: 5.5);
    }

    private static bool HasBrightRoundedButton(Mat src, double scale, int minWidth, int maxWidth, int minHeight, int maxHeight, double minAspect, double maxAspect)
    {
        return CountBrightRoundedButtons(src, scale, minWidth, maxWidth, minHeight, maxHeight, minAspect, maxAspect) > 0;
    }

    private static int CountBrightRoundedButtons(Mat src, double scale, int minWidth, int maxWidth, int minHeight, int maxHeight, double minAspect, double maxAspect)
    {
        using var mask = OpenCvCommonHelper.Threshold(src, new Scalar(180, 165, 135), new Scalar(255, 255, 255));
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(ToKernelSize(7 * scale), ToKernelSize(7 * scale)));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var scaledMinWidth = Math.Max(8, (int)Math.Round(minWidth * scale));
        var scaledMaxWidth = Math.Max(scaledMinWidth + 1, (int)Math.Round(maxWidth * scale));
        var scaledMinHeight = Math.Max(8, (int)Math.Round(minHeight * scale));
        var scaledMaxHeight = Math.Max(scaledMinHeight + 1, (int)Math.Round(maxHeight * scale));
        var minArea = scaledMinWidth * scaledMinHeight * 0.35;
        var matches = 0;

        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            if (rect.Width < scaledMinWidth || rect.Height < scaledMinHeight || rect.Width > scaledMaxWidth || rect.Height > scaledMaxHeight)
            {
                continue;
            }

            var aspect = rect.Width / (double)Math.Max(rect.Height, 1);
            if (aspect < minAspect || aspect > maxAspect)
            {
                continue;
            }

            var contourArea = Cv2.ContourArea(contour);
            if (contourArea < minArea)
            {
                continue;
            }

            var fillRatio = contourArea / Math.Max(1d, rect.Width * rect.Height);
            if (fillRatio < 0.48)
            {
                continue;
            }

            matches++;
        }

        return matches;
    }

    private static bool HasEllipsisDots(Mat src, double scale, bool detectDarkDots)
    {
        using var gray = src.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var mask = new Mat();
        Cv2.Threshold(gray, mask, detectDarkDots ? 115 : 210, 255, detectDarkDots ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary);

        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(ToKernelSize(3 * scale), ToKernelSize(3 * scale)));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel);
        Cv2.FindContours(mask, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var minDot = Math.Max(3, (int)Math.Round(4 * scale));
        var maxDot = Math.Max(minDot + 1, (int)Math.Round(22 * scale));
        var maxYOffset = Math.Max(4, (int)Math.Round(10 * scale));
        var minGap = Math.Max(2, (int)Math.Round(3 * scale));
        var maxGap = Math.Max(minGap + 1, (int)Math.Round(36 * scale));

        var dots = new List<(Rect Rect, Point Center)>();
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            if (rect.Width < minDot || rect.Height < minDot || rect.Width > maxDot || rect.Height > maxDot)
            {
                continue;
            }

            var aspect = rect.Width / (double)Math.Max(rect.Height, 1);
            if (aspect < 0.55 || aspect > 1.8)
            {
                continue;
            }

            dots.Add((rect, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2)));
        }

        if (dots.Count < 3)
        {
            return false;
        }

        dots.Sort((a, b) => a.Center.X.CompareTo(b.Center.X));
        for (var i = 0; i <= dots.Count - 3; i++)
        {
            var first = dots[i];
            var second = dots[i + 1];
            var third = dots[i + 2];

            if (Math.Abs(first.Center.Y - second.Center.Y) > maxYOffset ||
                Math.Abs(second.Center.Y - third.Center.Y) > maxYOffset ||
                Math.Abs(first.Center.Y - third.Center.Y) > maxYOffset)
            {
                continue;
            }

            var gap1 = second.Center.X - first.Center.X;
            var gap2 = third.Center.X - second.Center.X;
            if (gap1 < minGap || gap2 < minGap || gap1 > maxGap || gap2 > maxGap)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static double GetChatUiScale(ImageRegion region)
    {
        return region.Width / 1920d;
    }

    private static int ToKernelSize(double size)
    {
        var rounded = Math.Max(1, (int)Math.Round(size));
        return rounded % 2 == 0 ? rounded + 1 : rounded;
    }
}
