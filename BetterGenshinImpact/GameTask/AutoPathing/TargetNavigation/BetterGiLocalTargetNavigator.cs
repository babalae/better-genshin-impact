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
using Microsoft.Extensions.Logging;
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
/// 每次导航会话创建一次感知器：启动时扫描并解码模板，导航结束统一释放 Mat。
/// </summary>
public sealed class BetterGiLocalTargetNavigator : ILocalTargetNavigator
{
    private readonly IRouteCurrentPositionResolver _resolver;
    private readonly IRouteCoordinateConverter _converter;
    private readonly IReadOnlyList<string>? _templateRoots;

    public BetterGiLocalTargetNavigator(
        IRouteCurrentPositionResolver? positionResolver = null,
        IRouteCoordinateConverter? coordinateConverter = null,
        IReadOnlyList<string>? templateRoots = null)
    {
        _resolver = positionResolver ?? RouteCurrentPositionResolver.Instance;
        _converter = coordinateConverter ?? RouteNavigationCoordinateService.Instance;
        _templateRoots = templateRoots;
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
            using var perception = new BetterGiLocalNavigationPerception(_resolver, _converter, _templateRoots);
            var navigator = new MultiIconLocalNavigator(
                perception,
                new BetterGiLocalNavigationMotion(_resolver, _converter));
            result = await navigator.NavigateAsync(request, cancellationToken);
        });

        return !started
            ? LocalTargetNavigationResult.Failed(
                LocalNavigationFailureCode.Unexpected,
                "TaskRunner 正被其他独立任务占用")
            : result ?? LocalTargetNavigationResult.Failed(LocalNavigationFailureCode.Unexpected);
    }
}

