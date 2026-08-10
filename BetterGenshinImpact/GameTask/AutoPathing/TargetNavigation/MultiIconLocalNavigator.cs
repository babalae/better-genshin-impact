using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoPathing.TargetNavigation;

public enum LocalNavigationIconGroup
{
    Bigmap,
    Into,
    Start,
    Finish,
    Enter,
    Question,
    Task
}

public sealed record LocalNavigationIconMatch(
    LocalNavigationIconGroup Group,
    double X,
    double Y,
    double Confidence);

public sealed class LocalNavigationObservation
{
    public bool Reached { get; init; }

    public bool InTalk { get; init; }

    public IReadOnlyList<LocalNavigationIconMatch> Matches { get; init; } = [];

    public double? RemainingGameDistance { get; init; }
}

public sealed class LocalTargetNavigationRequest
{
    public string MapName { get; init; } = "Teyvat";

    public string? MapMatchMethod { get; init; }

    public RouteGraphPoint TargetImagePoint { get; init; }

    public double RemainingGameDistance { get; init; }

    public RouteNavigationCostOptions Options { get; init; } = new();

    public IReadOnlyList<LocalNavigationIconGroup> TemplateGroups { get; init; } =
    [
        LocalNavigationIconGroup.Task,
        LocalNavigationIconGroup.Question,
        LocalNavigationIconGroup.Bigmap,
        LocalNavigationIconGroup.Into,
        LocalNavigationIconGroup.Start,
        LocalNavigationIconGroup.Finish,
        LocalNavigationIconGroup.Enter
    ];
}

public enum LocalNavigationCompletionMode
{
    None,
    Icon,
    Coordinate
}

public enum LocalNavigationFailureCode
{
    None,
    IconUnavailableOutsideSafeDistance,
    CoordinateNavigationFailed,
    TimedOut,
    Cancelled,
    Unexpected
}

public sealed record LocalTargetNavigationResult(
    bool Succeeded,
    LocalNavigationCompletionMode CompletionMode,
    LocalNavigationFailureCode FailureCode = LocalNavigationFailureCode.None,
    string? Detail = null)
{
    public static LocalTargetNavigationResult Completed(LocalNavigationCompletionMode mode) =>
        new(true, mode);

    public static LocalTargetNavigationResult Failed(LocalNavigationFailureCode code, string? detail = null) =>
        new(false, LocalNavigationCompletionMode.None, code, detail);
}

public interface ILocalTargetNavigator
{
    Task<LocalTargetNavigationResult> NavigateAsync(
        LocalTargetNavigationRequest request,
        CancellationToken cancellationToken = default);
}

public interface ILocalNavigationPerception
{
    Task<LocalNavigationObservation> ObserveAsync(
        LocalTargetNavigationRequest request,
        IReadOnlyList<LocalNavigationIconGroup> templateGroups,
        CancellationToken cancellationToken);
}

public interface ILocalNavigationMotion
{
    Task AdvanceTowardIconAsync(
        LocalTargetNavigationRequest request,
        LocalNavigationIconMatch icon,
        CancellationToken cancellationToken);

    Task<bool> NavigateToCoordinateAsync(
        LocalTargetNavigationRequest request,
        CancellationToken cancellationToken);

    Task RequestTrackedQuestMarkerAsync(
        LocalTargetNavigationRequest request,
        CancellationToken cancellationToken);

    void ReleaseAllInputs();
}

/// <summary>
/// 可等待的局部导航器。先按任务图标优先级跟随多组模板；只有图标连续不可用且目标在安全距离内，
/// 才允许坐标直达。
/// </summary>
public sealed class MultiIconLocalNavigator(
    ILocalNavigationPerception perception,
    ILocalNavigationMotion motion) : ILocalTargetNavigator
{
    public async Task<LocalTargetNavigationResult> NavigateAsync(
        LocalTargetNavigationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        var misses = 0;
        var remainingDistance = request.RemainingGameDistance;

        try
        {
            while (stopwatch.Elapsed.TotalSeconds < Math.Max(1, request.Options.LocalFollowTimeoutSeconds))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var observation = await perception.ObserveAsync(request, request.TemplateGroups, cancellationToken);
                remainingDistance = observation.RemainingGameDistance ?? remainingDistance;
                if (observation.Reached || observation.InTalk)
                {
                    return LocalTargetNavigationResult.Completed(LocalNavigationCompletionMode.Icon);
                }

                var icon = SelectByPriority(observation.Matches, request.TemplateGroups);
                if (icon != null)
                {
                    misses = 0;
                    await motion.AdvanceTowardIconAsync(request, icon, cancellationToken);
                    continue;
                }

                misses++;
                if (misses < Math.Max(1, request.Options.LocalIconMissRetryCount))
                {
                    await motion.RequestTrackedQuestMarkerAsync(request, cancellationToken);
                    continue;
                }

                if (remainingDistance > request.Options.LocalDirectMaxGameDistance)
                {
                    return LocalTargetNavigationResult.Failed(
                        LocalNavigationFailureCode.IconUnavailableOutsideSafeDistance,
                        $"remaining {remainingDistance:F1}, safe {request.Options.LocalDirectMaxGameDistance:F1}");
                }

                var reached = await motion.NavigateToCoordinateAsync(request, cancellationToken);
                return reached
                    ? LocalTargetNavigationResult.Completed(LocalNavigationCompletionMode.Coordinate)
                    : LocalTargetNavigationResult.Failed(LocalNavigationFailureCode.CoordinateNavigationFailed);
            }

            return LocalTargetNavigationResult.Failed(LocalNavigationFailureCode.TimedOut);
        }
        catch (OperationCanceledException)
        {
            return LocalTargetNavigationResult.Failed(LocalNavigationFailureCode.Cancelled);
        }
        catch (Exception ex)
        {
            return LocalTargetNavigationResult.Failed(LocalNavigationFailureCode.Unexpected, ex.Message);
        }
        finally
        {
            motion.ReleaseAllInputs();
        }
    }

    private static LocalNavigationIconMatch? SelectByPriority(
        IReadOnlyList<LocalNavigationIconMatch> matches,
        IReadOnlyList<LocalNavigationIconGroup> priority)
    {
        // 置信度不参与决策：模板匹配只有命中/未命中，同一分组内多个模板不存在可信的分数排序。
        return matches
            .OrderBy(match => IndexOf(priority, match.Group))
            .FirstOrDefault();
    }

    private static int IndexOf(
        IReadOnlyList<LocalNavigationIconGroup> priority,
        LocalNavigationIconGroup group)
    {
        for (var index = 0; index < priority.Count; index++)
        {
            if (priority[index] == group)
            {
                return index;
            }
        }

        return int.MaxValue;
    }
}
