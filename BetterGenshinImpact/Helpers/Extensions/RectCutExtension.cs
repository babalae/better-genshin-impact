using OpenCvSharp;

namespace BetterGenshinImpact.Helpers.Extensions;

/// <summary>
/// 按比列切割出对应比例区域的矩形
/// </summary>
public static class RectCutExtension
{
    public static Rect CutLeft(this Rect rect, double ratio)
    {
        return new Rect(0, 0, (int)(rect.Width * ratio), rect.Height);
    }
    
    public static Rect CutRight(this Rect rect, double ratio)
    {
        return new Rect(0 + (int)(rect.Width * (1 - ratio)), 0, (int)(rect.Width * ratio), rect.Height);
    }
    
    public static Rect CutTop(this Rect rect, double ratio)
    {
        return new Rect(0, 0, rect.Width, (int)(rect.Height * ratio));
    }
    
    public static Rect CutBottom(this Rect rect, double ratio)
    {
        return new Rect(0, 0 + (int)(rect.Height * (1 - ratio)), rect.Width, (int)(rect.Height * ratio));
    }
    
    public static Rect CutLeftTop(this Rect rect, double ratioLeft, double ratioTop)
    {
        return new Rect(0, 0, (int)(rect.Width * ratioLeft), (int)(rect.Height * ratioTop));
    }
    
    public static Rect CutRightTop(this Rect rect, double ratioRight, double ratioTop)
    {
        return new Rect(0 + (int)(rect.Width * (1 - ratioRight)), 0, (int)(rect.Width * ratioRight), (int)(rect.Height * ratioTop));
    }
    
    public static Rect CutLeftBottom(this Rect rect, double ratioLeft, double ratioBottom)
    {
        return new Rect(0, 0 + (int)(rect.Height * (1 - ratioBottom)), (int)(rect.Width * ratioLeft), (int)(rect.Height * ratioBottom));
    }
    
    public static Rect CutRightBottom(this Rect rect, double ratioRight, double ratioBottom)
    {
        return new Rect(0 + (int)(rect.Width * (1 - ratioRight)), 0 + (int)(rect.Height * (1 - ratioBottom)), (int)(rect.Width * ratioRight), (int)(rect.Height * ratioBottom));
    }
    
    
}
