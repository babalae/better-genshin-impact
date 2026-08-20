using BetterGenshinImpact.Core.Config;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// Debug 专用分阶段计时。调用点在 Release 下由 <see cref="ConditionalAttribute"/> 整段抹掉。
/// 明细写入 <c>log/debug-timing/{category}-yyyyMMdd.csv</c>，并打 Debug 日志。
/// </summary>
public static class DebugTiming
{
    /// <param name="category">业务分类，如 tp / pathing，用于 CSV 文件名与列。</param>
    /// <param name="subject">本次运行的对象描述，如传送目标名称。</param>
    [Conditional("DEBUG")]
    public static void Begin(string category, string subject)
    {
#if DEBUG
        if (!IsEnabled())
        {
            return;
        }

        Session.Value?.Dispose();
        Session.Value = new TimingSession(Sanitize(category), Sanitize(subject));
        Write("begin", detail: string.Empty, status: "running");
#endif
    }

    [Conditional("DEBUG")]
    public static void Mark(string stage, string? detail = null)
    {
#if DEBUG
        if (!IsEnabled())
        {
            return;
        }

        Write(stage, detail ?? string.Empty, status: "running");
#endif
    }

    /// <summary>记录错误/失败分支（CSV Status=error），不结束当前 session。</summary>
    [Conditional("DEBUG")]
    public static void Fail(string stage, string? detail = null)
    {
#if DEBUG
        if (!IsEnabled())
        {
            return;
        }

        Write(stage, detail ?? string.Empty, status: "error");
#endif
    }

    [Conditional("DEBUG")]
    public static void End(string status = "ok")
    {
#if DEBUG
        if (!IsEnabled())
        {
            Session.Value?.Dispose();
            Session.Value = null;
            return;
        }

        Write("end", detail: string.Empty, status: status);
        Session.Value?.Dispose();
        Session.Value = null;
#endif
    }

#if DEBUG
    private static bool IsEnabled()
    {
        try
        {
            return BetterGenshinImpact.Service.ConfigService.Config?.OtherConfig.EnableDebugTiming == true;
        }
        catch
        {
            return false;
        }
    }
    private static readonly AsyncLocal<TimingSession?> Session = new();
    private static readonly object FileLock = new();
    private static readonly Dictionary<string, string> CsvPathByCategory = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> HeaderWrittenCategories = new(StringComparer.OrdinalIgnoreCase);

    private static void Write(string stage, string detail, string status)
    {
        var session = Session.Value;
        if (session == null)
        {
            // 重试间隙 / 硬超时等：session 已 End，仍落一条独立行，避免错误分支丢失。
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            var line = string.Join(',',
                Csv(timestamp),
                Csv("tp"),
                Csv("-"),
                Csv("(no-session)"),
                Csv(stage),
                "0",
                "0",
                Csv(detail),
                Csv(status));
            AppendCsv("tp", line);
            Log.Debug(
                "DebugTiming (no-session) stage={Stage} detail={Detail} status={Status}",
                stage,
                detail,
                status);
            return;
        }

        var elapsedMs = session.Stopwatch.ElapsedMilliseconds;
        var deltaMs = elapsedMs - session.LastElapsedMs;
        session.LastElapsedMs = elapsedMs;

        var ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var sessionLine = string.Join(',',
            Csv(ts),
            Csv(session.Category),
            Csv(session.RunId),
            Csv(session.Subject),
            Csv(stage),
            elapsedMs.ToString(CultureInfo.InvariantCulture),
            deltaMs.ToString(CultureInfo.InvariantCulture),
            Csv(detail),
            Csv(status));

        AppendCsv(session.Category, sessionLine);
        Log.Debug(
            "DebugTiming category={Category} run={RunId} stage={Stage} +{DeltaMs}ms total={ElapsedMs}ms detail={Detail} status={Status}",
            session.Category,
            session.RunId,
            stage,
            deltaMs,
            elapsedMs,
            detail,
            status);
    }

    private static void AppendCsv(string category, string line)
    {
        try
        {
            lock (FileLock)
            {
                EnsureCsvReady(category);
                File.AppendAllText(CsvPathByCategory[category], line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "DebugTiming 写入 CSV 失败 category={Category}", category);
        }
    }

    private static void EnsureCsvReady(string category)
    {
        if (CsvPathByCategory.ContainsKey(category) && HeaderWrittenCategories.Contains(category))
        {
            return;
        }

        var safeCategory = SanitizeFileName(category);
        var dir = Global.Absolute(@"log\debug-timing");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{safeCategory}-{DateTime.Now:yyyyMMdd}.csv");
        CsvPathByCategory[category] = path;

        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            File.WriteAllText(
                path,
                "Timestamp,Category,RunId,Subject,Stage,ElapsedMs,DeltaMs,Detail,Status" + Environment.NewLine,
                Encoding.UTF8);
        }

        HeaderWrittenCategories.Add(category);
    }

    private static string Csv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        return $"\"{escaped}\"";
    }

    private static string Sanitize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(unknown)" : value.Trim();
    }

    private static string SanitizeFileName(string value)
    {
        var sanitized = Sanitize(value);
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(c, '_');
        }

        return sanitized;
    }

    private sealed class TimingSession : IDisposable
    {
        public TimingSession(string category, string subject)
        {
            Category = category;
            Subject = subject;
            RunId = Guid.NewGuid().ToString("N")[..8];
            Stopwatch = Stopwatch.StartNew();
        }

        public string Category { get; }
        public string Subject { get; }
        public string RunId { get; }
        public Stopwatch Stopwatch { get; }
        public long LastElapsedMs { get; set; }

        public void Dispose()
        {
            Stopwatch.Stop();
        }
    }
#endif
}
