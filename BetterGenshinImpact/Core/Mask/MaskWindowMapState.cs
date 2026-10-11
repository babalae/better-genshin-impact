using System;
using System.Threading;
using System.Windows;

namespace BetterGenshinImpact.Core.Mask;

/// <summary>
/// 地图点位区域状态的存储。写入时按字段合并，内容不变则不通知
/// </summary>
public sealed class MaskWindowMapState : IMaskWindowMapState, IMaskWindowSnapshotSource<MaskWindowMapSnapshot>
{
    private readonly object _lock = new();
    private MaskWindowMapSnapshot _current = MaskWindowMapSnapshot.Empty;

    public MaskWindowMapSnapshot Current => Volatile.Read(ref _current);

    public event Action? Changed;

    public void Update(bool? isInBigMap = null, Rect? bigMapViewport = null, Rect? miniMapViewport = null)
    {
        lock (_lock)
        {
            var next = new MaskWindowMapSnapshot(
                isInBigMap ?? _current.IsInBigMap,
                bigMapViewport ?? _current.BigMapViewport,
                miniMapViewport ?? _current.MiniMapViewport);
            if (next == _current)
            {
                return;
            }

            Volatile.Write(ref _current, next);
        }

        Changed?.Invoke();
    }

    public void Reset()
    {
        lock (_lock)
        {
            if (_current == MaskWindowMapSnapshot.Empty)
            {
                return;
            }

            Volatile.Write(ref _current, MaskWindowMapSnapshot.Empty);
        }

        Changed?.Invoke();
    }
}
