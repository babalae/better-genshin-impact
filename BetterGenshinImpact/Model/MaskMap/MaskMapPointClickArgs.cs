using System.Windows;

namespace BetterGenshinImpact.Model.MaskMap;

public sealed class MaskMapPointClickArgs
{
    public MaskMapPointClickArgs(MaskMapPoint point, Point anchorPosition)
    {
        Point = point;
        AnchorPosition = anchorPosition;
    }

    public MaskMapPoint Point { get; }

    public Point AnchorPosition { get; }
}
