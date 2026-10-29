using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.Runtime;

namespace BetterGenshinImpact.GameTask.AutoStygianOnslaught;

/// <summary>
/// 危战勋望进度：当前累计值与目标值。
/// </summary>
/// <param name="Current">当前累计的危战勋望。</param>
/// <param name="Target">游戏显示的目标值。</param>
public sealed record StygianDirePrestigeProgress(int Current, int Target);

/// <summary>
/// 读取幽境危战界面的危战勋望进度（预留）。
/// 识别位置为危战界面的勋望进度，预期在每次领奖前调用；需要的截图由用户提供后再实现识别。
/// 返回 null 表示未识别，调用方必须当作“未知”而不是 0。识别实现后只替换 <see cref="ReadAsync"/> 的内部逻辑。
/// </summary>
public static class StygianDirePrestigeReader
{
    /// <summary>紊乱爆发期内领取额外圣遗物奖励所需累计的危战勋望。</summary>
    public const int CommendationPrestige = 1200;

    /// <summary>
    /// 读取当前危战勋望进度。本期没有识别实现，固定返回 null。
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>勋望进度；未识别时为 null。</returns>
    public static Task<StygianDirePrestigeProgress?> ReadAsync(GameRuntime runtime, CancellationToken ct)
    {
        // 预留：识别实现前不读取界面
        return Task.FromResult<StygianDirePrestigeProgress?>(null);
    }
}
