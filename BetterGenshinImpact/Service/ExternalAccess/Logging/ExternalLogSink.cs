using Serilog.Core;
using Serilog.Events;

namespace BetterGenshinImpact.Service.ExternalAccess.Logging;

/// <summary>
/// 将现有 Serilog 事件转交给非阻塞外部日志分发器。
/// </summary>
public sealed class ExternalLogSink(ExternalLogHub logHub, string instanceIdentity) : ILogEventSink
{
    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        var source = logEvent.Properties.TryGetValue("SourceContext", out var sourceValue)
                     && sourceValue is ScalarValue { Value: string sourceContext }
            ? sourceContext
            : null;

        logHub.Publish(
            logEvent.Timestamp.ToUniversalTime(),
            logEvent.Level,
            source,
            logEvent.RenderMessage(),
            logEvent.Exception?.ToString(),
            instanceIdentity);
    }
}
