using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading;

namespace BetterGenshinImpact.GameTask.AutoSkip.Audio;

internal sealed class DialogueOptionAudioWaiter
{
    internal const int SpeechStartGraceMilliseconds = 5000;
    internal const int NoSpeechQuietDurationMilliseconds = 1200;
    private const int DetectorRetryDelayMilliseconds = 5000;

    private readonly object _detectorLock = new();
    private DialogueOptionVoiceDetector? _detector;
    private int? _unavailableProcessId;
    private DateTime _detectorRetryAfter = DateTime.MinValue;
    private WaitState? _waitState;

    public bool IsWaiting
    {
        get
        {
            using var _ = EnterDetectorLock();
            return _waitState != null;
        }
    }

    /// <summary>
    /// 本次选项等待会话的真实进度，用于诊断面板展示与实际点击一致的判定。
    /// </summary>
    internal readonly record struct WaitProgress(
        bool IsFallback,
        bool HeardSpeech,
        bool InStartGrace,
        long WaitingMilliseconds,
        long QuietMilliseconds,
        int RequiredQuietMilliseconds);

    internal bool TryGetProgress(out WaitProgress progress)
    {
        using var _ = EnterDetectorLock();
        if (_waitState is not { } waitState)
        {
            progress = default;
            return false;
        }

        var elapsedMilliseconds = waitState.Stopwatch.ElapsedMilliseconds;
        if (waitState.IsFallback)
        {
            progress = new WaitProgress(true, false, false, elapsedMilliseconds, 0, 0);
            return true;
        }

        progress = new WaitProgress(
            false,
            waitState.HeardSpeech,
            !waitState.HeardSpeech && elapsedMilliseconds < SpeechStartGraceMilliseconds,
            elapsedMilliseconds,
            waitState.QuietSinceMilliseconds is { } quietSince ? elapsedMilliseconds - quietSince : 0,
            waitState.HeardSpeech ? DialogueOptionVoiceThresholds.SilenceMilliseconds : NoSpeechQuietDurationMilliseconds);
        return true;
    }

    public void Cancel()
    {
        using var _ = EnterDetectorLock();
        _waitState = null;
    }

    public void ReleaseDetector()
    {
        lock (_detectorLock)
        {
            if (_waitState?.Detector != null)
            {
                _waitState = null;
            }

            _detector?.Dispose();
            _detector = null;
            _unavailableProcessId = null;
            _detectorRetryAfter = DateTime.MinValue;
        }
    }

    public bool Start(int maxWaitMilliseconds, int fallbackDelayMilliseconds, ILogger logger)
    {
        using var _ = EnterDetectorLock();
        if (maxWaitMilliseconds <= 0)
        {
            return StartFallbackWait(fallbackDelayMilliseconds);
        }

        var detector = GetDetector(logger);
        if (detector == null)
        {
            return StartFallbackWait(fallbackDelayMilliseconds);
        }

        detector.Reset();
        _waitState = WaitState.ForAudio(detector, maxWaitMilliseconds, fallbackDelayMilliseconds);
        return true;
    }

