using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.ViewModel.Windows.Pulonia;

/// <summary>
/// 用户在创建弹窗中确认后生成的一次性任务创建结果。
/// </summary>
public sealed class PuloniaTaskCreationResult
{
    /// <summary>
    /// 用户确认后可以原子插入计划树的完整根节点。
    /// </summary>
    public PuloniaTask Task { get; }

    /// <summary>
    /// 建立不可变的任务创建结果。
    /// </summary>
    public PuloniaTaskCreationResult(PuloniaTask task)
    {
        Task = task;
    }
}
