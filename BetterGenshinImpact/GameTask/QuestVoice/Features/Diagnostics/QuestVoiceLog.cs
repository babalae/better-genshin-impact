using Serilog;
using Serilog.Events;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;

/// <summary>QuestVoice结构化日志边界：详细事件走主文件，用户摘要走悬浮。</summary>
public static class QuestVoiceLog
{
    /// <summary>按属性分流，不解析渲染后的日志文本。</summary>
    public const string KindProperty = "QuestVoiceLogKind", DiagnosticKind = "Diagnostic", SummaryKind = "Summary";

    /// <summary>延迟读取当前主Logger，避免在App配置前捕获静默Logger。</summary>
    public static ILogger Diagnostic => ForDiagnostic(Log.Logger);

    /// <summary>业务诊断保留原级别及结构，显式标记为非UI内容。</summary>
    public static ILogger ForDiagnostic(ILogger logger) => logger.ForContext(KindProperty, DiagnosticKind);

    /// <summary>摘要没有异常对象、资产路径或内部结构；不在文件额外复制OCR正文。</summary>
    public static void WriteSummary(LogEventLevel level, string message) => Log.ForContext(KindProperty, SummaryKind)
        .Write(level, "{QuestVoiceSummary}", message);

    /// <summary>文件仍记录原始业务事件，摘要只承担用户显示。</summary>
    public static bool IsSummary(LogEvent value) => value.Properties.TryGetValue(KindProperty, out var kind)
        && kind is ScalarValue { Value: SummaryKind };

    /// <summary>其他模块按原规则通过；Service的结构化SourceContext也是详细诊断边界。</summary>
    public static bool IsOverlayVisible(LogEvent value)
    {
        if (value.Properties.ContainsKey(KindProperty)) return IsSummary(value);
        return !value.Properties.TryGetValue("SourceContext", out var source)
            || source is not ScalarValue { Value: "BetterGenshinImpact.Service.QuestVoiceService" or "QuestVoice" };
    }
}
