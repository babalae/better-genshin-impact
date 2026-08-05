using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition;
using BetterGenshinImpact.Core.Script;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Model;
using BetterGenshinImpact.GameTask.Model.Area;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPathing.TargetNavigation;

/// <summary>
/// 在 TaskRunner 内运行可等待的多图标跟随，避免局部导航期间被其他独立任务抢占输入。
/// </summary>
public sealed class BetterGiLocalTargetNavigator : ILocalTargetNavigator
{
    private readonly MultiIconLocalNavigator _navigator;

    public BetterGiLocalTargetNavigator(
        IRouteCurrentPositionResolver? positionResolver = null,
        IRouteCoordinateConverter? coordinateConverter = null,
        IReadOnlyList<string>? templateRoots = null)
    {
        var resolver = positionResolver ?? RouteCurrentPositionResolver.Instance;
        var converter = coordinateConverter ?? RouteNavigationCoordinateService.Instance;
        _navigator = new MultiIconLocalNavigator(
            new BetterGiLocalNavigationPerception(resolver, converter, templateRoots),
            new BetterGiLocalNavigationMotion(resolver, converter));
    }

    public async Task<LocalTargetNavigationResult> NavigateAsync(
        LocalTargetNavigationRequest request,
        CancellationToken cancellationToken = default)
    {
        LocalTargetNavigationResult? result = null;
        var started = false;
        await new TaskRunner().RunThreadAsync(async () =>
        {
            started = true;
            using var registration = cancellationToken.Register(
                () => CancellationContext.Instance.ManualCancel());
            result = await _navigator.NavigateAsync(request, cancellationToken);
        });

        return !started
            ? LocalTargetNavigationResult.Failed(
                LocalNavigationFailureCode.Unexpected,
                "TaskRunner 正被其他独立任务占用")
            : result ?? LocalTargetNavigationResult.Failed(LocalNavigationFailureCode.Unexpected);
    }
}

