using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Globalization;
using System.Threading;
using System.Windows;

namespace BetterGenshinImpact.GameTask.AutoSkip.Audio;

public partial class DialogueOptionVoiceDiagnosticState : ObservableObject
{
    private int _recordingRequested;

    [ObservableProperty]
    private double _probability;

    [ObservableProperty]
    private string _probabilityText = "--";

    [ObservableProperty]
    private string _rmsText = "--";

    [ObservableProperty]
    private string _peakText = "--";

    [ObservableProperty]
    private string _statusText = "诊断未开启";

    [ObservableProperty]
    private string _verdictText = "—";

    [ObservableProperty]
    private string _decisionText = "未在等待选项";

    public string ThresholdText { get; } =
        $"起播 {DialogueOptionVoiceThresholds.SpeechProbability:F2}  ·  疑似 {DialogueOptionVoiceThresholds.MaybeSpeechProbability:F2}  ·  " +
        $"起播确认 {DialogueOptionVoiceThresholds.SpeechRiseMilliseconds}ms  ·  结束静音 {DialogueOptionVoiceThresholds.SilenceMilliseconds / 1000d:F1}s";

    [ObservableProperty]
    private string _recordingButtonText = "开始录制";

    [ObservableProperty]
    private string _recordingStatusText = "录音将保存到 log\\VadDiagnostics";

    internal bool IsRecordingRequested => Volatile.Read(ref _recordingRequested) != 0;

    public void ToggleRecording()
    {
        var requested = IsRecordingRequested;
        Interlocked.Exchange(ref _recordingRequested, requested ? 0 : 1);
        RecordingButtonText = requested ? "开始录制" : "停止并保存";
        RecordingStatusText = requested ? "正在停止录制…" : "等待音频帧后开始录制…";
    }

    internal void SetRecordingStarted(string filePath)
    {
        Dispatch(() =>
        {
            RecordingButtonText = "停止并保存";
            RecordingStatusText = $"正在录制：{filePath}";
        });
    }

    internal void SetRecordingStopped(string? filePath)
    {
        Interlocked.Exchange(ref _recordingRequested, 0);
        Dispatch(() =>
        {
            RecordingButtonText = "开始录制";
            RecordingStatusText = filePath == null
                ? "录音将保存到 log\\VadDiagnostics"
                : $"已保存：{filePath}";
        });
    }

    internal void SetDisabled()
    {
        Dispatch(() =>
        {
            Probability = 0;
            ProbabilityText = "--";
            RmsText = "--";
            PeakText = "--";
            StatusText = "诊断未开启";
            VerdictText = "—";
            DecisionText = "未在等待选项";
        });
    }

    internal void SetWaiting(string statusText, string decisionText)
    {
        Dispatch(() =>
        {
            Probability = 0;
            ProbabilityText = "--";
            RmsText = "--";
            PeakText = "--";
            StatusText = statusText;
            VerdictText = "—";
            DecisionText = decisionText;
        });
    }

    internal void SetSample(DialogueOptionVoiceDetector.VoiceDetectionResult result, string verdictText, string decisionText)
    {
        Dispatch(() =>
        {
            Probability = Math.Clamp(result.Probability, 0f, 1f);
            ProbabilityText = result.Probability.ToString("F6", CultureInfo.InvariantCulture);
            RmsText = result.Rms.ToString("F5", CultureInfo.InvariantCulture);
            PeakText = result.Peak.ToString("F5", CultureInfo.InvariantCulture);
            StatusText = "持续检测中";
            VerdictText = verdictText;
            DecisionText = decisionText;
        });
    }

    private static void Dispatch(Action update)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            update();
            return;
        }

        dispatcher.BeginInvoke(update);
    }
}
