using System;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public enum PaddleOcrCpuMode { Inherit, Auto, Ratio, Manual, RuntimeDefault }

public sealed record PaddleOcrCpuThreadOptions(int IntraOpThreads, int InterOpThreads, bool ParallelExecution, bool AllowSpinning);

/// <summary>冻结用户设置，检测和识别会话共享相同的参数来源，但分别计算算子内线程数。</summary>
public sealed record PaddleOcrCpuOptions
{
    public PaddleOcrCpuMode Mode { get; init; }
    public int DetectionThreadPercent { get; init; } = 50;
    public int RecognitionThreadPercent { get; init; } = 25;
    public int DetectionThreads { get; init; } = Math.Min(2, Environment.ProcessorCount);
    public int RecognitionThreads { get; init; } = Math.Min(2, Environment.ProcessorCount);
    public int InterOpThreads { get; init; } = 1;
    public bool ParallelExecution { get; init; }
    public bool AllowSpinning { get; init; } = true;

    public bool IsValid => Enum.IsDefined(Mode)
        && DetectionThreadPercent is >= 1 and <= 100 && RecognitionThreadPercent is >= 1 and <= 100
        && DetectionThreads >= 0 && DetectionThreads <= Environment.ProcessorCount
        && RecognitionThreads >= 0 && RecognitionThreads <= Environment.ProcessorCount
        && InterOpThreads >= 0 && InterOpThreads <= Environment.ProcessorCount;

    public PaddleOcrCpuOptions Normalize() => this with
    {
        Mode = Enum.IsDefined(Mode) ? Mode : PaddleOcrCpuMode.Inherit,
        DetectionThreadPercent = Math.Clamp(DetectionThreadPercent, 1, 100),
        RecognitionThreadPercent = Math.Clamp(RecognitionThreadPercent, 1, 100),
        DetectionThreads = Math.Clamp(DetectionThreads, 0, Environment.ProcessorCount),
        RecognitionThreads = Math.Clamp(RecognitionThreads, 0, Environment.ProcessorCount),
        InterOpThreads = Math.Clamp(InterOpThreads, 0, Environment.ProcessorCount)
    };

    public PaddleOcrCpuThreadOptions? Resolve(bool detection, int? processorCount = null)
    {
        var count = Math.Max(1, processorCount ?? Environment.ProcessorCount);
        var intraOp = Mode switch
        {
            // PR #3722：Det 用一半线程，Rec 用四分之一并限制在 2~4。
            // 小于两个可用逻辑处理器时，不能为了满足下限而超额创建线程。
            PaddleOcrCpuMode.Auto => detection ? Math.Clamp(count / 2, Math.Min(2, count), count)
                : Math.Clamp(count / 4, Math.Min(2, count), Math.Min(4, count)),
            // 向下取整，最低为 1；比例是线程数比例，并非 CPU 使用率上限。
            PaddleOcrCpuMode.Ratio => (int)Math.Clamp((long)count * Math.Clamp(detection ? DetectionThreadPercent : RecognitionThreadPercent, 1, 100) / 100, 1, count),
            PaddleOcrCpuMode.Manual => Math.Clamp(detection ? DetectionThreads : RecognitionThreads, 0, count),
            _ => -1
        };
        if (intraOp < 0) return null;
        var parallel = Mode == PaddleOcrCpuMode.Manual && ParallelExecution;
        return new(intraOp, parallel ? Math.Clamp(InterOpThreads, 0, count) : 1, parallel, AllowSpinning);
    }

    public string Describe(int inheritedThreads)
    {
        static string Threads(int count) => count == 0 ? "自动（ORT 默认）" : count.ToString();
        if (Mode == PaddleOcrCpuMode.RuntimeDefault) return "PPOCR 不配置线程参数（ONNX Runtime 默认）";
        var det = Resolve(true);
        var rec = Resolve(false);
        if (det == null || rec == null) return $"PPOCR 沿用通用线程：{Threads(inheritedThreads)}";
        var mode = Mode switch { PaddleOcrCpuMode.Auto => "PR 自动", PaddleOcrCpuMode.Ratio => "自定义比例", _ => "详细配置" };
        return $"PPOCR {mode} · Det {Threads(det.IntraOpThreads)} / Rec {Threads(rec.IntraOpThreads)} · "
            + (det.ParallelExecution ? $"并行，算子间 {Threads(det.InterOpThreads)}" : "顺序，算子间 1")
            + $" · 自旋{(det.AllowSpinning ? "开启" : "关闭")}";
    }

    // 沿用模式保留旧签名，避免新增默认参数使已有设备验证失效。
    internal string SignatureSuffix => Mode == PaddleOcrCpuMode.Inherit ? ""
        : $"|PPOCR-CPU-v1|{(int)Mode}|{DetectionThreadPercent}|{RecognitionThreadPercent}|{DetectionThreads}|{RecognitionThreads}|{InterOpThreads}|{ParallelExecution}|{AllowSpinning}";
}
