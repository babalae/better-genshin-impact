// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>ImageOcrService：原神自动配音的业务类型。</summary>
public sealed partial class ImageOcrService
{
    /// <summary>ProcessAsync：执行ImageOcrService.Processing中的对应校验或计算步骤。</summary>
    public async Task<ImageProcessingResult> ProcessAsync(ImageProcessingRequest request,
        Action<ImageSceneResult>? sceneSelected, CancellationToken cancellationToken)
    {
        ImageSnapshot? snapshot = null;
        SceneDecision? decision = null;
        SpeakerInkPolicy? speakerInk = null;
        var started = DateTimeOffset.UtcNow;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Enum.IsDefined(request.Mode)) throw new OcrException(OcrErrorCode.Configuration, "未知图片识别模式。");
            if (request.Mode == SceneSelectionMode.Explicit && request.Ocr is null)
                throw MissingOcr(request);
            // Explicit XML keeps the original template-before-image error precedence.
            SceneProfile? profile = request.Mode == SceneSelectionMode.Explicit
                ? await layouts.LoadAsync(request.TemplatePath ?? request.Ocr!.DefaultTemplatePath, cancellationToken).ConfigureAwait(false)
                : null;
            snapshot = await ImageSnapshot.ReadAsync(request.ImagePath, cancellationToken).ConfigureAwait(false);
            if (request.Mode == SceneSelectionMode.Automatic)
            {
                if (request.Scene is null || selector is null)
                    throw new OcrException(OcrErrorCode.Configuration, request.SceneConfigurationError ?? "未配置自动场景目录。");
                var selection = await selector.SelectAsync(snapshot.Bytes, request.Scene, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                decision = selection.Decision;
                profile = selection.Profile;
                speakerInk = selection.SpeakerInk;
                if (decision.Kind == SceneDecisionKind.Explicit)
                    throw new OcrException(OcrErrorCode.Configuration, "自动模式收到显式布局决定。");
            }
            else
            {
                decision = new(SceneDecisionKind.Explicit, null, "ExplicitTemplate", "", null, new Dictionary<string, double>());
            }
            cancellationToken.ThrowIfCancellationRequested();
            sceneSelected?.Invoke(new(snapshot.Source, decision));
            cancellationToken.ThrowIfCancellationRequested();
            if (decision.Kind is SceneDecisionKind.Rejected or SceneDecisionKind.Unknown)
                return Result(decision.Kind == SceneDecisionKind.Rejected ? ImageProcessingOutcome.Rejected : ImageProcessingOutcome.Unknown);
            if (profile is null) throw new OcrException(OcrErrorCode.Configuration, "所选场景缺少识别区域。");
            if (request.Ocr is null) throw MissingOcr(request);
            var ocr = await RecognizeSnapshotAsync(snapshot, profile, request.Ocr, request.ProfileDirectory, started,
                cancellationToken, speakerInk).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = request.Mode == SceneSelectionMode.Automatic && !ocr.Dialogue.EnumerateRunes().Any(Rune.IsLetterOrDigit)
                ? ImageProcessingOutcome.NoSpeakableText : ImageProcessingOutcome.Recognized;
            return Result(outcome) with { Ocr = ocr };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return Result(ImageProcessingOutcome.Cancelled) with { Error = OcrErrorCode.Cancelled }; }
        catch (OcrException exception)
        { return Result(ImageProcessingOutcome.Failed) with { Error = exception.Code, Detail = exception.Message }; }

        ImageProcessingResult Result(ImageProcessingOutcome outcome) => new(request.Mode, outcome, snapshot?.Source, decision, null);
    }

    /// <summary>MissingOcr：执行ImageOcrService.Processing中的对应校验或计算步骤。</summary>
    private static OcrException MissingOcr(ImageProcessingRequest request) =>
        new(OcrErrorCode.Configuration, request.OcrConfigurationError ?? "未配置 OCR 模型与布局。");
}
