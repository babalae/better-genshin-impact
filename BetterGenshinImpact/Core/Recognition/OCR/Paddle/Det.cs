using System;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.ONNX;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR.Paddle;

public class Det : IOcrDetector
{
    public Det(BgiOnnxModel model, OcrVersionConfig config, BgiOnnxSessionFactory bgiOnnxFactory)
        : this(config, new OrtOcrInferenceSession(bgiOnnxFactory.CreateInferenceSession(model, true)))
    {
    }

    /// <summary>注入任意推理会话（任意后端）的构造入口，预处理与后处理完全共用。</summary>
    internal Det(OcrVersionConfig config, IOcrInferenceSession session)
    {
        Config = config;
        _session = session;
    }

    private readonly IOcrInferenceSession _session;
    private readonly OcrVersionConfig Config;

    private readonly object _sessionLock = new();
    private bool _disposed;

    /// <summary>Gets or sets the detection side length limit used by the Python det preprocess.</summary>
    public int LimitSideLen { get; set; } = 960;

    /// <summary>Gets or sets the maximum size limit after resizing.</summary>
    public int MaxSideLimit { get; set; } = 4000;

    /// <summary>Gets or sets the side length limit type. Supports max/min/resize_long.</summary>
    public string LimitType { get; set; } = "max";

    /// <summary>Gets or sets whether the official 2x2 dilation is enabled during post-processing.</summary>
    public bool UseDilation { get; set; }

    /// <summary>Gets or sets the score threshold for filtering out possible text boxes.</summary>
    public float BoxScoreThreshold { get; set; } = 0.6f;

    /// <summary>Gets or sets the threshold to binarize the text region.</summary>
    public float BoxThreshold { get; set; } = 0.3f;

    /// <summary>Gets or sets the maximum number of contours processed by DBPostProcess.</summary>
    public int MaxCandidates { get; set; } = 1000;

    /// <summary>Gets or sets the minimum size of the text boxes to be considered as valid.</summary>
    public int MinSize { get; set; } = 3;

    /// <summary>Gets or sets the ratio for enlarging text boxes during post-processing.</summary>
    public float UnclipRatio { get; set; } = 1.5f;

    public void Dispose()
    {
        lock (_sessionLock)
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
        }
    }

    public RotatedRect[] Run(Mat src)
    {
        return RunBoxes(src).Select(box => box.Rect).ToArray();
    }

    PaddleOcrDetectionBox[] IOcrDetector.RunBoxes(Mat src) => RunBoxes(src);

    internal PaddleOcrDetectionBox[] RunBoxes(Mat src)
    {
        using var pred = RunRaw(src, out var resizedSize);
        //OpenCvSharp.OpenCVException: 0 <= _colRange.start && _colRange.start <= _colRange.end && _colRange.end <= m.cols
        using var roi = pred[0, resizedSize.Height, 0, resizedSize.Width];
        var postProcessor = new DbPostProcessor(
            BoxThreshold,
            BoxScoreThreshold,
            MaxCandidates,
            UnclipRatio,
            MinSize,
            UseDilation);
        return postProcessor.Run(roi, src.Size());
    }

    public Mat RunRaw(Mat src, out Size resizedSize)
    {
        Mat? converted = null;
        var input = src.Channels() switch
        {
            4 => converted = src.CvtColor(ColorConversionCodes.BGRA2BGR),
            1 => converted = src.CvtColor(ColorConversionCodes.GRAY2BGR),
            3 => src,
            var x => throw new Exception($"Unexpect src channel: {x}, allow: (1/3/4)")
        };
        try
        {
            // 与 PaddleOCR Python DetResizeForTest 保持一致：
            // 1. h + w < 64 时先将图像补全到至少 32x32
            // 2. 按 limit_type 规则缩放，并将宽高分别对齐到 32 的倍数
            using var resized = MatResizeForDetection(input);
            resizedSize = resized.Size();
            var inputTensor = OcrUtils.NormalizeToTensorDnn(resized, Config.NormalizeImage.Scale,
                Config.NormalizeImage.Mean, Config.NormalizeImage.Std, out var owner);
            using (owner)
            {
                lock (_sessionLock)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    var output = _session.Run(inputTensor)[0];
                    // 输出 [1,1,H,W] 概率图；DML 等后端的输出内存在 Run 返回后即失效，
                    // 因此数据在会话锁内拷出（OcrInferenceResult 为副本）。
                    var height = output.Shape[2];
                    var width = output.Shape[3];
                    var mat = new Mat(height, width, MatType.CV_32FC1);
                    output.Data.AsSpan(0, height * width).CopyTo(mat.AsSpan<float>());
                    return mat;
                }
            }
        }
        finally
        {
            converted?.Dispose();
        }
    }

    /// <summary>
    /// 按 PaddleOCR Python 的 DetResizeForTest 缩放图像：
    /// 超小图先补边，再按 max/min/resize_long 规则缩放，并对齐到 32 的倍数。
    /// </summary>
    /// <param name="src"></param>
    /// <returns></returns>
    private Mat MatResizeForDetection(Mat src)
    {
        using var preprocessed = OcrUtils.PrepareTinyImage(src);

        var size = preprocessed.Size();
        var height = size.Height;
        var width = size.Width;

        var ratio = OcrUtils.CalculateResizeRatio(width, height, LimitSideLen, LimitType);
        var resizeHeight = (int)(height * ratio);
        var resizeWidth = (int)(width * ratio);

        if (Math.Max(resizeHeight, resizeWidth) > MaxSideLimit)
        {
            var maxSideRatio = 1.0 * MaxSideLimit / Math.Max(resizeHeight, resizeWidth);
            resizeHeight = (int)(resizeHeight * maxSideRatio);
            resizeWidth = (int)(resizeWidth * maxSideRatio);
        }

        resizeHeight = Math.Max((int)Math.Round(resizeHeight / 32.0) * 32, 32);
        resizeWidth = Math.Max((int)Math.Round(resizeWidth / 32.0) * 32, 32);

        if (resizeWidth <= 0 || resizeHeight <= 0)
        {
            throw new InvalidOperationException(
                $"Invalid det resize target size: {resizeWidth}x{resizeHeight}, src={width}x{height}");
        }

        return preprocessed.Resize(new Size(resizeWidth, resizeHeight));
    }

    public string GetConfigName => Config.Name;
}
