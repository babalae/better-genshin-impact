using System.Threading;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Runtime;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

/// <summary>
/// 内置传送，是传送可插拔能力的默认实现：创建 <see cref="TpTask"/>。
/// </summary>
[PluggableImpl("standard", "内置传送", IsDefault = true, Description = "主程序内置的传送。")]
public sealed class BuiltinTpTaskFactory : ITpTaskFactory
{
    /// <inheritdoc />
    public ITpTask Create(GameRuntime runtime, AllConfig config, CancellationToken ct) => new TpTask(runtime, config, ct);
}
