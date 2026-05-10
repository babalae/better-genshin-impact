using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.Service.GearTask.Execution;

/// <summary>
/// 支持节点内部恢复的 GearTask 需要实现的接口。
/// 执行器会在节点执行前注入上次记录下来的恢复令牌。
/// </summary>
public interface IGearTaskResumable
{
    Task ApplyResumeTokenAsync(string? resumeTokenJson, CancellationToken ct);
}
