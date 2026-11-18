using System;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR.Paddle;

/// <summary>检测后端抽象：ONNX（Det）与 ncnn（NcnnDet）共用同一套后处理与调用点。</summary>
internal interface IOcrDetector : IDisposable
{
    /// <summary>检测文本框。</summary>
    PaddleOcrDetectionBox[] RunBoxes(Mat src);

    string GetConfigName { get; }
}

/// <summary>识别后端抽象：ONNX（Rec，批处理）与 ncnn（NcnnRec，逐行）共用一个调用点。</summary>
internal interface IOcrRecognizer : IDisposable
{
    OcrRecognizerResult[] Run(Mat[] srcs, int batchSize = 0);

    OcrRecognizerResult Run(Mat src);

    string GetConfigName { get; }
}
