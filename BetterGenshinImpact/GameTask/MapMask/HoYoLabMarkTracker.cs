using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>
/// HoYoLAB 账号标记的本地缓存。
/// 右键切换时先乐观更新缓存，失败再回滚；同一点位同一时间只允许一个请求；
/// 每次切换都会让此前开始的加载结果失效，避免旧快照覆盖刚做的标记。
/// 仅在界面线程上使用。
/// </summary>
public class HoYoLabMarkTracker
{
    /// <summary>
    /// 请求进行中的点位
    /// </summary>
    private readonly HashSet<long> _inFlight = [];

    /// <summary>
    /// 加载版本号；切换和重置都会递增，使进行中的加载结果作废
    /// </summary>
    private int _version;

    /// <summary>
    /// 是否有加载正在进行
    /// </summary>
    private bool _loading;

    /// <summary>
    /// 账号已标记的点位 ID；尚未加载成功时为 null
    /// </summary>
    public HashSet<long>? MarkedIds { get; private set; }

    /// <summary>
    /// 是否需要从账号加载（未加载成功且没有加载在进行）
    /// </summary>
    public bool NeedsLoad => MarkedIds == null && !_loading;

    /// <summary>
    /// 开始一次加载
    /// </summary>
    /// <returns>本次加载的版本号，完成时传回</returns>
    public int BeginLoad()
    {
        _loading = true;
        return ++_version;
    }

    /// <summary>
    /// 加载失败，允许之后重新加载
    /// </summary>
    /// <param name="version">BeginLoad 返回的版本号</param>
    public void FailLoad(int version)
    {
        if (version == _version)
        {
            _loading = false;
        }
    }

    /// <summary>
    /// 加载完成；期间发生过切换或重置时丢弃结果
    /// </summary>
    /// <param name="version">BeginLoad 返回的版本号</param>
    /// <param name="ids">账号标记</param>
    /// <returns>结果是否被采用</returns>
    public bool CompleteLoad(int version, IReadOnlySet<long> ids)
    {
        if (version != _version)
        {
            return false;
        }

        _loading = false;
        MarkedIds = new HashSet<long>(ids);
        return true;
    }

    /// <summary>
    /// 开始切换点位：同一点位请求未完成时拒绝；否则乐观更新缓存并使进行中的加载作废
    /// </summary>
    /// <param name="pointId">点位 ID</param>
    /// <param name="hidden">切换后的状态，true 表示标记</param>
    /// <returns>是否可以发起请求</returns>
    public bool TryBeginToggle(long pointId, bool hidden)
    {
        if (!_inFlight.Add(pointId))
        {
            return false;
        }

        _version++;
        _loading = false;
        Apply(pointId, hidden);
        return true;
    }

    /// <summary>
    /// 切换请求结束；失败时把缓存恢复为切换前的状态
    /// </summary>
    /// <param name="pointId">点位 ID</param>
    /// <param name="hidden">本次切换后的状态</param>
    /// <param name="success">请求是否成功</param>
    public void EndToggle(long pointId, bool hidden, bool success)
    {
        _inFlight.Remove(pointId);
        if (!success)
        {
            Apply(pointId, !hidden);
        }
    }

    /// <summary>
    /// 清空缓存（登录状态变化时），进行中的加载作废
    /// </summary>
    public void Reset()
    {
        MarkedIds = null;
        _inFlight.Clear();
        _loading = false;
        _version++;
    }

    /// <summary>
    /// 把点位状态写入缓存；缓存未加载时忽略
    /// </summary>
    private void Apply(long pointId, bool hidden)
    {
        if (hidden)
        {
            MarkedIds?.Add(pointId);
        }
        else
        {
            MarkedIds?.Remove(pointId);
        }
    }
}
