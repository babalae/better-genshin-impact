using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using System.IO;

namespace BetterGenshinImpact.Hutao;

internal sealed class HutaoNamedPipeLogEventSink : ILogEventSink
{
    private readonly MessageTemplateTextFormatter textFormatter = new("[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");

    private readonly MemoryStream buffer;
    private readonly TextWriter writer;
    private readonly TextReader reader;

    private HutaoNamedPipe? namedPipe;

    public HutaoNamedPipeLogEventSink()
    {
        buffer = new();
        writer = new StreamWriter(buffer);
        reader = new StreamReader(buffer);
    }

    private HutaoNamedPipe NamedPipe
    {
        get => namedPipe ??= App.GetService<HutaoNamedPipe>()!;
    }

    public void Emit(LogEvent logEvent)
    {
        textFormatter.Format(logEvent, writer);
        writer.Flush();
        buffer.Position = 0;
        NamedPipe.TryRedirectLog(reader.ReadToEnd());
        buffer.SetLength(0);
    }
}