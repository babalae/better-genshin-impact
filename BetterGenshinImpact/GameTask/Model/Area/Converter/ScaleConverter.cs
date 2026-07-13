namespace BetterGenshinImpact.GameTask.Model.Area.Converter;

/// <summary>
/// 比例变换
/// </summary>
/// <param name="scale">需要放大多少倍</param>
public class ScaleConverter(double scale) : INodeConverter
{
    public (int x, int y, int w, int h) ToPrev(int x, int y, int w, int h)
    {
        return ((int)(x * scale), (int)(y * scale), (int)(w * scale), (int)(h * scale));
        // return (x, y, (int)(w * scale), (int)(h * scale));
    }
}
