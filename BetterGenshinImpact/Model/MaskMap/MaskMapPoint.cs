using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Model.MaskMap;

public class MaskMapPoint
{
    public string Id { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    
    public string LabelId { get; set; } = string.Empty;

    public bool Contains(double px, double py)
    {
        return px >= X - MaskMapPointStatic.Width / 2 && px <= X + MaskMapPointStatic.Width / 2 &&
               py >= Y - MaskMapPointStatic.Height / 2 && py <= Y + MaskMapPointStatic.Height / 2;
    }
}

public class MaskMapPointStatic
{
    public static readonly int Width = 32;

    public static readonly int Height = 32;
}