using System;
using System.IO;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Job;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace BetterGenshinImpact.GameTask.AutoDomain.TrainingGuide;

public static class TrainingGuideDiagnostics
{
    public const string DirectoryPath = @"User\Diagnostics\AutoDomain.TrainingGuide";
    private static readonly System.Threading.AsyncLocal<Session?> Current = new();
    public static bool Enabled => Current.Value is { IsEnabled: true };
    public static string TroubleshootingHint => Enabled
        ? $"请查看 {DirectoryPath} 中的本次诊断；若保存失败，请开启诊断后重试。"
        : "请在任务设置中开启培养计划详细诊断后重试。";

    public static IDisposable Begin(bool enabled, ILogger logger)
    {
        var session = new Session(enabled, logger, Current.Value);
        Current.Value = session;
        return session;
    }

    private enum Category { Flow, OcrIssue, Reward, EntryVerification, IconLocation, IconMatch }

    private sealed class Session(bool enabled, ILogger logger, Session? previous) : IDisposable
    {
        private readonly object _gate = new();
        private StreamWriter? _writer;
        private FileLogger? _formatter;
        private string? _day;
        private bool _disposed;
        private bool _reported;
        private readonly string _id = Guid.NewGuid().ToString("N");
        public bool IsEnabled => enabled && !_disposed;

        public void Format(string template, object?[] args)
        {
            lock (_gate)
            {
                if (_disposed) return;
                try { (_formatter ??= new FileLogger(this)).LogInformation(template, args); }
                catch (Exception e) { Report(e); }
            }
        }

        public void Write(Category category, string message)
        {
            lock (_gate)
            {
                if (_disposed) return;
                var now = DateTime.Now;
                var day = now.ToString("yyyyMMdd");
                if (_writer == null || _day != day)
                {
                    var old = _writer;
                    _writer = null;
                    old?.Dispose();
                    var folder = Global.Absolute(DirectoryPath);
                    Directory.CreateDirectory(folder);
                    _writer = new StreamWriter(new FileStream(Path.Combine(folder, $"training-guide-{day}-{Environment.ProcessId}.log"),
                        FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
                    _day = day;
                }
                _writer.WriteLine($"{now:O} [{_id}] [{category}] {message}");
            }
        }

        public void Report(Exception e)
        {
            lock (_gate)
            {
                if (_reported) return;
                _reported = true;
                logger.LogWarning("培养诊断保存失败，后续同类提示静默：{Message}", e.Message);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                try { _writer?.Dispose(); }
                catch (Exception e) { Report(e); }
                finally { Current.Value = previous; }
            }
        }
    }

    // 使用标准日志模板格式化，但不经过全局日志管道，避免控制台和悬浮窗刷屏。
    public static void Detail(string template, params object?[] args)
    {
        if (Enabled) Current.Value!.Format(template, args);
    }

    private sealed class FileLogger(Session session) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => session.Write(Category.Flow, formatter(state, exception));
    }

    internal static void LogIconMatch((ItemIconCandidate Candidate, double Threshold)? match, string? recognizedName,
        string captureId, string level, int index)
    {
        if (!Enabled) return;
        try
        {
            string detail;
            if (match != null)
            {
                // 记录本次识别的原始候选，不再次执行模型推理。
                var (candidate, threshold) = match.Value;
                detail = string.IsNullOrEmpty(candidate.Name)
                    ? $"模型=ItemV2；无有效候选；阈值={threshold:F4}（余弦相似度 >= 阈值）"
                    : $"模型=ItemV2；最高候选={candidate.Name}；分数={candidate.Score:F6}；阈值={threshold:F4}；规则=余弦相似度 >= 阈值；原阈值通过={candidate.Score >= threshold}";
            }
            else
            {
                // 旧模型使用距离而非余弦分数，不套用 ItemV2 的阈值。
                detail = $"模型=Legacy；当前诊断仅支持 ItemV2，匹配分数及阈值未读取";
            }
            var message = $"[{captureId}] 入口={level}；位置={index}；{detail}；实际识别={recognizedName ?? "未识别"}";
            AppendText(Category.IconMatch, message);
        }
        catch (Exception e)
        {
            // 诊断失败不能改变正常识别结果，也不能阻断培养任务。
            Current.Value!.Report(e);
        }
    }

    public static void AppendOcrIssue(string captureId, string message)
        => AppendText(Category.OcrIssue, $"[{captureId}] {message}");

    public static void LogReward(string captureId, string message)
        => AppendText(Category.Reward, $"[{captureId}] {message}");

    public static void LogEntryVerification(string captureId, string message)
        => AppendText(Category.EntryVerification, $"[{captureId}] {message}");

    public static void LogIconLocation(string captureId, string message)
        => AppendText(Category.IconLocation, $"[{captureId}] {message}");

    private static void AppendText(Category category, string message)
    {
        if (!Enabled) return;
        try { Current.Value!.Write(category, message); }
        catch (Exception e) { Current.Value!.Report(e); }
    }

    public static void Save(Mat image, string stage, string? captureId = null)
    {
        if (!Enabled) return;
        try
        {
            var folder = Global.Absolute(DirectoryPath);
            Directory.CreateDirectory(folder);
            // 同一轮弹窗 OCR 共用标识，避免每张裁图重复追加时间戳和 GUID。
            captureId ??= $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}";
            var path = Path.Combine(folder, $"{captureId}-{stage}.png");
            if (!Cv2.ImWrite(path, image)) Current.Value!.Report(new IOException($"截图保存失败：{path}"));
            else Detail("培养诊断截图：{Path}", path);
        }
        catch (Exception e) { Current.Value!.Report(e); }
    }
}
