// 移植自 QuestVoice 1fc59d9c；保留原神业务算法，宿主适配见 QuestVoiceSessionFactory。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;
using SkiaSharp;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>ImageOcrService：原神自动配音的业务类型。</summary>
public sealed partial class ImageOcrService
{
    /// <summary>Uses the submitted encoded bytes and the profile selected from those same bytes.</summary>
    public async Task<ImageOcrResult> RecognizeFrameAsync(ContinuousFrameSnapshot frame,
        ContinuousFrameInspection inspection, OcrSettings settings, string profileDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var started = DateTimeOffset.UtcNow;
        if (inspection.Selection.Decision.Kind != SceneDecisionKind.Selected || inspection.Selection.Profile is not { } profile)
            throw new OcrException(OcrErrorCode.Configuration, "连续帧没有已选择的识别布局。");
        if (!StringComparer.Ordinal.Equals(inspection.Source.Sha256, frame.Sha256))
            throw new OcrException(OcrErrorCode.Image, "连续帧的观察来源与识别快照不一致。");
        var bytes = frame.Bytes;
        var snapshot = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var data = SKData.CreateCopy(bytes.Span);
            using var codec = SKCodec.Create(data);
            if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Bmp))
                throw new OcrException(OcrErrorCode.Image, "图片无法解码或格式不支持。");
            if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 80_000_000
                || inspection.Source.Width != codec.Info.Width || inspection.Source.Height != codec.Info.Height)
                throw new OcrException(OcrErrorCode.Image, "连续帧的观察尺寸与识别快照不一致或超出上限。");
            cancellationToken.ThrowIfCancellationRequested();
            return new ImageSnapshot(bytes, inspection.Source);
        }, cancellationToken).ConfigureAwait(false);
        return await RecognizeSnapshotAsync(snapshot, profile, settings, profileDirectory, started,
            cancellationToken, inspection.Selection.SpeakerInk).ConfigureAwait(false);
    }
}

/// <summary>Configuration adapter; preprocessing, inference and acceptance stay in ImageOcrService.</summary>
public sealed class ContinuousFrameRecognizer(ImageOcrService images, AppSettings settings) : IContinuousFrameRecognizer
{
    /// <summary>在取消检查和配置约束下执行识别。</summary>
    public Task<ImageOcrResult> RecognizeAsync(ContinuousFrameSnapshot frame, ContinuousFrameInspection inspection,
        CancellationToken cancellationToken) => images.RecognizeFrameAsync(frame, inspection,
            settings.Ocr ?? throw new OcrException(OcrErrorCode.Configuration, settings.OcrConfigurationError ?? "未配置 OCR 模型与布局。"),
            Path.Combine(settings.Output.LogsDirectory, "ocr"), cancellationToken);
}