/// <summary>
/// 局部导航感知器。模板在构造时一次性扫描并解码缓存，ObserveAsync 只做画面匹配，不读磁盘。
/// </summary>
internal sealed class BetterGiLocalNavigationPerception(
    IRouteCurrentPositionResolver positionResolver,
    IRouteCoordinateConverter coordinateConverter,
    IReadOnlyList<string>? templateRoots = null) : ILocalNavigationPerception, IDisposable
{
    private readonly LocalNavigationTemplateCache _templates =
        new(templateRoots ?? LocalNavigationTemplateCatalog.FindDefaultRoots());

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
                    matches.Add(ToMatch(group, taskMarker));
                }
            }

            foreach (var template in _templates.GetByGroup(group))
            {
                var match = TryMatchTemplate(screen, template, request.Options.LocalTemplateThreshold);
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
            InTalk = _templates.TalkTemplates
                .Any(template => TryMatchTemplate(
                    screen,
                    template,
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
        CachedLocalNavigationTemplate template,
        double threshold,
        bool talkTemplate = false)
    {
        try
        {
            var scale = screen.Width / 1920d;
            var recognition = template.Recognition;
            recognition.Threshold = threshold;
            recognition.RegionOfInterest = new OpenCvSharp.Rect(
                (int)Math.Round((talkTemplate ? 0 : 300) * scale),
                (int)Math.Round((talkTemplate ? 0 : 100) * scale),
                (int)Math.Round((talkTemplate ? 500 : 1300) * scale),
                (int)Math.Round((talkTemplate ? 200 : 800) * scale));

            using var region = screen.Find(recognition);
            return region.IsExist() ? ToMatch(template.Group, region) : null;
        }
        catch
        {
            return null;
        }
    }

    private static LocalNavigationIconMatch ToMatch(
        LocalNavigationIconGroup group,
        Region region)
    {
        // 匹配成功即视为存在，置信度不参与决策，见 MultiIconLocalNavigator.SelectByPriority。
        return new LocalNavigationIconMatch(
            group,
            region.X + region.Width / 2d,
            region.Y + region.Height / 2d,
            1.0);
    }

    private static double Distance(RouteGamePoint from, RouteGamePoint to)
    {
        var dx = from.X - to.X;
        var dy = from.Y - to.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public void Dispose()
    {
        _templates.Dispose();
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

/// <summary>
/// 局部导航图标模板目录与显式清单。分组由清单直接声明，不依赖文件名猜测。
/// </summary>
internal static class LocalNavigationTemplateCatalog
{
    /// <summary>相对模板根目录的路径 → 图标分组。每个模板只属于一个分组。</summary>
    internal static readonly (string RelativePath, LocalNavigationIconGroup Group, bool TalkTemplate)[] TemplateManifest =
    [
        ("Commission/IconBigmapCommission.jpg", LocalNavigationIconGroup.Bigmap, false),
        ("Commission/IconQuestionCommission.png", LocalNavigationIconGroup.Question, false),
        ("Commission/IconTaskCommission.png", LocalNavigationIconGroup.Task, false),
        ("Icon/Branch_Enter_For_Into.png", LocalNavigationIconGroup.Into, false),
        ("Icon/Branch_Finish.png", LocalNavigationIconGroup.Finish, false),
        ("Icon/Branch_Proce_For_Bigmap.png", LocalNavigationIconGroup.Bigmap, false),
        ("Icon/Branch_Start.png", LocalNavigationIconGroup.Start, false),
        ("Icon/Common02_Enter.png", LocalNavigationIconGroup.Enter, false),
        ("Icon/Common02_Finish.png", LocalNavigationIconGroup.Finish, false),
        ("Icon/Common02_Proce_For_Bigmap.png", LocalNavigationIconGroup.Bigmap, false),
        ("Icon/Common02_Start.png", LocalNavigationIconGroup.Start, false),
        ("Icon/Common_Enter.png", LocalNavigationIconGroup.Enter, false),
        ("Icon/Common_Finish.png", LocalNavigationIconGroup.Finish, false),
        ("Icon/Common_Proce_For_Bigmap.png", LocalNavigationIconGroup.Bigmap, false),
        ("Icon/Common_Start.png", LocalNavigationIconGroup.Start, false),
        ("Icon/Main_Enter.png", LocalNavigationIconGroup.Enter, false),
        ("Icon/Main_Finish.png", LocalNavigationIconGroup.Finish, false),
        ("Icon/Main_Proce_For_Bigmap.png", LocalNavigationIconGroup.Bigmap, false),
        ("Icon/Main_Start.png", LocalNavigationIconGroup.Start, false),
        ("IconInTalk.png", LocalNavigationIconGroup.Task, true)
    ];

    public static IReadOnlyList<string> FindDefaultRoots()
    {
        var root = Global.Absolute(Path.Combine("Assets", "AutoPathing", "LocalNavigationIcons"));
        return Directory.Exists(root) ? [root] : [];
    }
}

/// <summary>
/// 一次导航会话内的模板缓存：启动时扫描清单并解码一次，导航结束统一释放 Mat。
/// </summary>
internal sealed class LocalNavigationTemplateCache : IDisposable
{
    private readonly Dictionary<LocalNavigationIconGroup, List<CachedLocalNavigationTemplate>> _byGroup = [];
    private readonly List<CachedLocalNavigationTemplate> _talkTemplates = [];
    private readonly List<CachedLocalNavigationTemplate> _all = [];

    public LocalNavigationTemplateCache(IReadOnlyList<string> roots)
    {
        var existingRoots = roots.Where(Directory.Exists).ToList();
        if (existingRoots.Count == 0)
        {
            TaskControl.Logger.LogWarning(
                "局部导航图标模板目录不存在：{Roots}，本地图标识别将不可用",
                string.Join("; ", roots));
        }

        foreach (var (relativePath, group, talkTemplate) in LocalNavigationTemplateCatalog.TemplateManifest)
        {
            var fullPath = existingRoots
                .Select(root => Path.Combine(root, relativePath))
                .FirstOrDefault(File.Exists);
            if (fullPath == null)
            {
                TaskControl.Logger.LogWarning(
                    "局部导航图标模板缺失：{RelativePath}（在 {Roots} 中均未找到）",
                    relativePath,
                    string.Join("; ", existingRoots));
                continue;
            }

            try
            {
                var template = CachedLocalNavigationTemplate.Load(fullPath, relativePath, group, talkTemplate);
                _all.Add(template);
                if (talkTemplate)
                {
                    _talkTemplates.Add(template);
                }
                else
                {
                    if (!_byGroup.TryGetValue(group, out var list))
                    {
                        list = [];
                        _byGroup[group] = list;
                    }

                    list.Add(template);
                }
            }
            catch (Exception ex)
            {
                TaskControl.Logger.LogWarning(
                    "局部导航图标模板加载失败：{RelativePath}，{Reason}",
                    relativePath,
                    ex.Message);
            }
        }

        if (_all.Count < LocalNavigationTemplateCatalog.TemplateManifest.Length)
        {
            TaskControl.Logger.LogWarning(
                "局部导航图标模板加载不完整：{Loaded}/{Total} 个，缺失模板对应的图标分组将不参与识别",
                _all.Count,
                LocalNavigationTemplateCatalog.TemplateManifest.Length);
        }
    }

    public IReadOnlyList<CachedLocalNavigationTemplate> GetByGroup(LocalNavigationIconGroup group)
    {
        return _byGroup.TryGetValue(group, out var list) ? list : [];
    }

    public IReadOnlyList<CachedLocalNavigationTemplate> TalkTemplates => _talkTemplates;

    public void Dispose()
    {
        foreach (var template in _all)
        {
            template.Dispose();
        }

        _all.Clear();
        _byGroup.Clear();
        _talkTemplates.Clear();
    }
}

/// <summary>
/// 单个已解码的局部导航模板。Mat 只解码一次，由缓存统一释放。
/// </summary>
internal sealed class CachedLocalNavigationTemplate : IDisposable
{
    public string RelativePath { get; }

    public LocalNavigationIconGroup Group { get; }

    public RecognitionObject Recognition { get; }

    private CachedLocalNavigationTemplate(
        string relativePath,
        LocalNavigationIconGroup group,
        RecognitionObject recognition)
    {
        RelativePath = relativePath;
        Group = group;
        Recognition = recognition;
    }

    public static CachedLocalNavigationTemplate Load(
        string fullPath,
        string relativePath,
        LocalNavigationIconGroup group,
        bool talkTemplate)
    {
        var mat = Cv2.ImRead(fullPath, ImreadModes.Color);
        if (mat.Empty())
        {
            throw new InvalidDataException("图片为空或已损坏，无法解码");
        }

        var recognition = RecognitionObject.TemplateMatch(mat);
        recognition.Name = relativePath;
        return new CachedLocalNavigationTemplate(relativePath, group, recognition);
    }

    public void Dispose()
    {
        Recognition.TemplateImageMat?.Dispose();
        Recognition.TemplateImageGreyMat?.Dispose();
        Recognition.MaskMat?.Dispose();
    }
}
