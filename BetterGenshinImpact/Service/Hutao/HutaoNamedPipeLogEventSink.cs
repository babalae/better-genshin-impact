using System;
using System.IO;
using System.Threading;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace BetterGenshinImpact.Service.Hutao;

internal sealed class HutaoNamedPipeLogEventSink : ILogEventSink
{
    // 连接失败后的冷却时间:胡桃离线时避免每条日志都触发一次失败的管道连接。
    private static readonly TimeSpan ConnectCooldown = TimeSpan.FromSeconds(5);

    private readonly MessageTemplateTextFormatter textFormatter = new("[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

    private HutaoNamedPipe? namedPipe;
    private long lastFailedConnectTicks;

    public HutaoNamedPipeLogEventSink()
    {
    }

    // Serilog 在 DI 容器构建前配置,sink 无法走构造注入,只能首次 Emit 时延迟解析。
    // 解析可能发生在 _host 尚未构建完成时,此时拿不到服务就返回 null,由 Emit 判空跳过。
    private HutaoNamedPipe? NamedPipe => namedPipe ??= App.GetService<HutaoNamedPipe>();

    public void Emit(LogEvent logEvent)
    {
        if (NamedPipe is not { } pipe)
        {
            return;
        }

        // 冷却期内直接丢弃,避免高频日志在胡桃离线时反复尝试连接。
        long now = DateTime.UtcNow.Ticks;
        if (now - Interlocked.Read(ref lastFailedConnectTicks) < ConnectCooldown.Ticks)
        {
            return;
        }

        // Emit 会被多个线程并发调用,这里使用局部的 StringWriter 避免共享缓冲区竞争。
        StringWriter writer = new();
        textFormatter.Format(logEvent, writer);
        if (!pipe.TryRedirectLog(writer.ToString()))
        {
            Interlocked.Exchange(ref lastFailedConnectTicks, now);
        }
    }
}
