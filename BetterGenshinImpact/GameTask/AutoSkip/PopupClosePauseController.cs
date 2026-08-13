using System;
using System.Drawing;

namespace BetterGenshinImpact.GameTask.AutoSkip;

internal readonly record struct PopupClosePauseState(bool IsOwner, bool IsPausedByUser);

/// <summary>
/// 跟踪当前弹出页关闭按钮，并通过全局鼠标旁听为当前目标提供一次性的自动关闭暂停。
/// </summary>
internal static class PopupClosePauseController
{
    private static readonly object SyncRoot = new();
    private static readonly TimeSpan PendingClickWindow = TimeSpan.FromSeconds(2);

    private static object? _trackingOwner;
    private static object? _owner;
    private static Rectangle? _targetRect;
    private static Rectangle _hitRect;
    private static object? _pendingClickOwner;
    private static Point? _pendingClickPoint;
    private static DateTime _pendingClickAtUtc;
    private static bool _isPausedByUser;

    public static void StartTracking(object owner)
    {
        lock (SyncRoot)
        {
            if (_trackingOwner == null
                || ReferenceEquals(_trackingOwner, owner)
                || (!_targetRect.HasValue && !_pendingClickPoint.HasValue))
            {
                _trackingOwner = owner;
            }
        }
    }

    public static PopupClosePauseState ObserveTarget(object owner, Rectangle targetRect, Rectangle gameRect)
    {
        return ObserveTarget(owner, targetRect, gameRect, DateTime.UtcNow);
    }

    internal static PopupClosePauseState ObserveTarget(object owner, Rectangle targetRect, Rectangle gameRect, DateTime observedAtUtc)
    {
        lock (SyncRoot)
        {
            var isSameTarget = _targetRect.HasValue && IsSameTarget(_targetRect.Value, targetRect);
            if (_owner != null && !ReferenceEquals(_owner, owner) && isSameTarget)
            {
                return new PopupClosePauseState(false, _isPausedByUser);
            }

            var hitRect = BuildHitRect(targetRect, gameRect);
            var timeSinceClick = observedAtUtc - _pendingClickAtUtc;
            var hasPendingClick = _pendingClickPoint.HasValue
                                  && timeSinceClick >= TimeSpan.Zero
                                  && timeSinceClick <= PendingClickWindow;
            if (hasPendingClick
                && _pendingClickOwner != null
                && !ReferenceEquals(_pendingClickOwner, owner))
            {
                return new PopupClosePauseState(false, false);
            }

            if (!hasPendingClick && _pendingClickPoint.HasValue)
            {
                ClearPendingClickCore();
            }

            if (!isSameTarget)
            {
                _isPausedByUser = hasPendingClick && hitRect.Contains(_pendingClickPoint!.Value);
                if (hasPendingClick)
                {
                    ClearPendingClickCore();
                }
            }

            _owner = owner;
            _targetRect = targetRect;
            _hitRect = hitRect;

            return new PopupClosePauseState(true, _isPausedByUser);
        }
    }

    public static void RecordLeftButtonDown(Point point)
    {
        RecordLeftButtonDown(point, DateTime.UtcNow);
    }

    internal static void RecordLeftButtonDown(Point point, DateTime clickedAtUtc)
    {
        lock (SyncRoot)
        {
            if (_targetRect.HasValue)
            {
                if (!_hitRect.Contains(point))
                {
                    return;
                }

                _isPausedByUser = true;
                _pendingClickOwner = _owner;
            }
            else
            {
                if (_trackingOwner == null)
                {
                    return;
                }

                _pendingClickOwner = _trackingOwner;
            }

            _pendingClickPoint = point;
            _pendingClickAtUtc = clickedAtUtc;
        }
    }

    public static bool IsPausedByUser(object owner)
    {
        lock (SyncRoot)
        {
            return ReferenceEquals(_owner, owner) && _isPausedByUser;
        }
    }

    public static bool HasPendingClick(object owner)
    {
        lock (SyncRoot)
        {
            return ReferenceEquals(_pendingClickOwner, owner)
                   && _pendingClickPoint.HasValue
                   && DateTime.UtcNow - _pendingClickAtUtc <= PendingClickWindow;
        }
    }

    public static void MarkTargetMissing(object owner)
    {
        lock (SyncRoot)
        {
            if (ReferenceEquals(_owner, owner))
            {
                ClearTargetCore();
            }
        }
    }

    public static void StopTracking(object owner)
    {
        lock (SyncRoot)
        {
            if (ReferenceEquals(_owner, owner))
            {
                ClearTargetCore();
            }

            if (ReferenceEquals(_pendingClickOwner, owner))
            {
                ClearPendingClickCore();
            }

            if (ReferenceEquals(_trackingOwner, owner))
            {
                _trackingOwner = null;
            }
        }
    }

    public static void Reset()
    {
        lock (SyncRoot)
        {
            _trackingOwner = null;
            ClearTargetCore();
            ClearPendingClickCore();
        }
    }

    private static void ClearTargetCore()
    {
        _owner = null;
        _targetRect = null;
        _hitRect = Rectangle.Empty;
        _isPausedByUser = false;
    }

    private static void ClearPendingClickCore()
    {
        _pendingClickOwner = null;
        _pendingClickPoint = null;
        _pendingClickAtUtc = default;
    }

    private static Rectangle BuildHitRect(Rectangle targetRect, Rectangle gameRect)
    {
        var hitHeight = Math.Max(targetRect.Height, (int)Math.Ceiling(targetRect.Height * 1.5));
        hitHeight = Math.Min(hitHeight, gameRect.Height);

        var top = targetRect.Top - (hitHeight - targetRect.Height) / 2;
        top = Math.Clamp(top, gameRect.Top, gameRect.Bottom - hitHeight);

        return new Rectangle(gameRect.Left, top, gameRect.Width, hitHeight);
    }

    private static bool IsSameTarget(Rectangle first, Rectangle second)
    {
        var tolerance = Math.Max(4, Math.Min(first.Width, first.Height) / 2);
        return Math.Abs(first.Left + first.Width / 2 - (second.Left + second.Width / 2)) <= tolerance
               && Math.Abs(first.Top + first.Height / 2 - (second.Top + second.Height / 2)) <= tolerance;
    }
}
