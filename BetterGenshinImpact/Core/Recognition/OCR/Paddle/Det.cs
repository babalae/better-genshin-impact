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

    /// <summary>Gets or sets the shortest side threshold used for tiny-image upscaling.</summary>
    public int SmallImageLimitSideLen { get; set; } = 64;

    /// <summary>Gets or sets the size for dilation during preprocessing.</summary>
    public int? DilatedSize { get; set; } = 2;

    /// <summary>Gets or sets the score threshold for filtering out possible text boxes.</summary>
    public float? BoxScoreThreshold { get; set; } = 0.7f;

    /// <summary>Gets or sets the threshold to binarize the text region.</summary>
    public float? BoxThreshold { get; set; } = 0.3f;

    /// <summary>Gets or sets the minimum size of the text boxes to be considered as valid.</summary>
    public int MinSize { get; set; } = 3;

    /// <summary>Gets or sets the ratio for enlarging text boxes during post-processing.</summary>
    public float UnclipRatio { get; set; } = 2.0f;

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
        using var pred = RunRaw(src, out var resizedSize);
        using Mat cbuf = new();
        //OpenCvSharp.OpenCVException: 0 <= _colRange.start && _colRange.start <= _colRange.end && _colRange.end <= m.cols
        using var roi = pred[0, resizedSize.Height, 0, resizedSize.Width];
        roi.ConvertTo(cbuf, MatType.CV_8UC1, 255);
        using Mat dilated = new();
        using var binary = BoxThreshold != null
            ? cbuf.Threshold((int)(BoxThreshold * 255), 255, ThresholdTypes.Binary)
            : cbuf;
        if (DilatedSize != null)
        {
            using var ones =
                Cv2.GetStructuringElement(MorphShapes.Rect, new Size(DilatedSize.Value, DilatedSize.Value));
            Cv2.Dilate(binary, dilated, ones);
        }
        else
        {
            Cv2.CopyTo(binary, dilated);
        }

        var contours = dilated.FindContoursAsArray(RetrievalModes.List, ContourApproximationModes.ApproxSimple);
        // PaddleOCR Python keeps ratio_h/ratio_w separately. Because we align det resize to that behavior,
        // map contour points back with independent X/Y scales before building rotated rects.
        var scaleX = 1.0 * src.Width / resizedSize.Width;
        var scaleY = 1.0 * src.Height / resizedSize.Height;

        var rects = contours
            .Where(x => BoxScoreThreshold == null || GetScore(x, pred) > BoxScoreThreshold)
            .Select(contour =>
                contour.Select(point => new Point2f((float)(point.X * scaleX), (float)(point.Y * scaleY))).ToArray())
            .Select(Cv2.MinAreaRect)
            .Where(x => x.Size.Width > MinSize && x.Size.Height > MinSize)
            .Select(rect =>
            {
                var minEdge = Math.Min(rect.Size.Width, rect.Size.Height);
                Size2f newSize = new(
                    rect.Size.Width + UnclipRatio * minEdge,
                    rect.Size.Height + UnclipRatio * minEdge);
                RotatedRect largerRect = new(rect.Center, newSize, rect.Angle);
                return largerRect;
            })
            .OrderBy(v => v.Center.Y)
            .ThenBy(v => v.Center.X)
            .ToArray();
        //{
        //	using Mat demo = dilated.CvtColor(ColorConversionCodes.GRAY2RGB);
        //	demo.DrawContours(contours, -1, Scalar.Red);
        //	Image(demo).Dump();
        //}
        return rects;
    }

    public Mat RunRaw(Mat src, out Size resizedSize)
    {
        var padded = src.Channels() switch
        {
            4 => src.CvtColor(ColorConversionCodes.BGRA2BGR),
            1 => src.CvtColor(ColorConversionCodes.GRAY2BGR),
            3 => src,
            var x => throw new Exception($"Unexpect src channel: {x}, allow: (1/3/4)")
        };
        // Align with PaddleOCR Python DetResizeForTest:
        // 1. tiny image padding when h + w < 64
        // 2. keep current max/960 behavior for normal images
        // 3. switch to limit_type=min for very small crops so det can see the text
        using (var resized = MatResizeForDetection(padded))
        {
            resizedSize = new Size(resized.Width, resized.Height);
            padded = resized.Clone();
        }

        using (var _ = padded)
        {
            var inputTensor = OcrUtils.NormalizeToTensorDnn(padded, config.NormalizeImage.Scale,
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
    }

    private static Mat MatPadding32(Mat src)
    {
        var size = src.Size();
        Size newSize = new(
            32 * Math.Ceiling(1.0 * size.Width / 32),
            32 * Math.Ceiling(1.0 * size.Height / 32));
        return src.CopyMakeBorder(0, newSize.Height - size.Height, 0, newSize.Width - size.Width, BorderTypes.Constant,
            Scalar.Black);
    }

    /// <summary>
    /// 按 PaddleOCR Python 的 DetResizeForTest 思路缩放图像：
    /// 小图先补边，再按 max/min/resize_long 规则缩放，并对齐到 32 的倍数。
    /// </summary>
    /// <param name="src"></param>
    /// <returns></returns>
    private Mat MatResizeForDetection(Mat src)
    {
        using var preprocessed = PrepareTinyImage(src);

        var size = preprocessed.Size();
        var height = size.Height;
        var width = size.Width;

        var limitSideLen = LimitSideLen;
        var limitType = LimitType;

        if (Math.Min(height, width) < SmallImageLimitSideLen)
        {
            limitSideLen = SmallImageLimitSideLen;
            limitType = "min";
        }

        var ratio = CalculateResizeRatio(width, height, limitSideLen, limitType);
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

        using var resized = preprocessed.Resize(new Size(resizeWidth, resizeHeight));
        return MatPadding32(resized);
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
        src.CopyTo(padded[new Rect(0, 0, src.Width, src.Height)]);
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

    private static float GetScore(Point[] contour, Mat pred)
    {
        var width = pred.Width;
        var height = pred.Height;
        var boxX = contour.Select(v => v.X).ToArray();
        var boxY = contour.Select(v => v.Y).ToArray();

        var xmin = Math.Clamp(boxX.Min(), 0, width - 1);
        var xmax = Math.Clamp(boxX.Max(), 0, width - 1);
        var ymin = Math.Clamp(boxY.Min(), 0, height - 1);
        var ymax = Math.Clamp(boxY.Max(), 0, height - 1);

        var rootPoints = contour
            .Select(v => new Point(v.X - xmin, v.Y - ymin))
            .ToArray();
        using Mat mask = new(ymax - ymin + 1, xmax - xmin + 1, MatType.CV_8UC1, Scalar.Black);
        mask.FillPoly(new[] { rootPoints }, new Scalar(1));

        using var croppedMat = pred[ymin, ymax + 1, xmin, xmax + 1];
        var score = (float)croppedMat.Mean(mask).Val0;

        // Debug
        //{
        //	using Mat cu = new Mat();
        //	croppedMat.ConvertTo(cu, MatType.CV_8UC1, 255);
        //	Util.HorizontalRun(true, Image(cu), Image(mask), score).Dump();
        //}

        return score;
    }

    public string GetConfigName => config.Name;
}
