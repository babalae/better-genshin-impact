using OpenCvSharp;
using Vanara.PInvoke;

namespace BetterGenshinImpact.GameTask.Model;

public sealed class CaptureGeometry
{
    private const int ContentAspectWidth = 16;
    private const int ContentAspectHeight = 9;

    private CaptureGeometry(RECT rawCaptureRect, Rect captureSpace, RECT contentRect, Rect contentSpace)
    {
        RawCaptureRect = rawCaptureRect;
        CaptureSpace = captureSpace;
        ContentRect = contentRect;
        ContentSpace = contentSpace;
    }

    public RECT RawCaptureRect { get; }

    public Rect CaptureSpace { get; }

    public RECT ContentRect { get; }

    public Rect ContentSpace { get; }

    public bool HasValidContentSpace =>
        CaptureSpace.Width > 0
        && CaptureSpace.Height > 0
        && ContentSpace.Width > 0
        && ContentSpace.Height > 0;

    public static CaptureGeometry FromRawCaptureRect(RECT rawCaptureRect)
    {
        var captureSpace = new Rect(0, 0, rawCaptureRect.Width, rawCaptureRect.Height);
        var contentSpace = CalculateContentSpace(captureSpace);
        var contentRect = new RECT(
            rawCaptureRect.Left + contentSpace.X,
            rawCaptureRect.Top + contentSpace.Y,
            rawCaptureRect.Left + contentSpace.X + contentSpace.Width,
            rawCaptureRect.Top + contentSpace.Y + contentSpace.Height);

        return new CaptureGeometry(rawCaptureRect, captureSpace, contentRect, contentSpace);
    }

    public static Rect CalculateContentSpace(Rect captureSpace)
    {
        if (captureSpace.Width <= 0 || captureSpace.Height <= 0)
        {
            return new Rect(captureSpace.X, captureSpace.Y, 0, 0);
        }

        var width = captureSpace.Width;
        var height = captureSpace.Height;
        var widthLimitedByHeight = height * ContentAspectWidth / ContentAspectHeight;

        if (widthLimitedByHeight <= width)
        {
            var x = captureSpace.X + (width - widthLimitedByHeight) / 2;
            return new Rect(x, captureSpace.Y, widthLimitedByHeight, height);
        }

        var heightLimitedByWidth = width * ContentAspectHeight / ContentAspectWidth;
        var y = captureSpace.Y + (height - heightLimitedByWidth) / 2;
        return new Rect(captureSpace.X, y, width, heightLimitedByWidth);
    }
}
