// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>ImageOcrService：原神自动配音的业务类型。</summary>
public sealed partial class ImageOcrService(ISceneProfileLoader layouts, ImagePreprocessor images, IOcrEngine engine,
    ISceneSelector? selector = null, QuestVoiceMetrics? metrics = null) : IImageOcrService, IImageProcessingService
{
    /// <summary>在取消检查和配置约束下执行识别。</summary>
    public async Task<ImageOcrResult> RecognizeAsync(ImageOcrRequest request, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var profile = await layouts.LoadAsync(request.TemplatePath, cancellationToken).ConfigureAwait(false);
        var snapshot = await ImageSnapshot.ReadAsync(request.ImagePath, cancellationToken).ConfigureAwait(false);
        return await RecognizeSnapshotAsync(snapshot, profile, request.Settings, request.ProfileDirectory, started, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>对同一图像快照执行预处理、推理与置信度接受。</summary>
    private async Task<ImageOcrResult> RecognizeSnapshotAsync(ImageSnapshot snapshot, SceneProfile profile,
        OcrSettings settings, string profileDirectory, DateTimeOffset started, CancellationToken token,
        SpeakerInkPolicy? speakerInk = null)
    {
        token.ThrowIfCancellationRequested();
        var preprocessingStart = Stopwatch.GetTimestamp();
        var prepared = await images.PrepareAsync(snapshot.Bytes, profile, token, speakerInk).ConfigureAwait(false);
        metrics?.Record(QuestVoiceMetric.Preprocess, Stopwatch.GetTimestamp() - preprocessingStart);
        token.ThrowIfCancellationRequested();
        var recognized = await engine.RecognizeAsync(prepared.Tensors, settings, profileDirectory, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var regions = OcrResultAssembler.Accept(profile.Regions.Select((region, index) =>
            new OcrRegionResult(region, prepared.Bounds[index], recognized.Texts[index].Text, "", recognized.Texts[index].Confidence, OcrRejectionReason.None)),
            settings.NameConfidence, settings.DialogueConfidence);
        return new(prepared.Width, prepared.Height, profile, regions, OcrResultAssembler.Speaker(regions),
            OcrResultAssembler.Dialogue(regions), recognized.Model, started, DateTimeOffset.UtcNow);
    }
}