internal sealed class BetterGiLocalNavigationPerception(
    IRouteCurrentPositionResolver positionResolver,
    IRouteCoordinateConverter coordinateConverter,
    IReadOnlyList<string>? templateRoots = null) : ILocalNavigationPerception
{
    private readonly IReadOnlyList<string> _templateRoots =
        templateRoots ?? LocalNavigationTemplateCatalog.FindDefaultRoots();

    public Task<LocalNavigationObservation> ObserveAsync(
        LocalTargetNavigationRequest request,
        IReadOnlyList<LocalNavigationIconGroup> templateGroups,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var screen = TaskControl.CaptureToRectArea(true);
        var matches = new List<LocalNavigationIconMatch>();

        foreach (var group in templateGroups)
        {
            if (group == LocalNavigationIconGroup.Task)
            {
                using var taskMarker = screen.Find(ElementAssets.Instance.BlueTrackPoint);
                if (taskMarker.IsExist())
                {
                    matches.Add(ToMatch(group, taskMarker, 1));
                }
            }

            foreach (var templatePath in LocalNavigationTemplateCatalog.FindTemplates(_templateRoots, group))
            {
                var match = TryMatchTemplate(screen, templatePath, group, request.Options.LocalTemplateThreshold);
                if (match != null)
                {
                    matches.Add(match);
                }
            }
        }

        var remainingDistance = TryResolveRemainingDistance(screen, request, out var distance)
            ? distance
            : null;
        return Task.FromResult(new LocalNavigationObservation
        {
            Matches = matches,
            RemainingGameDistance = remainingDistance,
            Reached = remainingDistance <= request.Options.LocalArrivalGameDistance,
            InTalk = LocalNavigationTemplateCatalog.FindTalkTemplates(_templateRoots)
                .Any(path => TryMatchTemplate(
                    screen,
                    path,
                    LocalNavigationIconGroup.Task,
                    request.Options.LocalTemplateThreshold,
                    talkTemplate: true) != null)
        });
    }

    private bool TryResolveRemainingDistance(
        ImageRegion screen,
        LocalTargetNavigationRequest request,
        out double? distance)
    {
        distance = null;
        if (!positionResolver.TryResolve(
                screen,
                request.MapName,
                request.MapMatchMethod,
                out var current) ||
            !coordinateConverter.TryImageToGame(
                request.MapName,
                request.MapMatchMethod,
                current.ImagePoint,
                out var currentGame) ||
            !coordinateConverter.TryImageToGame(
                request.MapName,
                request.MapMatchMethod,
                request.TargetImagePoint,
                out var targetGame))
        {
            return false;
        }

        distance = Distance(currentGame, targetGame);
        return true;
    }

    private static LocalNavigationIconMatch? TryMatchTemplate(
        ImageRegion screen,
        string path,
        LocalNavigationIconGroup group,
        double threshold,
        bool talkTemplate = false)
    {
        try
        {
            using var template = Cv2.ImRead(path, ImreadModes.Color);
            if (template.Empty())
            {
                return null;
            }

            var scale = screen.Width / 1920d;
            var recognition = RecognitionObject.TemplateMatch(
                template,
                (talkTemplate ? 0 : 300) * scale,
                (talkTemplate ? 0 : 100) * scale,
                (talkTemplate ? 500 : 1300) * scale,
                (talkTemplate ? 200 : 800) * scale);
            recognition.Threshold = threshold;
            try
            {
                using var region = screen.Find(recognition);
                return region.IsExist() ? ToMatch(group, region, threshold) : null;
            }
            finally
            {
                recognition.TemplateImageGreyMat?.Dispose();
                recognition.MaskMat?.Dispose();
            }
        }
        catch
        {
            return null;
        }
    }

    private static LocalNavigationIconMatch ToMatch(
        LocalNavigationIconGroup group,
        Region region,
        double confidence)
    {
        return new LocalNavigationIconMatch(
            group,
            region.X + region.Width / 2d,
            region.Y + region.Height / 2d,
            confidence);
    }

    private static double Distance(RouteGamePoint from, RouteGamePoint to)
    {
        var dx = from.X - to.X;
        var dy = from.Y - to.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

internal sealed class BetterGiLocalNavigationMotion(
    IRouteCurrentPositionResolver positionResolver,
    IRouteCoordinateConverter coordinateConverter) : ILocalNavigationMotion
{
    public async Task AdvanceTowardIconAsync(
        LocalTargetNavigationRequest request,
        LocalNavigationIconMatch icon,
        CancellationToken cancellationToken)
    {
        var options = request.Options;
        var scale = TaskContext.Instance().SystemInfo.ScaleMax1080PCaptureRect.Width / 1920d;
        var centerX = options.LocalIconCenterX * scale;
        var tolerance = options.LocalIconCenterTolerance * scale;

        if (icon.Y > options.LocalIconMaximumY * scale)
        {
            Simulation.SendInput.Mouse.MoveMouseBy(0, (int)Math.Round(options.LocalVerticalMouseAdjustment * scale));
            await Task.Delay(Math.Max(1, options.LocalSettleMilliseconds), cancellationToken);
            return;
        }

        var deltaX = icon.X - centerX;
        if (Math.Abs(deltaX) > tolerance)
        {
            var steps = Math.Max(1, (int)Math.Floor(Math.Abs(deltaX) / Math.Max(1, tolerance)));
            var adjustment = Math.Sign(deltaX) * options.LocalMouseAdjustmentUnit * steps;
            Simulation.SendInput.Mouse.MoveMouseBy((int)Math.Round(adjustment * scale), 0);
            await Task.Delay(Math.Max(1, options.LocalSettleMilliseconds), cancellationToken);
            return;
        }

        Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
        try
        {
            await Task.Delay(Math.Max(1, options.LocalForwardStepMilliseconds), cancellationToken);
            Simulation.SendInput.SimulateAction(GIActions.Jump);
            await Task.Delay(Math.Max(1, options.LocalJumpIntervalMilliseconds), cancellationToken);
            Simulation.SendInput.SimulateAction(GIActions.Jump);
            await Task.Delay(Math.Max(1, options.LocalJumpIntervalMilliseconds), cancellationToken);
        }
        finally
        {
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
        }

        await Task.Delay(Math.Max(1, options.LocalSettleMilliseconds), cancellationToken);
    }

    public async Task<bool> NavigateToCoordinateAsync(
        LocalTargetNavigationRequest request,
        CancellationToken cancellationToken)
    {
        using var screen = TaskControl.CaptureToRectArea(true);
        if (!positionResolver.TryResolve(
                screen,
                request.MapName,
                request.MapMatchMethod,
                out var current) ||
            !coordinateConverter.TryImageToGame(
                request.MapName,
                request.MapMatchMethod,
                current.ImagePoint,
                out var currentGame) ||
            !coordinateConverter.TryImageToGame(
                request.MapName,
                request.MapMatchMethod,
                request.TargetImagePoint,
                out var targetGame))
        {
            return false;
        }

        var distance = Math.Sqrt(
            Math.Pow(currentGame.X - targetGame.X, 2) +
            Math.Pow(currentGame.Y - targetGame.Y, 2));
        if (distance > request.Options.LocalDirectMaxGameDistance)
        {
            return false;
        }

        var task = new PathingTask
        {
            Info = new PathingTaskInfo
            {
                Name = "目标导航局部坐标直达",
                MapName = request.MapName,
                MapMatchMethod = request.MapMatchMethod ?? string.Empty,
                Type = PathingTaskType.Collect.Code
            },
            Positions =
            [
                new Waypoint
                {
                    X = currentGame.X,
                    Y = currentGame.Y,
                    Type = WaypointType.Path.Code,
                    MoveMode = MoveModeEnum.Run.Code
                },
                new Waypoint
                {
                    X = targetGame.X,
                    Y = targetGame.Y,
                    Type = WaypointType.Target.Code,
                    MoveMode = MoveModeEnum.Run.Code
                }
            ]
        };
        var executor = new PathExecutor(CancellationContext.Instance.Cts.Token)
        {
            PartyConfig = new PathingPartyConfig { AutoFightEnabled = false }
        };
        await executor.Pathing(task);
        return executor.SuccessEnd;
    }

    public async Task RequestTrackedQuestMarkerAsync(
        LocalTargetNavigationRequest request,
        CancellationToken cancellationToken)
    {
        Simulation.SendInput.SimulateAction(GIActions.QuestNavigation);
        await Task.Delay(
            Math.Max(1, request.Options.LocalRecognitionRetryDelayMilliseconds),
            cancellationToken);
    }

    public void ReleaseAllInputs()
    {
        Simulation.ReleaseAllKey();
    }
}

internal static class LocalNavigationTemplateCatalog
{
    private static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".bmp"];

    public static IReadOnlyList<string> FindDefaultRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Global.Absolute(Path.Combine("User", "AutoPathing", "LocalNavigationIcons"))
        };

        for (var directory = new DirectoryInfo(Global.StartUpPath);
             directory != null;
             directory = directory.Parent)
        {
            roots.Add(Path.Combine(
                directory.FullName,
                "BadGI-JsScript",
                "自动剧情加载器",
                "Data",
                "RecognitionObject"));
        }

        return roots.Where(Directory.Exists).ToList();
    }

    public static IEnumerable<string> FindTemplates(
        IReadOnlyList<string> roots,
        LocalNavigationIconGroup group)
    {
        return EnumerateImages(roots).Where(path => MatchesGroup(Path.GetFileNameWithoutExtension(path), group));
    }

    public static IEnumerable<string> FindTalkTemplates(IReadOnlyList<string> roots)
    {
        return EnumerateImages(roots).Where(path =>
            Path.GetFileNameWithoutExtension(path).Contains("IconInTalk", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateImages(IReadOnlyList<string> roots)
    {
        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).ToList();
            }
            catch
            {
                continue;
            }

            foreach (var file in files.Where(file =>
                         SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)))
            {
                yield return file;
            }
        }
    }

    private static bool MatchesGroup(string name, LocalNavigationIconGroup group)
    {
        return group switch
        {
            LocalNavigationIconGroup.Bigmap =>
                name.Contains("Bigmap", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Proce_For_Bigmap", StringComparison.OrdinalIgnoreCase),
            LocalNavigationIconGroup.Into => name.Contains("Into", StringComparison.OrdinalIgnoreCase),
            LocalNavigationIconGroup.Start => name.Contains("Start", StringComparison.OrdinalIgnoreCase),
            LocalNavigationIconGroup.Finish => name.Contains("Finish", StringComparison.OrdinalIgnoreCase),
            LocalNavigationIconGroup.Enter => name.Contains("Enter", StringComparison.OrdinalIgnoreCase),
            LocalNavigationIconGroup.Question => name.Contains("Question", StringComparison.OrdinalIgnoreCase),
            LocalNavigationIconGroup.Task => name.Contains("Task", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
