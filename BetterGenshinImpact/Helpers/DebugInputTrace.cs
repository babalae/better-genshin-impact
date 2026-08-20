using BetterGenshinImpact.Core.Config;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Vanara.PInvoke;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// Debug 专用模拟键鼠记录。Release 下调用被 <see cref="ConditionalAttribute"/> 抹掉。
/// 写入 <c>log/debug-timing/input-yyyyMMdd.csv</c>。
/// 写入走后台队列，绝不阻塞 SendInput / 拖地图热路径。
/// </summary>
public static class DebugInputTrace
{
    [Conditional("DEBUG")]
    public static void Record(string channel, string action, string? detail = null)
    {
#if DEBUG
        if (!IsEnabled())
        {
            return;
        }

        Enqueue(channel, action, detail ?? string.Empty);
#endif
    }

    /// <summary>供 Fischless.WindowsInput 等底层库回调（无 channel 时默认 SendInput）。</summary>
    [Conditional("DEBUG")]
    public static void RecordSendInput(string action, string? detail = null)
    {
#if DEBUG
        if (!IsEnabled())
        {
            return;
        }

        Enqueue("SendInput", action, detail ?? string.Empty);
#endif
    }

#if DEBUG
    private static bool IsEnabled()
    {
        try
        {
            return BetterGenshinImpact.Service.ConfigService.Config?.OtherConfig.EnableDebugInputTrace == true;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct TraceItem(string Timestamp, string Channel, string Action, string Detail, int CursorX, int CursorY);

    private static readonly ConcurrentQueue<TraceItem> Queue = new();
    private static int s_writerStarted;
    private static string? s_csvPath;
    private static bool s_headerWritten;

    private static void Enqueue(string channel, string action, string detail)
    {
        try
        {
            User32.GetCursorPos(out var cursor);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            Queue.Enqueue(new TraceItem(timestamp, channel, action, detail, cursor.X, cursor.Y));
            EnsureWriterStarted();
        }
        catch
        {
            // 埋点失败绝不能影响键鼠
        }
    }

    private static void EnsureWriterStarted()
    {
        if (Interlocked.CompareExchange(ref s_writerStarted, 1, 0) != 0)
        {
            return;
        }

        var thread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "DebugInputTraceWriter",
            Priority = ThreadPriority.BelowNormal
        };
        thread.Start();
    }

    private static void WriterLoop()
    {
        var sb = new StringBuilder(4096);
        while (true)
        {
            try
            {
                var wrote = false;
                sb.Clear();
                while (Queue.TryDequeue(out var item))
                {
                    sb.Append(Csv(item.Timestamp)).Append(',')
                        .Append(Csv(item.Channel)).Append(',')
                        .Append(Csv(item.Action)).Append(',')
                        .Append(Csv(item.Detail)).Append(',')
                        .Append(item.CursorX.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append(item.CursorY.ToString(CultureInfo.InvariantCulture))
                        .AppendLine();
                    wrote = true;

                    if (sb.Length >= 32 * 1024)
                    {
                        Flush(sb);
                        sb.Clear();
                    }
                }

                if (wrote && sb.Length > 0)
                {
                    Flush(sb);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "DebugInputTrace 后台写入失败");
            }

            Thread.Sleep(50);
        }
    }

    private static void Flush(StringBuilder sb)
    {
        var dir = Global.Absolute(@"log\debug-timing");
        Directory.CreateDirectory(dir);
        s_csvPath ??= Path.Combine(dir, $"input-{DateTime.Now:yyyyMMdd}.csv");
        if (!s_headerWritten)
        {
            if (!File.Exists(s_csvPath) || new FileInfo(s_csvPath).Length == 0)
            {
                File.WriteAllText(
                    s_csvPath,
                    "Timestamp,Channel,Action,Detail,CursorX,CursorY" + Environment.NewLine,
                    Encoding.UTF8);
            }

            s_headerWritten = true;
        }

        File.AppendAllText(s_csvPath, sb.ToString(), Encoding.UTF8);
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
#endif
}
