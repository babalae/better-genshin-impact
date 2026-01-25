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
        var x = KongyingTavernOffsetOriginX - webX / 2.0;
        var y = KongyingTavernOffsetOriginY - webY / 2.0;
        return (x, y);
    }
}