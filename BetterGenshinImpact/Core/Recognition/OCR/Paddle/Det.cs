using System;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR.Paddle;

public class Det(BgiOnnxModel model, OcrVersionConfig config, BgiOnnxFactory bgiOnnxFactory)
    : IDisposable
{
    private readonly InferenceSession _session = bgiOnnxFactory.CreateInferenceSession(model, true);

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

    ~Det()
    {
        lock (_session)
        {
            _session.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_session)
        {
            _session.Dispose();
        }
        GC.SuppressFinalize(this);
    }

    public RotatedRect[] Run(Mat src)
    {
        return RunBoxes(src).Select(box => box.Rect).ToArray();
    }

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
            resizedSize = new Size(resized.Width, resized.Height);
            var inputTensor = OcrUtils.NormalizeToTensorDnn(resized, config.NormalizeImage.Scale,
                config.NormalizeImage.Mean, config.NormalizeImage.Std, out var owner);
            using (owner)
            {
                lock (_session)
                {
                    using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run([
                        NamedOnnxValue.CreateFromTensor(_session.InputNames[0], inputTensor)
                    ]);
                    var output = results[0];
                    if (output.ElementType is not TensorElementType.Float)
                        throw new Exception($"Unexpected output tensor type: {output.ElementType}");

                    if (output.ValueType is not OnnxValueType.ONNX_TYPE_TENSOR)
                        throw new Exception($"Unexpected output tensor value type: {output.ValueType}");
                    var outputTensor = output.AsTensor<float>();
                    return OcrUtils.Tensor2Mat(outputTensor);
                    // 因为一个已知bug,tensor中内存在dml下使用完后会被释放掉,锁之外的代码会报错
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
        using var preprocessed = PrepareTinyImage(src);

        var size = preprocessed.Size();
        var height = size.Height;
        var width = size.Width;

        var ratio = CalculateResizeRatio(width, height, LimitSideLen, LimitType);
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

    private static Mat PrepareTinyImage(Mat src)
    {
        if (src.Width + src.Height >= 64)
        {
            return src.Clone();
        }

        var newHeight = Math.Max(32, src.Height);
        var newWidth = Math.Max(32, src.Width);
        var padded = new Mat(newHeight, newWidth, src.Type(), Scalar.Black);
        using var roi = padded[new Rect(0, 0, src.Width, src.Height)];
        src.CopyTo(roi);
        return padded;
    }

    private static double CalculateResizeRatio(int width, int height, int limitSideLen, string limitType)
    {
        return limitType switch
        {
            "max" => Math.Max(height, width) > limitSideLen
                ? 1.0 * limitSideLen / Math.Max(height, width)
                : 1.0,
            "min" => Math.Min(height, width) < limitSideLen
                ? 1.0 * limitSideLen / Math.Min(height, width)
                : 1.0,
            "resize_long" => 1.0 * limitSideLen / Math.Max(height, width),
            _ => throw new ArgumentOutOfRangeException(nameof(limitType), limitType,
                "limitType only supports max/min/resize_long")
        };
    }

    public string GetConfigName => config.Name;
}
