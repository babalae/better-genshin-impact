using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

namespace BetterGenshinImpact.UnitTest.AutoPathing.Telemetry;

public sealed class PathingTaskRouteImporterTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "BetterGI.PathingTaskRouteImporterTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Import_ConvertsAdjacentWaypointsThroughCoordinateConverter()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "source")).FullName;
        WriteRoute(sourceDirectory, "route.json", """
            {
              "info": { "map_name": "Teyvat", "map_match_method": "TemplateMatch" },
              "positions": [
                { "x": 10, "y": 20, "type": "path", "move_mode": "walk" },
                { "x": 15, "y": 25, "type": "path", "move_mode": "dash" },
                { "x": 30, "y": 40, "type": "target", "move_mode": "walk" }
              ]
            }
            """);
        var converter = new OffsetCoordinateConverter();
        var importer = new PathingTaskRouteImporter(converter);

        var result = importer.Import([sourceDirectory]);

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(new RouteGraphPoint(1010, 2020), result.Segments[0].Start);
        Assert.Equal(new RouteGraphPoint(1015, 2025), result.Segments[0].End);
        Assert.Equal(new RouteGraphPoint(1030, 2040), result.Segments[1].End);
        Assert.Equal(3, converter.GameToImageCallCount);
    }

    [Fact]
    public void Import_RecursivelyScansMultipleDirectoriesAndReportsNonRoutes()
    {
        var firstSource = Directory.CreateDirectory(Path.Combine(_tempRoot, "first")).FullName;
        var secondSource = Directory.CreateDirectory(Path.Combine(_tempRoot, "second")).FullName;
        WriteRoute(firstSource, Path.Combine("nested", "first.json"), TwoPointRouteJson(0, 0, 10, 0));
        WriteRoute(secondSource, "second.json", TwoPointRouteJson(20, 0, 30, 0));
        WriteRoute(secondSource, "process.json", "{ \"steps\": [] }");
        var importer = new PathingTaskRouteImporter(new OffsetCoordinateConverter());

        var result = importer.Import([firstSource, secondSource]);

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(2, result.Report.SourceDirectoryCount);
        Assert.Equal(3, result.Report.TotalJsonFiles);
        Assert.Equal(2, result.Report.EligibleRouteFiles);
        Assert.Equal(1, result.Report.NonRouteFiles);
    }

    [Fact]
    public void Import_ContinuesAfterMissingDirectoryAndInvalidJson()
    {
        var validSource = Directory.CreateDirectory(Path.Combine(_tempRoot, "valid")).FullName;
        WriteRoute(validSource, "valid.json", TwoPointRouteJson(0, 0, 10, 0));
        WriteRoute(validSource, "invalid.json", "{ this is not json }");
        var missingSource = Path.Combine(_tempRoot, "missing");
        var importer = new PathingTaskRouteImporter(new OffsetCoordinateConverter());

        var result = importer.Import([missingSource, validSource]);

        Assert.Single(result.Segments);
        Assert.Equal(2, result.Report.SourceDirectoryCount);
        Assert.Equal(1, result.Report.MissingSourceDirectories);
        Assert.Equal(1, result.Report.InvalidJsonFiles);
    }

    [Fact]
    public void Import_SplitsAtTeleportAndStartsANewTeleportAnchor()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "teleport")).FullName;
        WriteRoute(sourceDirectory, "route.json", """
            {
              "info": { "map_name": "Teyvat", "map_match_method": "TemplateMatch" },
              "positions": [
                { "x": 0, "y": 0, "type": "path", "move_mode": "walk" },
                { "x": 10, "y": 0, "type": "path", "move_mode": "walk" },
                { "x": 100, "y": 0, "type": "teleport", "move_mode": "walk" },
                { "x": 110, "y": 0, "type": "path", "move_mode": "walk" }
              ]
            }
            """);
        var importer = new PathingTaskRouteImporter(new OffsetCoordinateConverter());

        var result = importer.Import([sourceDirectory]);

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal("START_0_0", result.Segments[0].AnchorId);
        Assert.Equal("TP_100_0", result.Segments[1].AnchorId);
        Assert.Equal(new RouteGraphPoint(1100, 2000), result.Segments[1].Start);
    }

    [Fact]
    public void Import_StripsSideEffectActions()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "actions")).FullName;
        WriteRoute(sourceDirectory, "route.json", """
            {
              "info": { "map_name": "Teyvat", "map_match_method": "TemplateMatch" },
              "positions": [
                { "x": 0, "y": 0, "type": "path", "move_mode": "walk" },
                { "x": 10, "y": 0, "type": "path", "move_mode": "walk",
                  "action": "fight", "action_params": "do not execute" }
              ]
            }
            """);
        var importer = new PathingTaskRouteImporter(new OffsetCoordinateConverter());

        var result = importer.Import([sourceDirectory]);

        var segment = Assert.Single(result.Segments);
        Assert.Equal(string.Empty, segment.Action);
        Assert.Equal(string.Empty, segment.ActionParams);
        Assert.Equal(1, result.Report.StrippedActions["fight"]);
    }

    [Theory]
    [InlineData("stop_flying")]
    [InlineData("up_down_grab_leaf")]
    public void Import_PreservesNavigationActions(string action)
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, action)).FullName;
        WriteRoute(sourceDirectory, "route.json", $$"""
            {
              "info": { "map_name": "Teyvat", "map_match_method": "TemplateMatch" },
              "positions": [
                { "x": 0, "y": 0, "type": "path", "move_mode": "walk" },
                { "x": 10, "y": 0, "type": "path", "move_mode": "walk",
                  "action": "{{action}}", "action_params": "required" }
              ]
            }
            """);
        var importer = new PathingTaskRouteImporter(new OffsetCoordinateConverter());

        var result = importer.Import([sourceDirectory]);

        var segment = Assert.Single(result.Segments);
        Assert.Equal(action, segment.Action);
        Assert.Equal("required", segment.ActionParams);
        Assert.Empty(result.Report.StrippedActions);
    }

    [Theory]
    [InlineData("walk", "", true)]
    [InlineData("run", "", true)]
    [InlineData("dash", "", true)]
    [InlineData("swim", "", true)]
    [InlineData("fly", "", false)]
    [InlineData("jump", "", false)]
    [InlineData("climb", "", false)]
    [InlineData("walk", "up_down_grab_leaf", false)]
    public void Import_ClassifiesSegmentDirectionality(string moveMode, string action, bool expectedBidirectional)
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, $"direction-{moveMode}-{action}")).FullName;
        WriteRoute(sourceDirectory, "route.json", $$"""
            {
              "info": { "map_name": "Teyvat", "map_match_method": "TemplateMatch" },
              "positions": [
                { "x": 0, "y": 0, "type": "path", "move_mode": "walk" },
                { "x": 10, "y": 0, "type": "path", "move_mode": "{{moveMode}}", "action": "{{action}}" }
              ]
            }
            """);
        var importer = new PathingTaskRouteImporter(new OffsetCoordinateConverter());

        var segment = Assert.Single(importer.Import([sourceDirectory]).Segments);

        Assert.Equal(moveMode, segment.MoveMode);
        Assert.Equal(expectedBidirectional, segment.IsBidirectionalCandidate);
    }

    [Fact]
    public void Import_OnlyCutsEdgesAdjacentToAnUnconvertibleWaypoint()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "conversion-failure")).FullName;
        WriteRoute(sourceDirectory, "route.json", """
            {
              "info": { "map_name": "Teyvat", "map_match_method": "TemplateMatch" },
              "positions": [
                { "x": 0, "y": 0, "type": "path", "move_mode": "walk" },
                { "x": 10, "y": 0, "type": "path", "move_mode": "walk" },
                { "x": 20, "y": 0, "type": "path", "move_mode": "walk" },
                { "x": 30, "y": 0, "type": "path", "move_mode": "walk" },
                { "x": 40, "y": 0, "type": "target", "move_mode": "walk" }
              ]
            }
            """);
        var importer = new PathingTaskRouteImporter(new OffsetCoordinateConverter(failAtGameX: 20));

        var result = importer.Import([sourceDirectory]);

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(new RouteGraphPoint(1010, 2000), result.Segments[0].End);
        Assert.Equal(new RouteGraphPoint(1030, 2000), result.Segments[1].Start);
        Assert.Equal(1, result.Report.CoordinateConversionFailures);
    }

    [Fact]
    public void Import_DeduplicatesIdenticalRoutesBeforeCoordinateConversion()
    {
        var firstSource = Directory.CreateDirectory(Path.Combine(_tempRoot, "duplicates-a")).FullName;
        var secondSource = Directory.CreateDirectory(Path.Combine(_tempRoot, "duplicates-b")).FullName;
        var routeJson = TwoPointRouteJson(0, 0, 10, 0);
        WriteRoute(firstSource, "first.json", routeJson);
        WriteRoute(secondSource, "second.json", routeJson);
        var converter = new OffsetCoordinateConverter();
        var importer = new PathingTaskRouteImporter(converter);

        var result = importer.Import([firstSource, secondSource]);

        var segment = Assert.Single(result.Segments);
        Assert.Equal(2, converter.GameToImageCallCount);
        Assert.Equal(1, result.Report.DuplicateRouteFiles);
        Assert.Equal(2, segment.SourceCount);
    }

    [Fact]
    public void Import_ReportsUnknownMapsAndLimitedFailureExamples()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "report-examples")).FullName;
        for (var index = 0; index < 8; index++)
        {
            WriteRoute(sourceDirectory, $"invalid-{index}.json", "{ invalid json }");
        }
        WriteRoute(sourceDirectory, "unknown-map.json", """
            {
              "info": { "map_name": "UnknownWorld", "map_match_method": "TemplateMatch" },
              "positions": [
                { "x": 0, "y": 0, "type": "path", "move_mode": "walk" },
                { "x": 10, "y": 0, "type": "target", "move_mode": "walk" }
              ]
            }
            """);
        var missingDirectory = Path.Combine(_tempRoot, "missing-example");
        var importer = new PathingTaskRouteImporter(new OffsetCoordinateConverter());

        var result = importer.Import([missingDirectory, sourceDirectory]);

        Assert.Equal(8, result.Report.InvalidJsonFiles);
        Assert.Equal(1, result.Report.UnrecognizedMapFiles);
        Assert.InRange(result.Report.InvalidJsonExamples.Count, 1, 5);
        Assert.Contains(result.Report.UnrecognizedMapExamples, path => path.EndsWith("unknown-map.json"));
        Assert.Contains(missingDirectory, result.Report.MissingSourceDirectoryExamples);
        Assert.Empty(result.Segments);
    }

    [Fact]
    public void Import_AddsStableRepresentativeSourceMetadata()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "source-metadata")).FullName;
        WriteRoute(sourceDirectory, "named-route.json", TwoPointRouteJson(0, 0, 10, 0));
        var importer = new PathingTaskRouteImporter(new OffsetCoordinateConverter());

        var segment = Assert.Single(importer.Import([sourceDirectory]).Segments);

        Assert.Equal("pathing_task", segment.SourceKind);
        Assert.Equal("named-route.json", segment.SourceFileName);
        Assert.StartsWith("path_route_", segment.SourceId);
    }

    [Fact]
    public void Import_DeduplicatesFilesFromOverlappingSelectedDirectories()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "overlapping-root")).FullName;
        var nestedDirectory = Directory.CreateDirectory(Path.Combine(sourceDirectory, "nested")).FullName;
        WriteRoute(nestedDirectory, "route.json", TwoPointRouteJson(0, 0, 10, 0));
        var importer = new PathingTaskRouteImporter(new OffsetCoordinateConverter());

        var result = importer.Import([sourceDirectory, nestedDirectory]);

        Assert.Single(result.Segments);
        Assert.Equal(1, result.Report.TotalJsonFiles);
        Assert.Equal(1, result.Report.EligibleRouteFiles);
        Assert.Equal(0, result.Report.DuplicateRouteFiles);
        Assert.Equal(1, result.Segments[0].SourceCount);
    }

    [Fact]
    public void Import_ReportsStrippedActionsOnFirstAndTeleportWaypoints()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "all-action-reporting")).FullName;
        WriteRoute(sourceDirectory, "route.json", """
            {
              "info": { "map_name": "Teyvat", "map_match_method": "TemplateMatch" },
              "positions": [
                { "x": 0, "y": 0, "type": "path", "move_mode": "walk", "action": "fight" },
                { "x": 100, "y": 0, "type": "teleport", "move_mode": "walk", "action": "combat_script" },
                { "x": 110, "y": 0, "type": "target", "move_mode": "walk" }
              ]
            }
            """);
        var importer = new PathingTaskRouteImporter(new OffsetCoordinateConverter());

        var result = importer.Import([sourceDirectory]);

        Assert.Equal(1, result.Report.StrippedActions["fight"]);
        Assert.Equal(1, result.Report.StrippedActions["combat_script"]);
        Assert.Equal(string.Empty, Assert.Single(result.Segments).Action);
    }

    [Fact]
    public void Import_DoesNotDeduplicateDifferentSafeActionParameters()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_tempRoot, "safe-action-params")).FullName;
        WriteRoute(sourceDirectory, "first.json", TwoPointSafeActionRouteJson("first-params"));
        WriteRoute(sourceDirectory, "second.json", TwoPointSafeActionRouteJson("second-params"));
        var converter = new OffsetCoordinateConverter();
        var importer = new PathingTaskRouteImporter(converter);

        var result = importer.Import([sourceDirectory]);

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(0, result.Report.DuplicateRouteFiles);
        Assert.Equal(4, converter.GameToImageCallCount);
        Assert.Equal(
            ["first-params", "second-params"],
            result.Segments.Select(segment => segment.ActionParams).Order().ToArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static void WriteRoute(string directory, string relativePath, string json)
    {
        var filePath = Path.Combine(directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, json);
    }

    private static string TwoPointRouteJson(double startX, double startY, double endX, double endY)
    {
        return $$"""
            {
              "info": { "map_name": "Teyvat", "map_match_method": "TemplateMatch" },
              "positions": [
                { "x": {{startX}}, "y": {{startY}}, "type": "path", "move_mode": "walk" },
                { "x": {{endX}}, "y": {{endY}}, "type": "target", "move_mode": "walk" }
              ]
            }
            """;
    }

    private static string TwoPointSafeActionRouteJson(string actionParams)
    {
        return $$"""
            {
              "info": { "map_name": "Teyvat", "map_match_method": "TemplateMatch" },
              "positions": [
                { "x": 0, "y": 0, "type": "path", "move_mode": "walk" },
                { "x": 10, "y": 0, "type": "target", "move_mode": "walk",
                  "action": "up_down_grab_leaf", "action_params": "{{actionParams}}" }
              ]
            }
            """;
    }

    private sealed class OffsetCoordinateConverter(double? failAtGameX = null) : IRouteCoordinateConverter
    {
        public int GameToImageCallCount { get; private set; }

        public bool TryImageToGame(
            string mapName,
            string? mapMatchMethod,
            RouteGraphPoint imagePoint,
            out RouteGamePoint gamePoint)
        {
            gamePoint = new RouteGamePoint(imagePoint.X - 1000, imagePoint.Y - 2000);
            return true;
        }

        public bool TryGameToImage(
            string mapName,
            string? mapMatchMethod,
            RouteGamePoint gamePoint,
            out RouteGraphPoint imagePoint)
        {
            GameToImageCallCount++;
            if (failAtGameX.HasValue && Math.Abs(gamePoint.X - failAtGameX.Value) < 0.001)
            {
                imagePoint = default;
                return false;
            }

            imagePoint = new RouteGraphPoint(gamePoint.X + 1000, gamePoint.Y + 2000);
            return true;
        }
    }
}
