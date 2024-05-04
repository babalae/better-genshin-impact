using System;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.Model.Area.Converter;

public class ConvertRes<T>(int x, int y, int width, int height, T node) where T : Region
{
    public int X { get; set; } = x;
    public int Y { get; set; } = y;
    public int Width { get; set; } = width;
    public int Height { get; set; } = height;

    public T TargetRegion { get; set; } = node;

    public Rect ToRect()
    {
        return new Rect(X, Y, Width, Height);
    }

    public static ConvertRes<T> ConvertPositionToTargetRegion(int x, int y, int w, int h, Region startNode)
    {
        var node = startNode;
        while (node != null)
        {
            if (node is T)
            {
                break;
            }

            if (node.PrevConverter != null)
            {
                (x, y, w, h) = node.PrevConverter.ToPrev(x, y, w, h);
            }
            else
            {
                throw new Exception("PrevConverter is null");
            }

            node = node.Prev;
        }

        if (node is T targetRegion)
        {
            return new ConvertRes<T>(x, y, w, h, targetRegion);
        }
        else
        {
            throw new Exception("Target Region not found");
        }
    }
}
