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

    private const int StatePlaneCount = 2;
    private const int StateBatchCount = 1;
    private const int StateSize = 128;
    private readonly InferenceSession _session;
    private readonly float[] _state = new float[StatePlaneCount * StateBatchCount * StateSize];
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
    }

    public float Predict(float[] samples)
    {
        if (samples.Length != FrameSampleCount)
        {
            throw new ArgumentException($"Silero VAD 需要 {FrameSampleCount} 个采样点", nameof(samples));
        }

        var inputTensor = new DenseTensor<float>(samples, [1, FrameSampleCount]);
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
