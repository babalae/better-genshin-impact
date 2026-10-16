using System;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

public enum PaddleOcrCpuMode
{
    FollowGlobal = 0,
    RuntimeAuto = 1,
    GameplayPriority = 2,
    Custom = 3
}

public sealed record PaddleOcrCpuThreadOptions(int IntraOpThreads, int InterOpThreads, bool ParallelExecution, bool AllowSpinning);

/// <summary>冻结用户设置，检测和识别会话共享相同的参数来源，但分别计算算子内线程数。</summary>
public sealed record PaddleOcrCpuOptions
{
    public PaddleOcrCpuMode Mode { get; init; }
    public int DetectionThreads { get; init; } = Math.Min(2, Environment.ProcessorCount);
    public int RecognitionThreads { get; init; } = Math.Min(2, Environment.ProcessorCount);
    public int InterOpThreads { get; init; } = 1;
    public bool ParallelExecution { get; init; }
    public bool AllowSpinning { get; init; } = true;

    public bool IsValid => Enum.IsDefined(Mode)
        && DetectionThreads >= 0 && DetectionThreads <= Environment.ProcessorCount
        && RecognitionThreads >= 0 && RecognitionThreads <= Environment.ProcessorCount
        && InterOpThreads >= 0 && InterOpThreads <= Environment.ProcessorCount;

    public PaddleOcrCpuOptions Normalize() => this with
    {
        Mode = Enum.IsDefined(Mode) ? Mode : PaddleOcrCpuMode.FollowGlobal,
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
            PaddleOcrCpuMode.GameplayPriority => detection ? Math.Clamp(count / 2, Math.Min(2, count), count)
                : Math.Clamp(count / 4, Math.Min(2, count), Math.Min(4, count)),
            PaddleOcrCpuMode.Custom => Math.Clamp(detection ? DetectionThreads : RecognitionThreads, 0, count),
            _ => -1
        };
        if (intraOp < 0) return null;
        var parallel = Mode == PaddleOcrCpuMode.Custom && ParallelExecution;
        return new(intraOp, parallel ? Math.Clamp(InterOpThreads, 0, count) : 1, parallel, AllowSpinning);
    }

    public string Describe(int inheritedThreads)
    {
        static string Threads(int count) => count == 0 ? "自动（ORT 默认）" : count.ToString();
        if (Mode == PaddleOcrCpuMode.RuntimeAuto) return "OCR 使用 ONNX Runtime 自动调度";
        var det = Resolve(true);
        var rec = Resolve(false);
        if (det == null || rec == null) return $"OCR 跟随全局线程设置：{Threads(inheritedThreads)}";
        var mode = Mode == PaddleOcrCpuMode.GameplayPriority ? "游戏流畅优先" : "自定义设置";
        return $"OCR {mode} · 文本检测 {Threads(det.IntraOpThreads)} / 文字识别 {Threads(rec.IntraOpThreads)} · "
            + (det.ParallelExecution ? $"节点并行 {Threads(det.InterOpThreads)}" : "节点顺序执行")
            + $" · 自旋{(det.AllowSpinning ? "开启" : "关闭")}";
    }

    // 跟随全局设置时保留旧签名，避免新增默认参数使已有设备验证失效。
    internal string SignatureSuffix => Mode switch
    {
        PaddleOcrCpuMode.FollowGlobal => "",
        PaddleOcrCpuMode.RuntimeAuto => $"|PPOCR-CPU-v1|{(int)Mode}",
        PaddleOcrCpuMode.GameplayPriority => $"|PPOCR-CPU-v1|{(int)Mode}|{AllowSpinning}",
        _ => $"|PPOCR-CPU-v1|{(int)Mode}|{DetectionThreads}|{RecognitionThreads}|{InterOpThreads}|{ParallelExecution}|{AllowSpinning}"
    };
}
