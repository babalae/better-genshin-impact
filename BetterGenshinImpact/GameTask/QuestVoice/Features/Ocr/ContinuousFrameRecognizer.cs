using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Configuration;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Continuous;
using BetterGenshinImpact.GameTask.QuestVoice.Core.Ocr;

namespace BetterGenshinImpact.GameTask.QuestVoice.Features.Ocr;

/// <summary>把本轮 OCR 阈值绑定到帧会话的识别边界；预处理、推理与接受仍由 ImageOcrService 完成。</summary>
public sealed class ContinuousFrameRecognizer(ImageOcrService images, AppSettings settings) : IContinuousFrameRecognizer
{
    /// <summary>在取消检查和配置约束下执行识别。</summary>
    public Task<ImageOcrResult> RecognizeAsync(ContinuousFrameSnapshot frame, ContinuousFrameInspection inspection,
        CancellationToken cancellationToken) => images.RecognizeFrameAsync(frame, inspection,
            settings.Ocr ?? new OcrSettings(), cancellationToken);
}
