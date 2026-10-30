// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;

/// <summary>OcrRegionKind：原神自动配音的业务类型。</summary>
public enum OcrRegionKind { Speaker, Dialogue }
/// <summary>OcrRejectionReason：原神自动配音的业务类型。</summary>
public enum OcrRejectionReason { None, Empty, LowConfidence }
/// <summary>OcrErrorCode：原神自动配音的业务类型。</summary>
public enum OcrErrorCode { Configuration, Image, Template, ModelMissing, ModelInitialization, Inference, NoDialogue, Cancelled }
/// <summary>OcrException：原神自动配音的业务类型。</summary>
public sealed class OcrException(OcrErrorCode code, string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>Code：当前业务对象的配置、身份或状态值。</summary>
    public OcrErrorCode Code { get; } = code;
}
/// <summary>PixelRect：原神自动配音的业务类型。</summary>
public sealed record PixelRect(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Width：当前业务对象的配置、身份或状态值。</summary>
    public int Width => Right - Left;
    /// <summary>Height：当前业务对象的配置、身份或状态值。</summary>
    public int Height => Bottom - Top;
}
/// <summary>同一帧编码数据的身份与尺寸，供场景检查和 OCR 结果互相核对。</summary>
public sealed record ImageSourceSnapshot(string Sha256, int Width, int Height);
/// <summary>LayoutRegion：原神自动配音的业务类型。</summary>
public sealed record LayoutRegion(string Label, OcrRegionKind Kind, int Order, PixelRect Bounds);
/// <summary>SceneProfile：原神自动配音的业务类型。</summary>
public sealed record SceneProfile(int Width, int Height, IReadOnlyList<LayoutRegion> Regions);
/// <summary>OcrText：原神自动配音的业务类型。</summary>
public sealed record OcrText(string Text, double Confidence);
/// <summary>OcrRegionResult：原神自动配音的业务类型。</summary>
public sealed record OcrRegionResult(LayoutRegion Region, PixelRect ImageBounds, string RawText,
    string AcceptedText, double Confidence, OcrRejectionReason RejectionReason);
/// <summary>实际使用的 OCR 模型与推理设备，仅用于诊断显示。</summary>
public sealed record OcrModelInfo(string Model, string Device);
/// <summary>ImageOcrResult：原神自动配音的业务类型。</summary>
public sealed record ImageOcrResult(int ImageWidth, int ImageHeight, SceneProfile Layout,
    IReadOnlyList<OcrRegionResult> Regions, string? Speaker, string Dialogue, OcrModelInfo Model,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt);

/// <summary>逐行识别已裁剪图像的 OCR 边界；生产实现为 BetterGI 内置 OCR，测试可替换。</summary>
public interface IOcrEngine
{
    /// <summary>按输入顺序识别每一行图像；调用方保留图像所有权。</summary>
    Task<(IReadOnlyList<OcrText> Texts, OcrModelInfo Model)> RecognizeAsync(
        IReadOnlyList<Mat> lines, CancellationToken cancellationToken);
}
