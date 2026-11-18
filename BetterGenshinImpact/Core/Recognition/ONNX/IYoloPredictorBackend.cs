using System;
using Compunet.YoloSharp.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BetterGenshinImpact.Core.Recognition.ONNX;

/// <summary>
///     可替换 YoloSharp（ORT）直推路径的图像预测器后端抽象，当前实现为 ncnn（NcnnYoloPredictor）。
///     实现者须自行串行化 Detect / Classify（调用方可能从多线程进入）。
///     与 OCR 侧 IOcrDetector / IOcrRecognizer 一致保持 internal：后端选型属于装配细节，不进公共 API。
/// </summary>
internal interface IYoloPredictorBackend : IDisposable
{
    /// <summary>是否为分类模型（输出类别分数）；否则为检测模型（输出边界框）。</summary>
    bool IsClassifier { get; }

    /// <summary>后端与模型的描述名（日志与诊断展示用）。</summary>
    string ConfigName { get; }

    YoloResult<Detection> Detect(Image<Rgb24> image);

    YoloResult<Classification> Classify(Image<Rgb24> image);
}
