using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Helpers;
using Newtonsoft.Json;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoTrackPath;

internal static class TpDiagnosticRecorder
{
    private static readonly object SyncRoot = new();
    private static SessionState? _session;
    private static PointState? _point;

    public static bool IsEnabled => RuntimeHelper.IsDebug && _session != null;

    public static string StartSession(string sourceVersion, int totalPoints)
    {
        if (!RuntimeHelper.IsDebug)
        {
            return string.Empty;
        }

        try
        {
            lock (SyncRoot)
            {
                var sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var root = Global.Absolute(Path.Combine("log", "tp-diagnostics", sessionId));
                Directory.CreateDirectory(root);
                _session = new SessionState(root, Stopwatch.StartNew());
                _point = null;
                File.WriteAllText(
                    Path.Combine(root, "session.json"),
                    JsonConvert.SerializeObject(new
                    {
                        sessionId,
                        sourceVersion,
                        totalPoints,
                        createdAt = DateTimeOffset.Now,
                    }, Formatting.Indented),
                    Encoding.UTF8);
                WriteEventLocked("session.start", new { sourceVersion, totalPoints });
                return root;
            }
        }
        catch
        {
            lock (SyncRoot)
            {
                _session = null;
                _point = null;
            }
            return string.Empty;
        }
    }

    public static void BeginPoint(int index, string pointId, string label, string mapName, double x, double y)
    {
        if (!IsEnabled)
        {
            return;
        }

        TryRecordLocked(() =>
        {
            _point = new PointState(index, pointId, label, mapName, x, y, Stopwatch.StartNew());
            WriteEventLocked("point.start", new { index, pointId, label, mapName, x, y });
        });
    }

    public static void CompletePoint(bool success, string? error)
    {
        if (!IsEnabled)
        {
            return;
        }

        TryRecordLocked(() =>
        {
            try
            {
                WriteEventLocked("point.complete", new
                {
                    success,
                    error,
                    elapsedMs = _point?.Stopwatch.ElapsedMilliseconds ?? 0,
                });
            }
            finally
            {
                _point = null;
            }
        });
    }

    public static void EndSession()
    {
        if (!IsEnabled)
        {
            return;
        }

        TryRecordLocked(() =>
        {
            try
            {
                WriteEventLocked("session.complete", new { elapsedMs = _session?.Stopwatch.ElapsedMilliseconds ?? 0 });
            }
            finally
            {
                _point = null;
                _session = null;
            }
        });
    }

    public static void Record(string stage, object? data = null)
    {
        if (!IsEnabled)
        {
            return;
        }

        TryRecordLocked(() =>
        {
            WriteEventLocked(stage, data);
        });
    }

    public static void RecordWithScreenshot(string stage, object? data, Mat screenshot)
    {
        if (!IsEnabled)
        {
            return;
        }

        TryRecordLocked(() =>
        {
            var screenshotPath = SaveScreenshotLocked(stage, screenshot);
            WriteEventLocked(stage, new { screenshot = screenshotPath, detail = data });
        });
    }

    private static void TryRecordLocked(Action action)
    {
        try
        {
            lock (SyncRoot)
            {
                if (_session != null)
                {
                    action();
                }
            }
        }
        catch
        {
            // Diagnostics must never change teleport behavior.
        }
    }

    private static string? SaveScreenshotLocked(string stage, Mat screenshot)
    {
        if (_session == null || screenshot.Empty())
        {
            return null;
        }

        var pointName = _point == null
            ? "session"
            : $"{_point.Index:D4}_{SanitizeFileName(_point.PointId)}";
        var sequence = _point?.NextScreenshotSequence() ?? 0;
        var relativePath = Path.Combine("screenshots", pointName, $"{sequence:D2}_{SanitizeFileName(stage)}.jpg");
        var absolutePath = Path.Combine(_session.RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        Cv2.ImWrite(absolutePath, screenshot, [new ImageEncodingParam(ImwriteFlags.JpegQuality, 88)]);
        return relativePath.Replace('\\', '/');
    }

    private static void WriteEventLocked(string stage, object? data)
    {
        if (_session == null)
        {
            return;
        }

        var line = JsonConvert.SerializeObject(new
        {
            timestamp = DateTimeOffset.Now,
            sessionElapsedMs = _session.Stopwatch.ElapsedMilliseconds,
            pointElapsedMs = _point?.Stopwatch.ElapsedMilliseconds,
            point = _point == null
                ? null
                : new
                {
                    _point.Index,
                    _point.PointId,
                    _point.Label,
                    _point.MapName,
                    _point.X,
                    _point.Y,
                },
            stage,
            data,
        });
        File.AppendAllText(Path.Combine(_session.RootPath, "events.jsonl"), line + Environment.NewLine, Encoding.UTF8);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0 || char.IsWhiteSpace(chars[i]))
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private sealed record SessionState(string RootPath, Stopwatch Stopwatch);

    private sealed record PointState(
        int Index,
        string PointId,
        string Label,
        string MapName,
        double X,
        double Y,
        Stopwatch Stopwatch)
    {
        private int _screenshotSequence;

        public int NextScreenshotSequence()
        {
            return ++_screenshotSequence;
        }
    }
}
