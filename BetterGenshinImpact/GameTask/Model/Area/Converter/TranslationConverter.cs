namespace BetterGenshinImpact.GameTask.Model.Area.Converter;


/// <summary>
/// 平移变换
/// </summary>
public class TranslationConverter(int offsetX, int offsetY) : INodeConverter
{

    public (int x, int y, int w, int h) ToPrev(int x, int y, int w, int h)
    {
        return (x + offsetX, y + offsetY, w, h);
    }
}
