using System;
using System.Collections.Generic;

namespace BetterGenshinImpact.GameTask.AutoSkip.Audio;

internal sealed class DialogueOptionVoiceDetector : IDisposable
{
    private readonly ProcessLoopbackAudioCapture _capture;
    private readonly SileroVadDetector _vad;
    private readonly List<float> _pendingSamples = [];
    private int _pendingSampleOffset;

    private DialogueOptionVoiceDetector(int targetProcessId, ProcessLoopbackAudioCapture capture, SileroVadDetector vad)
    {
        TargetProcessId = targetProcessId;
        _capture = capture;
        _vad = vad;
    }

    public int TargetProcessId { get; }

    public static DialogueOptionVoiceDetector Create(int targetProcessId)
    {
        ProcessLoopbackAudioCapture? capture = null;
        SileroVadDetector? vad = null;
        try
        {
            vad = new SileroVadDetector();
            capture = new ProcessLoopbackAudioCapture(targetProcessId);
            return new DialogueOptionVoiceDetector(targetProcessId, capture, vad);
        }
        catch
        {
            capture?.Dispose();
            vad?.Dispose();
            throw;
        }
    }

    public void Reset()
    {
        _pendingSamples.Clear();
        _pendingSampleOffset = 0;
        _vad.Reset();
        _capture.DiscardAvailableSamples();
    }

    public VoiceDetectionResult? Update(List<float>? processedSamples = null)
    {
        _capture.ReadAvailableSamples(_pendingSamples);

        float? probabilityMax = null;
        double squareSum = 0d;
        var peak = 0f;
        var processedSampleCount = 0;
        while (_pendingSamples.Count - _pendingSampleOffset >= SileroVadDetector.FrameSampleCount)
        {
            var frame = new float[SileroVadDetector.FrameSampleCount];
            _pendingSamples.CopyTo(_pendingSampleOffset, frame, 0, frame.Length);
            _pendingSampleOffset += frame.Length;
            probabilityMax = Math.Max(probabilityMax ?? 0f, _vad.Predict(frame));
            processedSamples?.AddRange(frame);

            foreach (var sample in frame)
            {
                squareSum += sample * sample;
                peak = Math.Max(peak, Math.Abs(sample));
            }

            processedSampleCount += frame.Length;
        }

        CompactPendingSamples();
        if (probabilityMax == null)
        {
            return null;
        }

        var rms = (float)Math.Sqrt(squareSum / processedSampleCount);
        return new VoiceDetectionResult(probabilityMax.Value, rms, peak);
    }

    public readonly record struct VoiceDetectionResult(float Probability, float Rms, float Peak);

    private void CompactPendingSamples()
    {
        if (_pendingSampleOffset == 0)
        {
            return;
        }

        if (_pendingSampleOffset >= _pendingSamples.Count)
        {
            _pendingSamples.Clear();
        }
        else
        {
            _pendingSamples.RemoveRange(0, _pendingSampleOffset);
        }

        _pendingSampleOffset = 0;
    }

    public void Dispose()
    {
        _capture.Dispose();
        _vad.Dispose();
    }
}
