using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

namespace BetterGenshinImpact.UnitTest.AutoPathing.Telemetry;

public class RouteNavigationCostModelTests
{
    [Fact]
    public void Defaults_AreCalibratableNavigationValues()
    {
        var options = new RouteNavigationCostOptions();

        Assert.Equal(4.5, options.WalkSpeed);
        Assert.Equal(6, options.RunSpeed);
        Assert.Equal(7.5, options.DashSpeed);
        Assert.Equal(18, options.TeleportDurationSeconds);
        Assert.Equal(80, options.LocalDirectMaxGameDistance);
        Assert.Equal(20, options.ReplanDriftGameDistance);
    }

    [Fact]
    public void EvaluateEdge_UsesMeasuredDurationForTelemetryRoute()
    {
        var model = new RouteNavigationCostModel(new IdentityCoordinateConverter());
        var edge = CreateEdge(MoveModeEnum.Walk.Code, 0, 0, 450, 0);
        edge.SourceKind = "telemetry";
        edge.AverageDurationMs = 30_000;

        var cost = model.EvaluateEdge("Teyvat", "TemplateMatch", edge, new RouteNavigationCostOptions());

        Assert.Equal(30, cost.Seconds, precision: 3);
        Assert.Equal(RouteNavigationCostSource.Telemetry, cost.Source);
        Assert.Equal("edge", cost.Component);
    }

    [Fact]
    public void EvaluateEdge_EstimatesSecondsFromGameDistanceAndConfiguredSpeed()
    {
        var model = new RouteNavigationCostModel(new IdentityCoordinateConverter());
        var edge = CreateEdge(MoveModeEnum.Walk.Code, 0, 0, 45, 0);
        edge.SourceKind = "historical-path";

        var cost = model.EvaluateEdge(
            "Teyvat",
            "TemplateMatch",
            edge,
            new RouteNavigationCostOptions { WalkSpeed = 4.5 });

        Assert.Equal(10, cost.Seconds, precision: 3);
        Assert.Equal(45, cost.GameDistance, precision: 3);
        Assert.Equal(RouteNavigationCostSource.Estimated, cost.Source);
    }

    [Fact]
    public void EvaluateTeleport_UsesConfiguredEstimatedDuration()
    {
        var model = new RouteNavigationCostModel(new IdentityCoordinateConverter());

        var cost = model.EvaluateTeleport(new RouteNavigationCostOptions { TeleportDurationSeconds = 21 });

        Assert.Equal(21, cost.Seconds, precision: 3);
        Assert.Equal(RouteNavigationCostSource.Estimated, cost.Source);
        Assert.Equal("teleport", cost.Component);
    }

    [Fact]
    public void EvaluateConnector_UsesGameCoordinatesInsteadOfImagePixels()
    {
        var model = new RouteNavigationCostModel(new ScaleCoordinateConverter(10));

        var cost = model.EvaluateConnector(
            "Teyvat",
            "TemplateMatch",
            new RouteGraphPoint(0, 0),
            new RouteGraphPoint(45, 0),
            MoveModeEnum.Run.Code,
            new RouteNavigationCostOptions { RunSpeed = 6 },
            "target-connector");

        Assert.Equal(4.5, cost.GameDistance, precision: 3);
        Assert.Equal(0.75, cost.Seconds, precision: 3);
        Assert.Equal(RouteNavigationCostSource.Estimated, cost.Source);
    }

    private static RouteNavigationEdge CreateEdge(string moveMode, double x1, double y1, double x2, double y2)
    {
        return new RouteNavigationEdge
        {
            EdgeId = "edge-1",
            MapName = "Teyvat",
            MoveMode = moveMode,
            Points =
            [
                new TelemetryPoint2D { X = (float)x1, Y = (float)y1 },
                new TelemetryPoint2D { X = (float)x2, Y = (float)y2 }
            ]
        };
    }

    private sealed class IdentityCoordinateConverter : IRouteCoordinateConverter
    {
        public bool TryImageToGame(string mapName, string? mapMatchMethod, RouteGraphPoint imagePoint, out RouteGamePoint gamePoint)
        {
            gamePoint = new RouteGamePoint(imagePoint.X, imagePoint.Y);
            return true;
        }

        public bool TryGameToImage(string mapName, string? mapMatchMethod, RouteGamePoint gamePoint, out RouteGraphPoint imagePoint)
        {
            imagePoint = new RouteGraphPoint(gamePoint.X, gamePoint.Y);
            return true;
        }
    }

    private sealed class ScaleCoordinateConverter(double imageUnitsPerGameUnit) : IRouteCoordinateConverter
    {
        public bool TryImageToGame(string mapName, string? mapMatchMethod, RouteGraphPoint imagePoint, out RouteGamePoint gamePoint)
        {
            gamePoint = new RouteGamePoint(imagePoint.X / imageUnitsPerGameUnit, imagePoint.Y / imageUnitsPerGameUnit);
            return true;
        }

        public bool TryGameToImage(string mapName, string? mapMatchMethod, RouteGamePoint gamePoint, out RouteGraphPoint imagePoint)
        {
            imagePoint = new RouteGraphPoint(gamePoint.X * imageUnitsPerGameUnit, gamePoint.Y * imageUnitsPerGameUnit);
            return true;
        }
    }
}
