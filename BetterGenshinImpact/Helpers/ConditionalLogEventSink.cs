using System;
using Serilog.Core;
using Serilog.Events;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// 仅在条件满足时才将日志事件转发给内部 sink 的包装器。
/// 条件在每次写入时动态判断（而非创建时固定），因此运行中切换状态也能即时生效。
/// 用于“遮罩日志框：仅当遮罩启用且日志框可见时才写入，隐藏时零 UI 开销”的场景（#3357）。
/// </summary>
public sealed class ConditionalLogEventSink : ILogEventSink, IDisposable
{
    private readonly ILogEventSink _innerSink;
    private readonly Func<bool> _isEnabled;

    public ConditionalLogEventSink(ILogEventSink innerSink, Func<bool> isEnabled)
    {
        _innerSink = innerSink;
        _isEnabled = isEnabled;
    }

    public void Emit(LogEvent logEvent)
    {
        if (_isEnabled())
        {
            _innerSink.Emit(logEvent);
        }
    }

    public void Dispose()
    {
        (_innerSink as IDisposable)?.Dispose();
    }
}
