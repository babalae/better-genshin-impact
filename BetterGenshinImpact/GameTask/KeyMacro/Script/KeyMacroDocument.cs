using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.KeyMacro.Script;

/// <summary>
/// 一段按键宏脚本的解析结果
/// </summary>
public sealed class KeyMacroDocument
{
    /// <summary>
    /// 空脚本的解析结果
    /// </summary>
    public static KeyMacroDocument Empty { get; } = new([], []);

    /// <summary>
    /// 按出现顺序排列的步骤：有效指令和注释。无法识别的指令不在其中，只出现在 <see cref="Diagnostics"/> 里
    /// </summary>
    public IReadOnlyList<KeyMacroStep> Steps { get; }

    /// <summary>
    /// 全部诊断，按行号排序
    /// </summary>
    public IReadOnlyList<KeyMacroDiagnostic> Diagnostics { get; }

    /// <summary>
    /// 可执行的指令（不含注释）
    /// </summary>
    public IReadOnlyList<KeyMacroStep> Commands { get; }

    /// <summary>
    /// 错误数量
    /// </summary>
    public int ErrorCount { get; }

    /// <summary>
    /// 是否存在错误
    /// </summary>
    public bool HasError => ErrorCount > 0;

    /// <summary>
    /// 一轮中全部 wait 的总时长（毫秒）
    /// </summary>
    public int TotalWaitMs { get; }

    /// <summary>
    /// 创建解析结果
    /// </summary>
    /// <param name="steps">步骤</param>
    /// <param name="diagnostics">诊断</param>
    public KeyMacroDocument(IReadOnlyList<KeyMacroStep> steps, IReadOnlyList<KeyMacroDiagnostic> diagnostics)
    {
        Steps = steps;
        Diagnostics = diagnostics.OrderBy(d => d.Line).ThenBy(d => d.Column).ToList();
        Commands = steps.Where(s => s.IsCommand).ToList();
        ErrorCount = diagnostics.Count(d => d.IsError);
        TotalWaitMs = Commands.Where(s => s.Kind == KeyMacroCommandKind.Wait).Sum(s => s.WaitMs);
    }
}
