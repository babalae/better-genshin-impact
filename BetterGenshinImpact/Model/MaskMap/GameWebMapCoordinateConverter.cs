namespace BetterGenshinImpact.Model.MaskMap;

public static class GameWebMapCoordinateConverter
{
    public const double GameOriginX = -749.75;
    public const double GameOriginY = 2322.0;

    public const double WebOriginX = 0.0;
    public const double WebOriginY = 0.0;

    public static (double x, double y) GameToMysWeb(double gameX, double gameY)
    {
        var x = -(gameX - GameOriginX);
        var y = -(gameY - GameOriginY);
        return (x, y);
    }

    public static (double x, double y) MysWebToGame(double webX, double webY)
    {
        var x = GameOriginX - webX;
        var y = GameOriginY - webY;
        return (x, y);
    }
}

