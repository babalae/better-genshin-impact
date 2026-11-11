namespace BetterGenshinImpact.GameTask.KeyMacro.Script;

/// <summary>
/// 诊断级别
/// </summary>
public enum KeyMacroDiagnosticSeverity
{
    /// <summary>
    /// 错误：对应的指令不会执行
    /// </summary>
    Error,

    /// <summary>
    /// 警告：可以执行，但可能不符合预期
    /// </summary>
    Warning
}

/// <summary>
/// 解析时发现的一个问题
/// </summary>
/// <param name="Severity">级别</param>
/// <param name="Line">行号，从 1 开始；0 表示针对整个脚本</param>
/// <param name="Column">行内起始列，从 0 开始</param>
/// <param name="Length">行内长度，0 表示整行</param>
/// <param name="Message">提示文字</param>
public sealed record KeyMacroDiagnostic(KeyMacroDiagnosticSeverity Severity, int Line, int Column, int Length, string Message)
{
    /// <summary>
    /// 是否为错误
    /// </summary>
    public bool IsError => Severity == KeyMacroDiagnosticSeverity.Error;

    /// <summary>
    /// 带行号的展示文字
    /// </summary>
    public string DisplayText => Line > 0 ? $"第 {Line} 行：{Message}" : Message;
}
