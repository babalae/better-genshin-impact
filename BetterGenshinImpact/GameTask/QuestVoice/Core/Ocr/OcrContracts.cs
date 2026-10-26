// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;

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
/// <summary>LayoutRegion：原神自动配音的业务类型。</summary>
public sealed record LayoutRegion(string Label, OcrRegionKind Kind, int Order, PixelRect Bounds);
/// <summary>SceneProfile：原神自动配音的业务类型。</summary>
public sealed record SceneProfile(int Width, int Height, IReadOnlyList<LayoutRegion> Regions);
/// <summary>OcrText：原神自动配音的业务类型。</summary>
public sealed record OcrText(string Text, double Confidence);
/// <summary>OcrRegionResult：原神自动配音的业务类型。</summary>
public sealed record OcrRegionResult(LayoutRegion Region, PixelRect ImageBounds, string RawText,
    string AcceptedText, double Confidence, OcrRejectionReason RejectionReason);
/// <summary>OcrModelInfo：原神自动配音的业务类型。</summary>
public sealed record OcrModelInfo(string ModelPath, string Sha256, string Provider, int DeviceId,
    string? ProfilePath, IReadOnlyDictionary<string,int>? KernelCounts = null);
/// <summary>ImageOcrResult：原神自动配音的业务类型。</summary>
public sealed record ImageOcrResult(int ImageWidth, int ImageHeight, SceneProfile Layout,
    IReadOnlyList<OcrRegionResult> Regions, string? Speaker, string Dialogue, OcrModelInfo Model,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt);
/// <summary>ImageOcrRequest：原神自动配音的业务类型。</summary>
public sealed record ImageOcrRequest(string ImagePath, string TemplatePath, OcrSettings Settings,
    string ProfileDirectory);

/// <summary>ISceneProfileLoader：原神自动配音的业务类型。</summary>
public interface ISceneProfileLoader
{
    /// <summary>异步读取并严格校验外置配置或布局。</summary>
    Task<SceneProfile> LoadAsync(string path, CancellationToken cancellationToken);
}
/// <summary>IImageOcrService：原神自动配音的业务类型。</summary>
public interface IImageOcrService
{
    /// <summary>在取消检查和配置约束下执行识别。</summary>
    Task<ImageOcrResult> RecognizeAsync(ImageOcrRequest request, CancellationToken cancellationToken);
}
/// <summary>IOcrEngine：原神自动配音的业务类型。</summary>
public interface IOcrEngine
{
    /// <summary>在取消检查和配置约束下执行识别。</summary>
    Task<(IReadOnlyList<OcrText> Texts, OcrModelInfo Model)> RecognizeAsync(
        IReadOnlyList<OcrTensor> tensors, OcrSettings settings, string profileDirectory, CancellationToken cancellationToken);
}
/// <summary>OcrTensor：原神自动配音的业务类型。</summary>
public sealed record OcrTensor(float[] Data, int Width);
