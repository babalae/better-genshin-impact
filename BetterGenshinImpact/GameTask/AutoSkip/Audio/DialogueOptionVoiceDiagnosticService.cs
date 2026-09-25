using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.AutoSkip.Audio;

internal sealed class DialogueOptionVoiceDiagnosticService : BackgroundService
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(32);
    private static readonly TimeSpan DisabledPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan DetectorRetryDelay = TimeSpan.FromSeconds(5);

    private readonly AutoSkipConfig _config;
    private readonly DialogueOptionVoiceDiagnosticState _state;
    private readonly ILogger<DialogueOptionVoiceDiagnosticService> _logger;
    private DialogueOptionVoiceDetector? _detector;
    private PcmWaveRecorder? _recorder;
    private int? _unavailableProcessId;
    private DateTime _detectorRetryAfter = DateTime.MinValue;

    public DialogueOptionVoiceDiagnosticService(
        IConfigService configService,
        DialogueOptionVoiceDiagnosticState state,
        ILogger<DialogueOptionVoiceDiagnosticService> logger)
    {
        _config = configService.Get().AutoSkipConfig;
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!_config.DialogueOptionVoiceVadDiagnosticEnabled)
                {
                    StopRecording();
                    ReleaseDetector();
                    _state.SetDisabled();
                    await Task.Delay(DisabledPollInterval, stoppingToken);
                    continue;
                }

                try
                {
                    if (!_state.IsRecordingRequested)
                    {
                        StopRecording();
                    }

                    var detector = GetDetector();
                    if (detector == null)
                    {
                        await Task.Delay(DisabledPollInterval, stoppingToken);
                        continue;
                    }

                    UpdateRecordingState();
                    var recordedSamples = _recorder == null ? null : new List<float>();
                    var result = detector.Update(recordedSamples);
                    if (recordedSamples is { Count: > 0 })
                    {
                        _recorder?.Write(recordedSamples);
                    }

                    var decisionText = BuildDecisionText();
                    if (result == null)
                    {
                        _state.SetWaiting("等待完整音频帧", decisionText);
                    }
                    else
                    {
                        var probability = result.Value.Probability;
                        var verdict = probability >= DialogueOptionVoiceThresholds.SpeechProbability
                            ? "说话"
                            : probability > DialogueOptionVoiceThresholds.MaybeSpeechProbability
                                ? "疑似语音"
                                : "静音";
                        _state.SetSample(result.Value, verdict, decisionText);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "自动剧情：Silero VAD 持续诊断失败，稍后重试");
                    var processId = _detector?.TargetProcessId;
                    StopRecording();
                    ReleaseDetector();
                    _unavailableProcessId = processId;
                    _detectorRetryAfter = DateTime.Now.Add(DetectorRetryDelay);
                    _state.SetWaiting("检测失败，稍后重试", BuildDecisionText());
                }

                await Task.Delay(UpdateInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            StopRecording();
            ReleaseDetector();
            _state.SetDisabled();
        }
    }

    private DialogueOptionVoiceDetector? GetDetector()
    {
        using var process = FindGameProcess();
        if (process == null || process.Id <= 0)
        {
            ReleaseDetector();
            _state.SetWaiting("未找到游戏进程", BuildDecisionText());
            return null;
        }

        var processId = process.Id;
        if (_detector?.TargetProcessId == processId)
        {
            return _detector;
        }

        ReleaseDetector();
        if (_unavailableProcessId == processId && DateTime.Now < _detectorRetryAfter)
        {
            _state.SetWaiting("检测器初始化失败，等待重试", BuildDecisionText());
            return null;
        }

        try
        {
            _detector = DialogueOptionVoiceDetector.Create(processId);
            _unavailableProcessId = null;
            _detectorRetryAfter = DateTime.MinValue;
            _state.SetWaiting("等待完整音频帧", BuildDecisionText());
            _logger.LogDebug("自动剧情：Silero VAD 持续诊断采样来源 游戏进程音频 PID={ProcessId}", processId);
            return _detector;
        }
        catch (Exception e)
        {
            _unavailableProcessId = processId;
            _detectorRetryAfter = DateTime.Now.Add(DetectorRetryDelay);
            _state.SetWaiting("检测器初始化失败，等待重试", BuildDecisionText());
            _logger.LogWarning(e, "自动剧情：初始化 Silero VAD 持续诊断失败，稍后重试");
            return null;
        }
    }

    private void UpdateRecordingState()
    {
        if (_state.IsRecordingRequested)
        {
            if (_recorder != null)
            {
                return;
            }

            var directory = Global.Absolute(Path.Combine("log", "VadDiagnostics"));
            var filePath = Path.Combine(directory, $"silero-vad-{DateTime.Now:yyyyMMdd-HHmmss-fff}.wav");
            _recorder = new PcmWaveRecorder(filePath);
            _state.SetRecordingStarted(filePath);
            return;
        }

        StopRecording();
    }

    private void StopRecording()
    {
        if (_recorder == null)
        {
            if (_state.IsRecordingRequested)
            {
                _state.SetRecordingStopped(null);
            }

            return;
        }

        var filePath = _recorder.FilePath;
        _recorder.Dispose();
        _recorder = null;
        _state.SetRecordingStopped(filePath);
    }

    private static string BuildDecisionText()
    {
        var trigger = GameTaskManager.TriggerDictionary?.GetValueOrDefault("AutoSkip") as AutoSkipTrigger;
        if (trigger == null || !trigger.VoiceWaiter.TryGetProgress(out var progress))
        {
            return "未在等待选项";
        }

        if (progress.IsFallback)
        {
            return "等待选项 · 回退固定延迟";
        }

        if (progress.HeardSpeech)
        {
            return $"等待选项 · 已确认说话 · 静音 {progress.QuietMilliseconds / 1000d:F1}s / {progress.RequiredQuietMilliseconds / 1000d:F1}s";
        }

        return progress.InStartGrace
            ? $"等待选项 · 未起播 · 宽限 {progress.WaitingMilliseconds / 1000d:F1}s / {DialogueOptionAudioWaiter.SpeechStartGraceMilliseconds / 1000d:F1}s"
            : $"等待选项 · 未起播 · 静音 {progress.QuietMilliseconds / 1000d:F1}s / {progress.RequiredQuietMilliseconds / 1000d:F1}s";
    }

    private static Process? FindGameProcess()
    {
        var configuredHandle = TaskContext.Instance().GameHandle;
        if (configuredHandle != IntPtr.Zero)
        {
            var configuredProcess = SystemControl.GetProcessByHandle(configuredHandle);
            if (configuredProcess is { Id: > 0 })
            {
                return configuredProcess;
            }

            configuredProcess?.Dispose();
        }

        var detectedHandle = SystemControl.FindGenshinImpactHandle();
        return detectedHandle == IntPtr.Zero
            ? null
            : SystemControl.GetProcessByHandle(detectedHandle);
    }

    private void ReleaseDetector()
    {
        _detector?.Dispose();
        _detector = null;
    }
}
