using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR.Paddle;

/// <summary>检测后端抽象：ONNX（Det）与 ncnn（NcnnDet）共用同一套后处理与调用点。</summary>
internal interface IOcrDetector : IDisposable
{
    /// <summary>检测文本框。</summary>
    PaddleOcrDetectionBox[] RunBoxes(Mat src);

    /// <summary>当前配置下可能的输入桶，供启动预热。</summary>
    int[] GetWarmupBuckets();

    /// <summary>预热门控：用零张量触发该桶的底层编译/分配。</summary>
    void Warmup(int bucket);

    string GetConfigName { get; }
}

/// <summary>识别后端抽象：ONNX（Rec，含 DML 堆叠批）与 ncnn（NcnnRec，逐行）共用一个调用点。</summary>
internal interface IOcrRecognizer : IDisposable
{
    OcrRecognizerResult[] Run(Mat[] srcs, int batchSize = 0);

    OcrRecognizerResult Run(Mat src);

    /// <summary>预热档位：(批大小, 桶宽)。ncnn 后端无批维，仅返回 (1, 宽度) 档。</summary>
    (int Batch, int Width)[] GetWarmupBuckets();

    void Warmup(int batch, int width);

    string GetConfigName { get; }
}
