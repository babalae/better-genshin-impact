using BetterGenshinImpact.GameTask.AutoSkip;
using System.Drawing;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoSkipTests;

public class PopupClosePauseControllerTests : IDisposable
{
    private static readonly Rectangle GameRect = new(100, 200, 1920, 1080);
    private static readonly Rectangle TargetRect = new(1950, 220, 36, 36);
    private readonly object _owner = new();

    public PopupClosePauseControllerTests()
    {
        PopupClosePauseController.Reset();
    }

    [Fact]
    public void ClickOutsideHitRect_ShouldNotPauseTarget()
    {
        PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect);

        PopupClosePauseController.RecordLeftButtonDown(new Point(GameRect.Left + 100, TargetRect.Bottom + 100));

        var state = PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect);
        Assert.False(state.IsPausedByUser);
    }

    [Fact]
    public void ClickInsideFullWidthHitRect_ShouldPauseSameTarget()
    {
        PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect);

        PopupClosePauseController.RecordLeftButtonDown(new Point(GameRect.Left + 100, TargetRect.Top));

        var state = PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect);
        Assert.True(state.IsPausedByUser);
    }

    [Fact]
    public void TargetMissing_ThenNewTargetWithinClickWindow_ShouldPauseNewTarget()
    {
        var clickedAt = DateTime.UtcNow;
        PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect, clickedAt);
        PopupClosePauseController.RecordLeftButtonDown(new Point(GameRect.Left + 100, TargetRect.Top), clickedAt);

        PopupClosePauseController.MarkTargetMissing(_owner);
        var state = PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect, clickedAt.AddMilliseconds(1500));

        Assert.True(state.IsPausedByUser);
    }

    [Fact]
    public void TargetMissing_ThenNewTargetAfterClickWindow_ShouldNotPauseNewTarget()
    {
        var clickedAt = DateTime.UtcNow;
        PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect, clickedAt);
        PopupClosePauseController.RecordLeftButtonDown(new Point(GameRect.Left + 100, TargetRect.Top), clickedAt);

        PopupClosePauseController.MarkTargetMissing(_owner);
        var state = PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect, clickedAt.AddSeconds(3));

        Assert.False(state.IsPausedByUser);
    }

    [Fact]
    public void ClickWithoutActiveTracking_ShouldBeIgnored()
    {
        var clickedAt = DateTime.UtcNow;
        PopupClosePauseController.RecordLeftButtonDown(new Point(GameRect.Left + 100, TargetRect.Top), clickedAt);

        var state = PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect, clickedAt.AddMilliseconds(500));

        Assert.False(state.IsPausedByUser);
    }

    [Fact]
    public void ClickBeforeFirstObservation_InsideFutureHitRect_ShouldPauseTarget()
    {
        var clickedAt = DateTime.UtcNow;
        PopupClosePauseController.StartTracking(_owner);
        PopupClosePauseController.RecordLeftButtonDown(new Point(GameRect.Left + 100, TargetRect.Top), clickedAt);

        var state = PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect, clickedAt.AddMilliseconds(500));

        Assert.True(state.IsPausedByUser);
    }

    [Fact]
    public void ClickBeforeFirstObservation_OutsideFutureHitRect_ShouldNotPauseTarget()
    {
        var clickedAt = DateTime.UtcNow;
        PopupClosePauseController.StartTracking(_owner);
        PopupClosePauseController.RecordLeftButtonDown(new Point(GameRect.Left + 100, TargetRect.Bottom + 100), clickedAt);

        var state = PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect, clickedAt.AddMilliseconds(500));

        Assert.False(state.IsPausedByUser);
    }

    [Fact]
    public void NewTargetWithoutPrecedingClick_ShouldNotPauseOrDelay()
    {
        var state = PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect);

        Assert.False(state.IsPausedByUser);
    }

    [Fact]
    public void ClickOutsideCurrentHitRect_ShouldNotCreatePendingClickForNewTarget()
    {
        var clickedAt = DateTime.UtcNow;
        PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect, clickedAt);
        PopupClosePauseController.RecordLeftButtonDown(new Point(GameRect.Left + 100, TargetRect.Bottom + 100), clickedAt);

        PopupClosePauseController.MarkTargetMissing(_owner);
        var state = PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect, clickedAt.AddMilliseconds(500));

        Assert.False(state.IsPausedByUser);
    }

    [Fact]
    public void StopTracking_ShouldClearPendingClick()
    {
        var clickedAt = DateTime.UtcNow;
        PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect, clickedAt);
        PopupClosePauseController.RecordLeftButtonDown(new Point(GameRect.Left + 100, TargetRect.Top), clickedAt);

        PopupClosePauseController.StopTracking(_owner);
        var state = PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect, clickedAt.AddMilliseconds(500));

        Assert.False(state.IsPausedByUser);
    }

    [Fact]
    public void StopTracking_ShouldClearClickRecordedBeforeFirstObservation()
    {
        var clickedAt = DateTime.UtcNow;
        PopupClosePauseController.StartTracking(_owner);
        PopupClosePauseController.RecordLeftButtonDown(new Point(GameRect.Left + 100, TargetRect.Top), clickedAt);

        PopupClosePauseController.StopTracking(_owner);
        var state = PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect, clickedAt.AddMilliseconds(500));

        Assert.False(state.IsPausedByUser);
    }

    [Fact]
    public void NonTrackingOwnerStop_ShouldNotClearClickRecordedBeforeFirstObservation()
    {
        var clickedAt = DateTime.UtcNow;
        PopupClosePauseController.StartTracking(_owner);
        PopupClosePauseController.RecordLeftButtonDown(new Point(GameRect.Left + 100, TargetRect.Top), clickedAt);

        PopupClosePauseController.StopTracking(new object());
        var state = PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect, clickedAt.AddMilliseconds(500));

        Assert.True(state.IsPausedByUser);
    }

    [Fact]
    public void AnotherOwnerStartTracking_ShouldNotStealPendingClick()
    {
        var clickedAt = DateTime.UtcNow;
        PopupClosePauseController.StartTracking(_owner);
        PopupClosePauseController.RecordLeftButtonDown(new Point(GameRect.Left + 100, TargetRect.Top), clickedAt);

        var otherOwner = new object();
        PopupClosePauseController.StartTracking(otherOwner);

        var otherState = PopupClosePauseController.ObserveTarget(otherOwner, TargetRect, GameRect, clickedAt.AddMilliseconds(500));
        var ownerState = PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect, clickedAt.AddMilliseconds(500));

        Assert.False(otherState.IsOwner);
        Assert.True(ownerState.IsPausedByUser);
    }

    [Fact]
    public void NonOwnerMissing_ShouldNotClearCurrentTarget()
    {
        PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect);
        PopupClosePauseController.RecordLeftButtonDown(new Point(GameRect.Left + 100, TargetRect.Top));

        PopupClosePauseController.MarkTargetMissing(new object());
        var state = PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect);

        Assert.True(state.IsOwner);
        Assert.True(state.IsPausedByUser);
    }

    [Fact]
    public void AnotherOwnerObservingSameTarget_ShouldNotTakeOwnership()
    {
        PopupClosePauseController.ObserveTarget(_owner, TargetRect, GameRect);
        PopupClosePauseController.RecordLeftButtonDown(new Point(GameRect.Left + 100, TargetRect.Top));

        var otherOwner = new object();
        var state = PopupClosePauseController.ObserveTarget(otherOwner, TargetRect, GameRect);

        Assert.False(state.IsOwner);
        Assert.True(state.IsPausedByUser);
        Assert.True(PopupClosePauseController.IsPausedByUser(_owner));
        Assert.False(PopupClosePauseController.IsPausedByUser(otherOwner));
    }

    public void Dispose()
    {
        PopupClosePauseController.Reset();
    }
}
