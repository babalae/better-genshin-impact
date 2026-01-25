namespace BetterGenshinImpact.Model.MaskMap;

public static class GameWebMapCoordinateConverter
{
    public const double MysWebOffsetOriginX = -749.75;
    public const double MysWebOffsetOriginY = 2322.0;
    
    
    public const double KongyingTavernOffsetOriginX = 396.125;
    public const double KongyingTavernOffsetOriginY = -619.9375;
    
    
    public static (double x, double y) GameToMysWeb(double gameX, double gameY)
    {
        var x = -(gameX - MysWebOffsetOriginX);
        var y = -(gameY - MysWebOffsetOriginY);
        return (x, y);
    }

    public static (double x, double y) MysWebToGame(double webX, double webY)
    {
        var x = MysWebOffsetOriginX - webX;
        var y = MysWebOffsetOriginY - webY;
        return (x, y);
    }

    public static (double x, double y) KongyingTavernToGame(double webX, double webY)
    {
        var x = KongyingTavernOffsetOriginX / 3.0 * 4 - webX;
        var y = KongyingTavernOffsetOriginY / 3.0 * 4 - webY;
        return (x, y);
    }
}

