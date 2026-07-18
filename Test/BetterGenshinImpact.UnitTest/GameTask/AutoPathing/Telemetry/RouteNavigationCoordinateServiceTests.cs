using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

namespace BetterGenshinImpact.UnitTest.AutoPathing.Telemetry;

public class RouteNavigationCoordinateServiceTests
{
    [Theory]
    [InlineData("Teyvat", 32768, 16384, 0, 0)]
    [InlineData("TheChasm", 2048, 2048, 0, 0)]
    [InlineData("Enkanomiya", 2048, 2048, 0, 0)]
    public void KnownMapCoordinateConversion_DoesNotRequireRecognitionAssets(
        string mapName,
        double imageX,
        double imageY,
        double expectedGameX,
        double expectedGameY)
    {
        var converter = RouteNavigationCoordinateService.Instance;

        var succeeded = converter.TryImageToGame(
            mapName,
            "TemplateMatch",
            new RouteGraphPoint(imageX, imageY),
            out var gamePoint);

        Assert.True(succeeded);
        Assert.Equal(expectedGameX, gamePoint.X, precision: 3);
        Assert.Equal(expectedGameY, gamePoint.Y, precision: 3);
    }

    [Theory]
    [InlineData("Teyvat", 1234.5, -678.25)]
    [InlineData("TheChasm", -350, 480)]
    [InlineData("SeaOfBygoneEras", 980, -1200)]
    public void KnownMapCoordinateConversion_RoundTrips(string mapName, double gameX, double gameY)
    {
        var converter = RouteNavigationCoordinateService.Instance;

        Assert.True(converter.TryGameToImage(
            mapName,
            null,
            new RouteGamePoint(gameX, gameY),
            out var imagePoint));
        Assert.True(converter.TryImageToGame(mapName, null, imagePoint, out var roundTrip));

        Assert.Equal(gameX, roundTrip.X, precision: 3);
        Assert.Equal(gameY, roundTrip.Y, precision: 3);
    }
}
