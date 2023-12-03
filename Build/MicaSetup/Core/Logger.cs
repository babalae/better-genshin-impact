using MicaSetup.Helper;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DebugOut = System.Diagnostics.Debug;

namespace MicaSetup.Core;

public static class Logger
{
    private static readonly string ApplicationLogPath = SpecialPathHelper.GetFolder();
    private static readonly TextWriterTraceListener TraceListener = null!;

    static Logger()
    {
        if (!Directory.Exists(ApplicationLogPath))
        {
            _ = Directory.CreateDirectory(ApplicationLogPath);
        }

        string logFilePath = Path.Combine(ApplicationLogPath, DateTime.Now.ToString(@"yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
        TraceListener = new TextWriterTraceListener(logFilePath);
#if LEGACY
        Trace.AutoFlush = true;
        Trace.Listeners.Clear();
        Trace.Listeners.Add(TraceListener);
#endif
    }

    [SuppressMessage("Style", "IDE0060:")]
    public static void Ignore(params object[] values)
    {
    }

    [Conditional("DEBUG")]
    public static void Debug(params object[] values)
    {
        Log("DEBUG", string.Join(" ", values));
    }

    public static void Info(params object[] values)
    {
        Log("INFO", string.Join(" ", values));
    }

    public static void Warn(params object[] values)
    {
        Log("ERROR", string.Join(" ", values));
    }

    public static void Error(params object[] values)
    {
        Log("ERROR", string.Join(" ", values));
    }

    public static void Fatal(params object[] values)
    {
        Log("FATAL", string.Join(" ", values));
    }

    public static void Exception(Exception e, string message = null!)
    {
        Log(
            (message ?? string.Empty) + Environment.NewLine +
            e?.Message + Environment.NewLine +
            "Inner exception: " + Environment.NewLine +
            e?.InnerException?.Message + Environment.NewLine +
            "Stack trace: " + Environment.NewLine +
            e?.StackTrace,
            "ERROR");
#if DEBUG
        Debugger.Break();
#endif
    }

    private static void Log(string type, string message)
    {
        StringBuilder sb = new();

        sb.Append(type + "|" + DateTime.Now.ToString(@"yyyy-MM-dd|HH:mm:ss.fff", CultureInfo.InvariantCulture))
          .Append("|" + GetCallerInfo())
          .Append("|" + message);

        DebugOut.WriteLine(sb.ToString());
        if (Option.Current.Logging)
        {
            TraceListener.WriteLine(sb.ToString());
            TraceListener.Flush();
        }
    }

    private static string GetCallerInfo()
    {
        StackTrace stackTrace = new();

        MethodBase methodName = stackTrace.GetFrame(3)?.GetMethod()!;
        string? className = methodName?.DeclaringType?.Name;
        return className + "|" + methodName?.Name;
    }
}
