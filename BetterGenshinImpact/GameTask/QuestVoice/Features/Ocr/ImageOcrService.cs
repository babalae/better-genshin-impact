// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using BetterGenshinImpact.GameTask.QuestVoice.Features.Diagnostics;
using SkiaSharp;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>按场景识别选出的布局裁剪同一帧，交给 OCR 引擎识别并按置信度接受姓名和正文。</summary>
public sealed class ImageOcrService(ImagePreprocessor images, IOcrEngine engine, QuestVoiceMetrics? metrics = null)
{
    /// <summary>使用提交的编码帧和从同一帧选出的布局识别对白。</summary>
    public async Task<ImageOcrResult> RecognizeFrameAsync(ContinuousFrameSnapshot frame,
        ContinuousFrameInspection inspection, OcrSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var started = DateTimeOffset.UtcNow;
        if (inspection.Selection.Decision.Kind != SceneDecisionKind.Selected || inspection.Selection.Profile is not { } profile)
            throw new OcrException(OcrErrorCode.Configuration, "连续帧没有已选择的识别布局。");
        if (!StringComparer.Ordinal.Equals(inspection.Source.Sha256, frame.Sha256))
            throw new OcrException(OcrErrorCode.Image, "连续帧的观察来源与识别快照不一致。");
        var bytes = frame.Bytes;
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var data = SKData.CreateCopy(bytes.Span);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Bmp))
                throw new OcrException(OcrErrorCode.Image, "图片无法解码或格式不支持。");
            if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 80_000_000
                || inspection.Source.Width != codec.Info.Width || inspection.Source.Height != codec.Info.Height)
                throw new OcrException(OcrErrorCode.Image, "连续帧的观察尺寸与识别快照不一致或超出上限。");
        }, cancellationToken).ConfigureAwait(false);

        // 预处理、推理与置信度接受，三步都基于同一份帧数据。
        var preprocessingStart = Stopwatch.GetTimestamp();
        var prepared = await images.PrepareAsync(bytes, profile, cancellationToken, inspection.Selection.SpeakerInk).ConfigureAwait(false);
        metrics?.Record(QuestVoiceMetric.Preprocess, Stopwatch.GetTimestamp() - preprocessingStart);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recognized = await engine.RecognizeAsync(prepared.Lines, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var regions = OcrResultAssembler.Accept(profile.Regions.Select((region, index) =>
                new OcrRegionResult(region, prepared.Bounds[index], recognized.Texts[index].Text, "", recognized.Texts[index].Confidence, OcrRejectionReason.None)),
                settings.NameConfidence, settings.DialogueConfidence);
            return new(prepared.Width, prepared.Height, profile, regions, OcrResultAssembler.Speaker(regions),
                OcrResultAssembler.Dialogue(regions), recognized.Model, started, DateTimeOffset.UtcNow);
        }
        finally
        {
            foreach (var line in prepared.Lines) line.Dispose();
        }
    }
}
