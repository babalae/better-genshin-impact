using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;

namespace BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

public sealed class PathingTaskRouteImporter(IRouteCoordinateConverter coordinateConverter)
{
    private const int FailureExampleLimit = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public PathingTaskRouteImportResult Import(
        IEnumerable<string> sourceDirectories,
        CancellationToken cancellationToken = default,
        double maximumStraightSegmentGameDistance = 300)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectories);

        var segments = new List<RouteNavigationSourceSegment>();
        var requestedDirectories = sourceDirectories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var report = new PathingTaskImportReport
        {
            SourceDirectoryCount = requestedDirectories.Count,
            MissingSourceDirectories = requestedDirectories.Count(path => !Directory.Exists(path))
        };
        foreach (var missingDirectory in requestedDirectories.Where(path => !Directory.Exists(path)))
        {
            AddExample(report.MissingSourceDirectoryExamples, missingDirectory);
        }
        var directories = requestedDirectories.Where(Directory.Exists).ToList();
        var files = new List<string>();
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };
        foreach (var directory in directories)
        {
            try
            {
                files.AddRange(Directory.EnumerateFiles(directory, "*.json", enumerationOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                report.UnreadableSourceDirectories++;
                AddExample(report.UnreadableSourceDirectoryExamples, directory);
            }
        }
        files = files
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        report.TotalJsonFiles = files.Count;
        var uniqueRoutes = new Dictionary<string, ImportedRouteCandidate>(StringComparer.Ordinal);

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImportRouteFile? route;
            try
            {
                route = JsonSerializer.Deserialize<ImportRouteFile>(File.ReadAllText(filePath), JsonOptions);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                report.InvalidJsonFiles++;
                AddExample(report.InvalidJsonExamples, filePath);
                continue;
            }
            if (route?.Positions is not { Count: >= 2 })
            {
                report.NonRouteFiles++;
                AddExample(report.NonRouteExamples, filePath);
                continue;
            }

            var mapName = RouteGraphGeometry.NormalizeMapName(route.Info?.MapName);
            if (!Enum.TryParse<MapTypes>(mapName, ignoreCase: true, out _))
            {
                report.UnrecognizedMapFiles++;
                AddExample(report.UnrecognizedMapExamples, filePath);
                continue;
            }

            report.EligibleRouteFiles++;
            var signature = CreateRouteSignature(mapName, route.Positions);
            if (uniqueRoutes.TryGetValue(signature, out var existingRoute))
            {
                existingRoute.SourceCount++;
                report.DuplicateRouteFiles++;
                continue;
            }

            uniqueRoutes[signature] = new ImportedRouteCandidate(
                filePath,
                mapName,
                route,
                "path_route_" + signature[..16].ToLowerInvariant(),
                ResolveRepositoryName(filePath),
                ResolveRelativeSourceName(filePath, directories));
        }

        foreach (var candidate in uniqueRoutes.Values.OrderBy(route => route.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var route = candidate.Route;
            var mapName = candidate.MapName;
            var imagePoints = new RouteGraphPoint?[route.Positions.Count];
            var safeActions = new (string Action, string ActionParams)[route.Positions.Count];
            for (var waypointIndex = 0; waypointIndex < route.Positions.Count; waypointIndex++)
            {
                var waypoint = route.Positions[waypointIndex];
                safeActions[waypointIndex] = ResolveSafeAction(waypoint, report);
                if (!coordinateConverter.TryGameToImage(
                        mapName,
                        route.Info?.MapMatchMethod,
                        new RouteGamePoint(waypoint.X, waypoint.Y),
                        out var imagePoint))
                {
                    report.CoordinateConversionFailures++;
                    AddExample(
                        report.CoordinateConversionFailureExamples,
                        $"{candidate.FilePath}#{waypointIndex + 1}");
                    continue;
                }

                imagePoints[waypointIndex] = imagePoint;
            }

            var anchorId = CreateAnchorId(route.Positions[0]);
            for (var index = 1; index < imagePoints.Length; index++)
            {
                if (string.Equals(route.Positions[index].Type, "teleport", StringComparison.OrdinalIgnoreCase))
                {
                    anchorId = CreateAnchorId(route.Positions[index]);
                    continue;
                }

                if (!imagePoints[index - 1].HasValue || !imagePoints[index].HasValue)
                {
                    continue;
                }

                if (maximumStraightSegmentGameDistance > 0 &&
                    Distance(route.Positions[index - 1], route.Positions[index]) > maximumStraightSegmentGameDistance)
                {
                    report.SkippedExcessiveSegments++;
                    continue;
                }

                var (action, actionParams) = safeActions[index];
                var moveMode = string.IsNullOrWhiteSpace(route.Positions[index].MoveMode)
                    ? MoveModeEnum.Walk.Code
                    : route.Positions[index].MoveMode;
                segments.Add(new RouteNavigationSourceSegment(
                    mapName,
                    imagePoints[index - 1]!.Value,
                    imagePoints[index]!.Value,
                    anchorId,
                    moveMode,
                    action,
                    actionParams,
                    IsBidirectional(moveMode, action),
                    candidate.SourceCount,
                    "pathing_task",
                    candidate.RelativeSourceName,
                    candidate.SourceId,
                    candidate.Repository,
                    string.IsNullOrWhiteSpace(route.Info?.Name) ? Path.GetFileNameWithoutExtension(candidate.FilePath) : route.Info.Name,
                    route.Info?.Author ?? string.Empty));
            }
        }

        return new PathingTaskRouteImportResult(segments, report);
    }

    private static double Distance(ImportWaypoint from, ImportWaypoint to)
    {
        var dx = from.X - to.X;
        var dy = from.Y - to.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static string CreateRouteSignature(string mapName, IReadOnlyList<ImportWaypoint> positions)
    {
        var signature = new StringBuilder(mapName);
        foreach (var waypoint in positions)
        {
            var moveMode = string.IsNullOrWhiteSpace(waypoint.MoveMode)
                ? MoveModeEnum.Walk.Code
                : waypoint.MoveMode;
            var safeAction = IsSafeAction(waypoint.Action) ? waypoint.Action : string.Empty;
            var safeActionParams = string.IsNullOrEmpty(safeAction)
                ? string.Empty
                : waypoint.ActionParams ?? string.Empty;
            // Historical target markers describe the old task, not reusable graph semantics.
            // Only teleport remains a special graph point; every other waypoint is normalized to path.
            var normalizedType = string.Equals(waypoint.Type, "teleport", StringComparison.OrdinalIgnoreCase)
                ? "teleport"
                : "path";
            signature
                .Append('|').Append(waypoint.X.ToString("R", CultureInfo.InvariantCulture))
                .Append(',').Append(waypoint.Y.ToString("R", CultureInfo.InvariantCulture))
                .Append(',').Append(normalizedType)
                .Append(',').Append(moveMode.ToLowerInvariant())
                .Append(',').Append(safeAction.ToLowerInvariant())
                .Append(',').Append(safeActionParams.Length).Append(':').Append(safeActionParams);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature.ToString())));
    }

    private static void AddExample(ICollection<string> examples, string value)
    {
        if (examples.Count < FailureExampleLimit)
        {
            examples.Add(value);
        }
    }

    private static string ResolveRepositoryName(string filePath)
    {
        var directory = new FileInfo(filePath).Directory;
        while (directory != null)
        {
            if (string.Equals(directory.Name, "repo", StringComparison.OrdinalIgnoreCase) && directory.Parent != null)
            {
                return directory.Parent.Name;
            }
            if (string.Equals(directory.Name, "BetterGI", StringComparison.OrdinalIgnoreCase))
            {
                return directory.Name;
            }
            directory = directory.Parent;
        }
        return string.Empty;
    }

    private static string ResolveRelativeSourceName(string filePath, IReadOnlyList<string> sourceDirectories)
    {
        var root = sourceDirectories
            .Where(directory => filePath.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(directory => directory.Length)
            .FirstOrDefault();
        return root == null ? Path.GetFileName(filePath) : Path.GetRelativePath(root, filePath);
    }

    private static string CreateAnchorId(ImportWaypoint waypoint)
    {
        var prefix = string.Equals(waypoint.Type, "teleport", StringComparison.OrdinalIgnoreCase)
            ? "TP"
            : "START";
        return FormattableString.Invariant($"{prefix}_{Math.Round(waypoint.X)}_{Math.Round(waypoint.Y)}");
    }

    private static (string Action, string ActionParams) ResolveSafeAction(
        ImportWaypoint waypoint,
        PathingTaskImportReport report)
    {
        if (string.IsNullOrWhiteSpace(waypoint.Action))
        {
            return (string.Empty, string.Empty);
        }

        if (IsSafeAction(waypoint.Action))
        {
            return (waypoint.Action, waypoint.ActionParams ?? string.Empty);
        }

        report.StrippedActions.TryGetValue(waypoint.Action, out var count);
        report.StrippedActions[waypoint.Action] = count + 1;
        return (string.Empty, string.Empty);
    }

    private static bool IsSafeAction(string? action)
    {
        return string.Equals(action, ActionEnum.StopFlying.Code, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(action, ActionEnum.UpDownGrabLeaf.Code, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBidirectional(string moveMode, string action)
    {
        if (string.Equals(action, ActionEnum.UpDownGrabLeaf.Code, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !moveMode.Contains("fly", StringComparison.OrdinalIgnoreCase) &&
               !moveMode.Contains("jump", StringComparison.OrdinalIgnoreCase) &&
               !moveMode.Contains("climb", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ImportRouteFile
    {
        public ImportRouteInfo? Info { get; init; }

        public List<ImportWaypoint>? Positions { get; init; }
    }

    private sealed class ImportedRouteCandidate(
        string filePath,
        string mapName,
        ImportRouteFile route,
        string? sourceId = null,
        string? repository = null,
        string? relativeSourceName = null)
    {
        public string FilePath { get; } = filePath;

        public string MapName { get; } = mapName;

        public ImportRouteFile Route { get; } = route;

        public string SourceId { get; } = sourceId ?? string.Empty;

        public int SourceCount { get; set; } = 1;

        public string Repository { get; } = repository ?? string.Empty;

        public string RelativeSourceName { get; } = relativeSourceName ?? Path.GetFileName(filePath);
    }

    private sealed class ImportRouteInfo
    {
        public string MapName { get; init; } = string.Empty;

        public string MapMatchMethod { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public string Author { get; init; } = string.Empty;
    }

    private sealed class ImportWaypoint
    {
        public double X { get; init; }

        public double Y { get; init; }

        public string Type { get; init; } = string.Empty;

        public string MoveMode { get; init; } = string.Empty;

        public string Action { get; init; } = string.Empty;

        public string? ActionParams { get; init; }
    }
}

public sealed record RouteNavigationSourceSegment(
    string MapName,
    RouteGraphPoint Start,
    RouteGraphPoint End,
    string AnchorId,
    string MoveMode,
    string Action,
    string ActionParams,
    bool IsBidirectionalCandidate,
    int SourceCount,
    string SourceKind,
    string SourceFileName,
    string SourceId,
    string SourceRepository,
    string SourceRouteName,
    string SourceAuthor);

public sealed record PathingTaskRouteImportResult(
    IReadOnlyList<RouteNavigationSourceSegment> Segments,
    PathingTaskImportReport Report);

public sealed class PathingTaskImportReport
{
    public int SourceDirectoryCount { get; internal set; }

    public int TotalJsonFiles { get; internal set; }

    public int EligibleRouteFiles { get; internal set; }

    public int NonRouteFiles { get; internal set; }

    public int MissingSourceDirectories { get; internal set; }

    public int InvalidJsonFiles { get; internal set; }

    public int CoordinateConversionFailures { get; internal set; }

    public int SkippedExcessiveSegments { get; internal set; }

    public int DuplicateRouteFiles { get; internal set; }

    public int UnrecognizedMapFiles { get; internal set; }

    public int UnreadableSourceDirectories { get; internal set; }

    public List<string> MissingSourceDirectoryExamples { get; } = [];

    public List<string> UnreadableSourceDirectoryExamples { get; } = [];

    public List<string> InvalidJsonExamples { get; } = [];

    public List<string> NonRouteExamples { get; } = [];

    public List<string> UnrecognizedMapExamples { get; } = [];

    public List<string> CoordinateConversionFailureExamples { get; } = [];

    public Dictionary<string, int> StrippedActions { get; } = new(StringComparer.OrdinalIgnoreCase);
}
