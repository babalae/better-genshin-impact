using Newtonsoft.Json;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System;
using System.IO;
using System.Threading;

namespace BetterGenshinImpact.GameTask.QuickSereniteaPot;

/// <summary>仅捕获手动测试调用链的日志，不混入其他线程或其他实例的任务。</summary>
internal sealed class SereniteaPotTestLogSink : ILogEventSink
{
    private static readonly AsyncLocal<SereniteaPotTestSession?> Active = new();
    internal static SereniteaPotTestSession? Current => Active.Value;

    internal static IDisposable Capture(SereniteaPotTestSession session)
    {
        var previous = Active.Value;
        Active.Value = session;
        return new Scope(previous);
    }

    public void Emit(LogEvent logEvent) => Active.Value?.Write(logEvent);

    private sealed class Scope(SereniteaPotTestSession? previous) : IDisposable
    {
        public void Dispose() => Active.Value = previous;
    }
}

internal sealed class SereniteaPotTestSession : IDisposable
{
    private readonly Logger _log;
    private readonly object _sync = new();
    private readonly object _metadata;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private bool _disposed;

    internal string RunId { get; } = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-p{Environment.ProcessId}-{Guid.NewGuid():N}";
    internal string DirectoryPath { get; }
    internal string? FailureStage { get; set; }

    internal SereniteaPotTestSession(string rootDirectory, object metadata)
    {
        _metadata = metadata;
        DirectoryPath = Path.Combine(rootDirectory, RunId);
        Directory.CreateDirectory(DirectoryPath);
        _log = new LoggerConfiguration().MinimumLevel.Debug()
            .Enrich.WithProperty("PotTestRunId", RunId)
            .WriteTo.File(Path.Combine(DirectoryPath, "test.log"),
                outputTemplate: "[{Timestamp:O}] [{Level:u3}] [{PotTestRunId}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        try
        {
            SaveResult("created", null, null);
        }
        catch
        {
            _log.Dispose();
            throw;
        }
    }

    internal void Write(LogEvent logEvent)
    {
        lock (_sync)
        {
            if (!_disposed) _log.Write(logEvent);
        }
    }

    internal void Complete(string status, string? detail = null) => SaveResult(status, detail, DateTimeOffset.Now);

    private void SaveResult(string status, string? detail, DateTimeOffset? finishedAt)
    {
        var result = new
        {
            RunId, Status = status, Detail = detail, FailureStage,
            StartedAt = _startedAt, FinishedAt = finishedAt, Metadata = _metadata,
            Screenshots = Directory.GetFiles(DirectoryPath, "*.png")
        };
        File.WriteAllText(Path.Combine(DirectoryPath, "result.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _log.Dispose();
        }
    }
}
