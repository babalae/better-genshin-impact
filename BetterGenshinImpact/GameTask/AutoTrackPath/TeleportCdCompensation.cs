using System;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 传送冻结 CD 补偿器（单机专用）。
/// 传送/加载期间游戏世界冻结、技能 CD 不流逝，但 CD 计算基于挂钟时间外推会把这段误算成流逝。
/// 记录坐标传送耗时（打开大地图到返回主界面），传送成功后把队伍 CD 时间戳整体后推该时长，抵消误差。
/// 在分发器层包一次即同时覆盖公版(_official)与茶包版(_fastDrag)，不重复补偿。
/// 纯内存补偿，不新增任何截图/OCR/等待，不改传送逻辑；异常/超时不进入补偿（直接透传，由调用方保证只在此方法前调用）。
/// 仅单人世界补偿：联机（多人世界）传送/加载期间游戏世界不暂停、CD 正常流逝，不能把这段当冻结时间。
/// </summary>
public sealed class TeleportCdCompensation
{
    private readonly Func<bool> _isSoloWorld;
    private DateTime _startUtc;

    /// <summary>
    /// 构造补偿器。
    /// </summary>
    /// <param name="isSoloWorld">是否处于单人世界的判定；缺省视为单人世界（公版无联机锄地，恒单机语义）。</param>
    public TeleportCdCompensation(Func<bool>? isSoloWorld = null)
    {
        _isSoloWorld = isSoloWorld ?? (() => true);
    }

    /// <summary>
    /// 传送开始前调用，记录起始时刻。
    /// </summary>
    public void Start()
    {
        _startUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// 传送成功后调用；若为单人世界则把队伍 CD 时间戳整体后推传送耗时。
    /// 异常/超时路径不应调用（调用方在转发返回成功后才调用，异常直接向上透传）。
    /// </summary>
    public void CompensateIfSolo()
    {
        if (!TeleportCdCompensationDecisions.ShouldCompensate(_isSoloWorld()))
        {
            return;
        }
        RunnerContext.Instance.CompensateFrozenCd(DateTime.UtcNow - _startUtc);
    }
}