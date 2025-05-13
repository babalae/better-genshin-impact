using System;
using System.Linq;
using BetterGenshinImpact.Core.Recognition.OCR.engine;
using BetterGenshinImpact.Core.Recognition.ONNX;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR.paddle;

public class Det
{
    private readonly OcrVersionConfig _config;
    private readonly InferenceSession _session;

    public Det(BgiOnnxModel model, OcrVersionConfig config)
    {
        _config = config;
        _session = BgiOnnxFactory.Instance.CreateInferenceSession(model, true);
    }

    /// <summary>Gets or sets the maximum size for resizing the input image.</summary>
    public int? MaxSize { get; set; } = 1536;

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
        // var size = src.Size();
        var scaleRate = 1.0 * src.Width / resizedSize.Width;

        var rects = contours
            .Where(x => BoxScoreThreshold == null || GetScore(x, pred) > BoxScoreThreshold)
            .Select(Cv2.MinAreaRect)
            .Where(x => x.Size.Width > MinSize && x.Size.Height > MinSize)
            .Select(rect =>
            {
                var minEdge = Math.Min(rect.Size.Width, rect.Size.Height);
                Size2f newSize = new(
                    (rect.Size.Width + UnclipRatio * minEdge) * scaleRate,
                    (rect.Size.Height + UnclipRatio * minEdge) * scaleRate);
                RotatedRect largerRect = new(rect.Center * scaleRate, newSize, rect.Angle);
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
        using (var resized = MatResize(padded, MaxSize))
        {
            resizedSize = new Size(resized.Width, resized.Height);
            padded = MatPadding32(resized);
        }

        using (var _ = padded)
        {
            var inputTensor = OcrUtils.NormalizeToTensorDnn(padded, _config.NormalizeImage.Scale,
                _config.NormalizeImage.Mean, _config.NormalizeImage.Std, out var owner);
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

    private static Mat MatResize(Mat src, int? maxSize)
    {
        if (maxSize == null) return src.Clone();

        var size = src.Size();
        var longEdge = Math.Max(size.Width, size.Height);
        var scaleRate = 1.0 * maxSize.Value / longEdge;
        return scaleRate < 1.0 ? src.Resize(default, scaleRate, scaleRate) : src.Clone();
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
}