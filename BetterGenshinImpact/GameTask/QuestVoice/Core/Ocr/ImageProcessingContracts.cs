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

/// <summary>ImageProcessingOutcome：原神自动配音的业务类型。</summary>
public enum ImageProcessingOutcome { Recognized, Spoken, Rejected, Unknown, NoSpeakableText, Failed, Cancelled }
/// <summary>ImageSourceSnapshot：原神自动配音的业务类型。</summary>
public sealed record ImageSourceSnapshot(string Sha256, int Width, int Height);
/// <summary>ImageSceneResult：原神自动配音的业务类型。</summary>
public sealed record ImageSceneResult(ImageSourceSnapshot Source, SceneDecision Decision);
/// <summary>ImageProcessingRequest：原神自动配音的业务类型。</summary>
public sealed record ImageProcessingRequest(string ImagePath, SceneSelectionMode Mode, string? TemplatePath,
    OcrSettings? Ocr, SceneSettings? Scene, string ProfileDirectory,
    string? OcrConfigurationError = null, string? SceneConfigurationError = null);
/// <summary>ImageProcessingResult：原神自动配音的业务类型。</summary>
public sealed record ImageProcessingResult(SceneSelectionMode Mode, ImageProcessingOutcome Outcome,
    ImageSourceSnapshot? Source, SceneDecision? Scene, ImageOcrResult? Ocr,
    OcrErrorCode? Error = null, string? Detail = null);

/// <summary>IImageProcessingService：原神自动配音的业务类型。</summary>
public interface IImageProcessingService
{
    /// <summary>ProcessAsync：执行ImageProcessingContracts中的对应校验或计算步骤。</summary>
    Task<ImageProcessingResult> ProcessAsync(ImageProcessingRequest request,
        Action<ImageSceneResult>? sceneSelected, CancellationToken cancellationToken);
}
