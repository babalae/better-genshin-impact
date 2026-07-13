using OpenCvSharp;
using System;

namespace BetterGenshinImpact.Core.Config;

[Obsolete("Replace with original OpenCvSharp.Rect")]
public class RectConfig
{
    public RectConfig()
    {
    }

    public RectConfig(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public Rect ToRect()
    {
        return new Rect(X, Y, Width, Height);
    }
}
