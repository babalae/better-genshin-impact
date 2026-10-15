using System;
using BetterGenshinImpact.Core.Recognition.ONNX;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterGenshinImpact.Core.Config;

/// <summary>OCR 纯 CPU 推理的独立线程设置，默认跟随全局 CPU 线程数。</summary>
public partial class PaddleOcrCpuConfig : ObservableObject
{
    [ObservableProperty] private PaddleOcrCpuMode _mode;
    [ObservableProperty] private int _detectionThreads = Math.Min(2, Environment.ProcessorCount);
    [ObservableProperty] private int _recognitionThreads = Math.Min(2, Environment.ProcessorCount);
    [ObservableProperty] private int _interOpThreads = 1;
    [ObservableProperty] private bool _parallelExecution;
    [ObservableProperty] private bool _allowSpinning = true;

    public PaddleOcrCpuOptions ToOptions() => new()
    {
        Mode = Mode, DetectionThreads = DetectionThreads, RecognitionThreads = RecognitionThreads,
        InterOpThreads = InterOpThreads, ParallelExecution = ParallelExecution, AllowSpinning = AllowSpinning
    };

    public static PaddleOcrCpuConfig FromOptions(PaddleOcrCpuOptions options) => new()
    {
        Mode = options.Mode, DetectionThreads = options.DetectionThreads, RecognitionThreads = options.RecognitionThreads,
        InterOpThreads = options.InterOpThreads, ParallelExecution = options.ParallelExecution, AllowSpinning = options.AllowSpinning
    };
}
