using System;

namespace BetterGenshinImpact.View.Controls.Overlay;

public sealed class OverlayLayoutCommittedEventArgs : EventArgs
{
    public OverlayLayoutCommittedEventArgs(string layoutKey, double left, double top, double width, double height)
    {
        LayoutKey = layoutKey;
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    public string LayoutKey { get; }

    public double Left { get; }

    public double Top { get; }

    public double Width { get; }

    public double Height { get; }
}

