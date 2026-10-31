using System.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Runtime;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 传送任务的工厂，也是可插拔能力“传送”。所有传送和大地图操作都先经它创建 <see cref="ITpTask"/>：
/// 内置实现创建 <see cref="TpTask"/>，插件可以返回自己的完整实现。
/// </summary>
/// <remarks>
/// 返回的实例只依赖传入的参数，不依赖工厂实例：插件停用后，已经创建的实例仍可以用到所在任务结束（插件程序集不会卸载），
/// 之后新创建的实例改用默认实现。
/// </remarks>
[Pluggable("teleport", "传送", Description = "大地图传送，以及打开大地图、切换区域、移动和缩放地图等大地图操作。")]
public interface ITpTaskFactory
{
    /// <summary>
    /// 创建绑定到指定运行环境的传送任务。
    /// </summary>
    /// <param name="runtime">本次运行使用的运行环境。</param>
    /// <param name="config">配置根。</param>
    /// <param name="ct">取消令牌。</param>
    ITpTask Create(GameRuntime runtime, AllConfig config, CancellationToken ct);
}
