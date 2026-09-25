using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.IO;
using System.Linq;

namespace BetterGenshinImpact.GameTask.AutoSkip.Audio;

internal sealed class SileroVadDetector : IDisposable
{
    public const int SampleRate = 16000;
    public const int FrameSampleCount = 512;

    /// <summary>
    /// 模型要求每帧额外拼接上一帧末尾的上下文采样，16kHz 下为 64 点。
    /// 因此实际输入长度为 576，缺失上下文会显著破坏模型输出。
    /// </summary>
    private const int ContextSampleCount = 64;

    private const int StatePlaneCount = 2;
    private const int StateBatchCount = 1;
    private const int StateSize = 128;
    private readonly InferenceSession _session;
    private readonly float[] _state = new float[StatePlaneCount * StateBatchCount * StateSize];
    private readonly float[] _context = new float[ContextSampleCount];
    private readonly float[] _input = new float[ContextSampleCount + FrameSampleCount];
    private readonly long[] _sampleRate = [SampleRate];

    public SileroVadDetector()
    {
        var modelPath = BgiOnnxModel.SileroVad.ModalPath;
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("Silero VAD 模型文件不存在", modelPath);
        }

        _session = new InferenceSession(modelPath, CreateSessionOptions());
    }

    public void Reset()
    {
        Array.Clear(_state);
        Array.Clear(_context);
    }

    public float Predict(float[] samples)
    {
        if (samples.Length != FrameSampleCount)
        {
            throw new ArgumentException($"Silero VAD 需要 {FrameSampleCount} 个采样点", nameof(samples));
        }

        Array.Copy(_context, 0, _input, 0, ContextSampleCount);
        Array.Copy(samples, 0, _input, ContextSampleCount, FrameSampleCount);
        Array.Copy(_input, FrameSampleCount, _context, 0, ContextSampleCount);

        var inputTensor = new DenseTensor<float>(_input, [1, _input.Length]);
        var stateTensor = new DenseTensor<float>(_state, [StatePlaneCount, StateBatchCount, StateSize]);
        var sampleRateTensor = new DenseTensor<long>(_sampleRate, Array.Empty<int>());

        using var results = _session.Run(
        [
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("state", stateTensor),
            NamedOnnxValue.CreateFromTensor("sr", sampleRateTensor)
        ]);

        var probability = results.First(x => x.Name == "output").AsEnumerable<float>().FirstOrDefault();
        var nextState = results.First(x => x.Name == "stateN").AsEnumerable<float>();
        var index = 0;
        foreach (var value in nextState)
        {
            if (index >= _state.Length)
            {
                break;
            }

            _state[index++] = value;
        }

        return Math.Clamp(probability, 0f, 1f);
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    private static SessionOptions CreateSessionOptions()
    {
        return new SessionOptions
        {
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC,
            InterOpNumThreads = 1,
            IntraOpNumThreads = 1
        };
    }
}