    public bool Update(ILogger logger)
    {
        using var _ = EnterDetectorLock();
        if (_waitState == null)
        {
            return true;
        }

        if (_waitState.IsFallback)
        {
            if (_waitState.Stopwatch.ElapsedMilliseconds < _waitState.MaxWaitMilliseconds)
            {
                return false;
            }

            _waitState = null;
            return true;
        }

        try
        {
            var waitState = _waitState;
            var elapsedMilliseconds = waitState.Stopwatch.ElapsedMilliseconds;
            if (elapsedMilliseconds >= waitState.MaxWaitMilliseconds)
            {
                logger.LogDebug("自动剧情：Silero VAD 检测超时，最大语音概率 {ProbabilityMax:F2}", waitState.ProbabilityMax);
                _waitState = null;
                return true;
            }

            var probabilityResult = waitState.Detector!.Update();
            if (probabilityResult == null)
            {
                return false;
            }

            var probability = probabilityResult.Value.Probability;
            if (float.IsNaN(probability) || float.IsInfinity(probability))
            {
                probability = 0f;
            }

            waitState.ProbabilityMax = Math.Max(waitState.ProbabilityMax, probability);

            if (probability >= DialogueOptionVoiceThresholds.SpeechProbability)
            {
                waitState.VoiceLikeSinceMilliseconds ??= elapsedMilliseconds;
                if (elapsedMilliseconds - waitState.VoiceLikeSinceMilliseconds >= DialogueOptionVoiceThresholds.SpeechRiseMilliseconds)
                {
                    waitState.HeardSpeech = true;
                }

                waitState.QuietSinceMilliseconds = null;
                return false;
            }

            waitState.VoiceLikeSinceMilliseconds = null;

            if (probability > DialogueOptionVoiceThresholds.MaybeSpeechProbability)
            {
                waitState.QuietSinceMilliseconds = null;
                return false;
            }

            waitState.QuietSinceMilliseconds ??= elapsedMilliseconds;
            var requiredQuietMilliseconds = waitState.HeardSpeech ? DialogueOptionVoiceThresholds.SilenceMilliseconds : NoSpeechQuietDurationMilliseconds;
            if (!waitState.HeardSpeech && elapsedMilliseconds < SpeechStartGraceMilliseconds)
            {
                return false;
            }

            var quietMilliseconds = elapsedMilliseconds - waitState.QuietSinceMilliseconds.Value;
            if (quietMilliseconds < requiredQuietMilliseconds)
            {
                return false;
            }

            logger.LogDebug("自动剧情：Silero VAD 检测到{Text}，最大语音概率 {ProbabilityMax:F2}", waitState.HeardSpeech ? "语音结束" : "语音未起播", waitState.ProbabilityMax);
            _waitState = null;
            return true;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "自动剧情：Silero VAD 检测失败，回退到固定延迟");
            var fallbackDelayMilliseconds = _waitState?.FallbackDelayMilliseconds ?? 0;
            ReleaseDetector();
            _waitState = null;
            return !StartFallbackWait(fallbackDelayMilliseconds);
        }
    }

    private DialogueOptionVoiceDetector? GetDetector(ILogger logger)
    {
        lock (_detectorLock)
        {
            var targetProcessId = GetGameProcessId();
            if (targetProcessId is not > 0)
            {
                logger.LogWarning("自动剧情：未能获取游戏进程 PID，将使用固定延迟");
                return null;
            }

            var processId = targetProcessId.Value;
            if (_detector != null && _detector.TargetProcessId == processId)
            {
                return _detector;
            }

            _detector?.Dispose();
            _detector = null;

            var now = DateTime.Now;
            if (_unavailableProcessId == processId && now < _detectorRetryAfter)
            {
                return null;
            }

            try
            {
                _detector = DialogueOptionVoiceDetector.Create(processId);
                _unavailableProcessId = null;
                _detectorRetryAfter = DateTime.MinValue;
                logger.LogDebug("自动剧情：Silero VAD 采样来源 游戏进程音频 PID={ProcessId}", processId);
                return _detector;
            }
            catch (Exception e)
            {
                _unavailableProcessId = processId;
                _detectorRetryAfter = now.AddMilliseconds(DetectorRetryDelayMilliseconds);
                logger.LogWarning(e, "自动剧情：初始化 Silero VAD 失败，将使用固定延迟，稍后重试");
                return null;
            }
        }
    }

    private static int? GetGameProcessId()
    {
        using var process = SystemControl.GetProcessByHandle(TaskContext.Instance().GameHandle);
        return process?.Id;
    }

    private bool StartFallbackWait(int milliseconds)
    {
        if (milliseconds <= 0)
        {
            return false;
        }

        _waitState = WaitState.ForFallback(milliseconds);
        return true;
    }

    private DetectorLockScope EnterDetectorLock()
    {
        Monitor.Enter(_detectorLock);
        return new DetectorLockScope(_detectorLock);
    }

    private readonly struct DetectorLockScope : IDisposable
    {
        private readonly object _lockObject;

        public DetectorLockScope(object lockObject)
        {
            _lockObject = lockObject;
        }

        public void Dispose()
        {
            Monitor.Exit(_lockObject);
        }
    }

    private sealed class WaitState
    {
        private WaitState(DialogueOptionVoiceDetector? detector, int maxWaitMilliseconds, int fallbackDelayMilliseconds, bool isFallback)
        {
            Detector = detector;
            MaxWaitMilliseconds = maxWaitMilliseconds;
            FallbackDelayMilliseconds = fallbackDelayMilliseconds;
            IsFallback = isFallback;
            Stopwatch = Stopwatch.StartNew();
        }

        public DialogueOptionVoiceDetector? Detector { get; }

        public int MaxWaitMilliseconds { get; }

        public int FallbackDelayMilliseconds { get; }

        public bool IsFallback { get; }

        public Stopwatch Stopwatch { get; }

        public bool HeardSpeech { get; set; }

        public float ProbabilityMax { get; set; }

        public long? VoiceLikeSinceMilliseconds { get; set; }

        public long? QuietSinceMilliseconds { get; set; }

        public static WaitState ForAudio(DialogueOptionVoiceDetector detector, int maxWaitMilliseconds, int fallbackDelayMilliseconds)
        {
            return new WaitState(detector, maxWaitMilliseconds, fallbackDelayMilliseconds, false);
        }

        public static WaitState ForFallback(int milliseconds)
        {
            return new WaitState(null, milliseconds, 0, true);
        }
    }
}
