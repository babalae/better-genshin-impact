using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 只向任务服务登记能力定义、不执行任何节点的测试执行器，用于验证菜单和插入命令。
/// </summary>
internal sealed class PuloniaDefinitionOnlyExecutor : IPuloniaTaskExecutor
{
    /// <summary>
    /// 使用给定的能力定义建立执行器。
    /// </summary>
    /// <param name="definitions">要登记的能力定义。</param>
    public PuloniaDefinitionOnlyExecutor(IReadOnlyList<PuloniaTaskDefinition> definitions)
    {
        Definitions = definitions;
    }

    /// <inheritdoc />
    public IReadOnlyList<PuloniaTaskDefinition> Definitions { get; }

    /// <summary>
    /// 测试不运行这些能力，被调用说明测试意外提交了运行。
    /// </summary>
    /// <param name="task">准备好的节点。</param>
    /// <param name="context">执行上下文。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>不返回。</returns>
    /// <exception cref="InvalidOperationException">始终抛出。</exception>
    public Task<PuloniaTaskOutcome> ExecuteAsync(PuloniaTaskPreparedTask task, PuloniaTaskExecutionContext context,
        CancellationToken ct) => throw new InvalidOperationException("测试执行器只登记能力定义。");
}
