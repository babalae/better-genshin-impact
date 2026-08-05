using BetterGenshinImpact.GameTask.AutoPathing.TargetNavigation;
using BetterGenshinImpact.GameTask.AutoPathing.Telemetry;

namespace BetterGenshinImpact.UnitTest.AutoPathing.TargetNavigation;

public class MultiIconLocalNavigatorTests
{
    [Fact]
    public async Task NavigateAsync_PrioritizesTaskIconAcrossTemplateGroups()
    {
        var perception = new SequencePerception(
            new LocalNavigationObservation
            {
                Matches =
                [
                    new LocalNavigationIconMatch(LocalNavigationIconGroup.Bigmap, 800, 400, 0.95),
                    new LocalNavigationIconMatch(LocalNavigationIconGroup.Task, 1100, 420, 0.86)
                ]
            },
            new LocalNavigationObservation { Reached = true });
        var motion = new RecordingMotion();
        var navigator = new MultiIconLocalNavigator(perception, motion);

        var result = await navigator.NavigateAsync(CreateRequest(200));

        Assert.True(result.Succeeded);
        Assert.Equal(LocalNavigationCompletionMode.Icon, result.CompletionMode);
        Assert.Equal(LocalNavigationIconGroup.Task, Assert.Single(motion.FollowedIcons).Group);
        Assert.Equal(1, motion.ReleaseCount);
    }

    [Fact]
    public async Task NavigateAsync_UsesCoordinateOnlyAfterIconsAreUnavailableWithinSafeDistance()
    {
        var perception = new SequencePerception(
            new LocalNavigationObservation(),
            new LocalNavigationObservation());
        var motion = new RecordingMotion();
        var navigator = new MultiIconLocalNavigator(perception, motion);

        var result = await navigator.NavigateAsync(CreateRequest(
            79,
            new RouteNavigationCostOptions
            {
                LocalDirectMaxGameDistance = 80,
                LocalIconMissRetryCount = 2
            }));

        Assert.True(result.Succeeded);
        Assert.Equal(LocalNavigationCompletionMode.Coordinate, result.CompletionMode);
        Assert.Equal(1, motion.CoordinateFollowCount);
        Assert.Equal(1, motion.ReleaseCount);
    }

    [Fact]
    public async Task NavigateAsync_DoesNotUseCoordinateOutsideSafeDistance()
    {
        var perception = new SequencePerception(new LocalNavigationObservation());
        var motion = new RecordingMotion();
        var navigator = new MultiIconLocalNavigator(perception, motion);

        var result = await navigator.NavigateAsync(CreateRequest(
            81,
            new RouteNavigationCostOptions
            {
                LocalDirectMaxGameDistance = 80,
                LocalIconMissRetryCount = 1
            }));

        Assert.False(result.Succeeded);
        Assert.Equal(LocalNavigationFailureCode.IconUnavailableOutsideSafeDistance, result.FailureCode);
        Assert.Equal(0, motion.CoordinateFollowCount);
        Assert.Equal(1, motion.ReleaseCount);
    }

    [Fact]
    public async Task NavigateAsync_ReleasesInputsWhenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var motion = new RecordingMotion();
        var navigator = new MultiIconLocalNavigator(
            new SequencePerception(new LocalNavigationObservation()),
            motion);

        var result = await navigator.NavigateAsync(CreateRequest(20), cts.Token);

        Assert.False(result.Succeeded);
        Assert.Equal(LocalNavigationFailureCode.Cancelled, result.FailureCode);
        Assert.Equal(1, motion.ReleaseCount);
    }

    private static LocalTargetNavigationRequest CreateRequest(
        double remainingGameDistance,
        RouteNavigationCostOptions? options = null)
    {
        return new LocalTargetNavigationRequest
        {
            MapName = "Teyvat",
            TargetImagePoint = new RouteGraphPoint(100, 200),
            RemainingGameDistance = remainingGameDistance,
            Options = options ?? new RouteNavigationCostOptions()
        };
    }

    private sealed class SequencePerception(params LocalNavigationObservation[] observations) : ILocalNavigationPerception
    {
        private int _index;

        public Task<LocalNavigationObservation> ObserveAsync(
            LocalTargetNavigationRequest request,
            IReadOnlyList<LocalNavigationIconGroup> templateGroups,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observation = observations[Math.Min(_index, observations.Length - 1)];
            _index++;
            return Task.FromResult(observation);
        }
    }

    private sealed class RecordingMotion : ILocalNavigationMotion
    {
        public List<LocalNavigationIconMatch> FollowedIcons { get; } = [];

        public int CoordinateFollowCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public Task AdvanceTowardIconAsync(
            LocalTargetNavigationRequest request,
            LocalNavigationIconMatch icon,
            CancellationToken cancellationToken)
        {
            FollowedIcons.Add(icon);
            return Task.CompletedTask;
        }

        public Task<bool> NavigateToCoordinateAsync(LocalTargetNavigationRequest request, CancellationToken cancellationToken)
        {
            CoordinateFollowCount++;
            return Task.FromResult(true);
        }

        public Task RequestTrackedQuestMarkerAsync(
            LocalTargetNavigationRequest request,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void ReleaseAllInputs()
        {
            ReleaseCount++;
        }
    }
}
