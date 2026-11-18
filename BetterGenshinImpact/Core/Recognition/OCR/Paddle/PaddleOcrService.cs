using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.ONNX;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using Size = OpenCvSharp.Size;

namespace BetterGenshinImpact.Core.Recognition.OCR.Paddle;

public class PaddleOcrService : IOcrService, IDisposable
{
    /// <summary>
    ///     Usage:
    ///     https://github.com/sdcb/PaddleSharp/blob/master/docs/ocr.md
    ///     模型列表:
    ///     https://github.com/PaddlePaddle/PaddleOCR/blob/release/2.5/doc/doc_ch/models_list.md
    /// </summary>
    private readonly IOcrDetector _localDetModel;

    private readonly IOcrRecognizer _localRecModel;
    private readonly object _inferenceLock = new();
    private bool _disposed;

    /// <summary>本实例的推理后端名（ONNX Runtime / ncnn），由路由结果确定；诊断与测速描述用。</summary>
    public string BackendName { get; }

    /// <summary>Gets or sets the official recognition score threshold used to discard low-confidence results.</summary>
    public float DropScore { get; set; } = 0.5f;

    public record PaddleOcrModelType(
        BgiOnnxModel DetectionModel,
        OcrVersionConfig DetectionVersion,
        BgiOnnxModel RecognitionModel,
        OcrVersionConfig RecognitionVersion,
        Func<IReadOnlyList<string>> RecLabel,
        String PreHeatImagePath
    )
    {
        /// <summary>
        /// 默认预热图，也用于推理验证和测速。V4、V5 等其他版本的模型已移入插件（见 Docs/design/plugin-system.md 第 17 节）。
        /// </summary>
        public static string TestImagePath = Global.Absolute(@"Assets\Model\PaddleOCR\test_pp_ocr.png");

        private static readonly Func<BgiOnnxModel, IReadOnlyList<string>> DefaultRecLabelFunc =
            recModel =>
            {
                const string modelConfigFileName = "inference.yml";
                var configFilePath = Path.Combine(
                    Path.GetDirectoryName(recModel.ModalPath) ??
                    throw new InvalidOperationException("Cannot get model directory"),
                    modelConfigFileName);

                if (!File.Exists(configFilePath))
                    throw new FileNotFoundException(
                        $"PaddleOCR config file {modelConfigFileName} not found: {configFilePath}");

                using var reader = new StreamReader(configFilePath);
                var parser = new Parser(reader);

                // Traverse YAML to find PostProcess:character_dict
                while (parser.MoveNext())
                {
                    if (parser.Current is not YamlDotNet.Core.Events.Scalar { Value: "PostProcess" }) continue;
                    parser.MoveNext(); // Should be MappingStart
                    while (parser.MoveNext())
                    {
                        if (parser.Current is not YamlDotNet.Core.Events.Scalar { Value: "character_dict" }) continue;
                        parser.MoveNext(); // Should be SequenceStart
                        var result = new List<string>();
                        while (parser.MoveNext())
                        {
                            switch (parser.Current)
                            {
                                case SequenceEnd:
                                    return result;
                                case YamlDotNet.Core.Events.Scalar charScalar:
                                    result.Add(charScalar.Value);
                                    break;
                            }
                        }
                    }
                }

                throw new InvalidOperationException("未在 YAML 的 PostProcess 部分找到 character_dict。");
            };


        /// <summary>
        /// 组合检测与识别模型。字典默认读取识别模型同目录的 inference.yml，预热图默认为 <see cref="TestImagePath"/>。
        /// 插件用它组合自己目录中的模型。
        /// </summary>
        public static PaddleOcrModelType Create(
            BgiOnnxModel detectionModel,
            OcrVersionConfig detectionVersion,
            BgiOnnxModel recognitionModel,
            OcrVersionConfig recognitionVersion,
            String? preHeatImagePath = null,
            Func<IReadOnlyList<string>>? recLabel = null
        )
        {
            return new PaddleOcrModelType(
                detectionModel,
                detectionVersion,
                recognitionModel,
                recognitionVersion,
                recLabel ?? (() => DefaultRecLabelFunc(recognitionModel)),
                preHeatImagePath ?? TestImagePath);
        }

        internal (IOcrDetector Det, IOcrRecognizer Rec) Build(BgiOnnxSessionFactory onnxFactory)
        {
            var detection = new Det(DetectionModel, DetectionVersion, onnxFactory);
            try { return (detection, new Rec(RecognitionModel, RecLabel(), RecognitionVersion, onnxFactory)); }
            catch { detection.Dispose(); throw; }
        }

        /// <summary>
        /// 主程序内置的 PP-OCRv6 模型组合。
        /// </summary>
        public static readonly PaddleOcrModelType V6 = Create(
            BgiOnnxModel.PaddleOcrDetV6,
            OcrVersionConfig.PpOcrV6,
            BgiOnnxModel.PaddleOcrRecV6,
            OcrVersionConfig.PpOcrV6);

    }

    public PaddleOcrService(BgiOnnxFactory bgiOnnxFactory, PaddleOcrModelType modelType)
        : this(bgiOnnxFactory.ForCurrentRuntime(), modelType)
    {
    }

    /// <summary>
    /// 用指定的会话工厂加载模型并预热。可插拔实现（内置 PP-OCRv6 和插件中的模型）通过它创建。
    /// 推理后端按设置页的逐用途开关路由（ONNX Runtime / ncnn），路由细节见 <see cref="InferenceBackendRouter" />。
    /// </summary>
    /// <param name="bgiOnnxFactory">绑定推理运行时的会话工厂。</param>
    /// <param name="modelType">检测与识别模型组合。</param>
    /// <param name="logger">路由与后端装配日志；DI 未就绪时为 null，由路由器退回 NullLogger。</param>
    public PaddleOcrService(BgiOnnxSessionFactory bgiOnnxFactory, PaddleOcrModelType modelType, ILogger? logger = null)
    {
        var standalone = ResolveStandaloneBackends(modelType, logger);
        var (det, rec, backendName) = standalone ?? WithName(ModelBackends(bgiOnnxFactory, modelType));
        BackendName = backendName;
        _localDetModel = det;
        _localRecModel = rec;
        Warmup(modelType);
    }

    /// <summary>ONNX 路线：模型自身的 Det/Rec 组合。</summary>
    private static (IOcrDetector Det, IOcrRecognizer Rec) ModelBackends(BgiOnnxSessionFactory bgiOnnxFactory,
        PaddleOcrModelType modelType)
        => modelType.Build(bgiOnnxFactory);

    private static (IOcrDetector Det, IOcrRecognizer Rec, string BackendName) WithName(
        (IOcrDetector Det, IOcrRecognizer Rec) backends, string backendName = "ONNX Runtime")
        => (backends.Det, backends.Rec, backendName);

    /// <summary>后端路由：独立后端（ncnn）装配结果；ONNX 时返回 null（走模型自身的 Build）。</summary>
    private static (IOcrDetector Det, IOcrRecognizer Rec, string BackendName)? ResolveStandaloneBackends(
        PaddleOcrModelType modelType, ILogger? logger)
    {
        return ONNX.InferenceBackendRouter.CreateOcrBackends(modelType,
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }

    /// <summary>加载后预热一次（所有构造路径共用）。</summary>
    private void Warmup(PaddleOcrModelType modelType)
    {
        try
        {
            using var preHeatImageMat = Bv.ImRead(modelType.PreHeatImagePath) ??
                                        throw new FileNotFoundException($"预热图片未找到: {modelType.PreHeatImagePath}");
            var preHeatResult = RunAll(preHeatImageMat, 1);
            Debug.WriteLine(
                $"PaddleOcrService 预热完成，使用模型: {modelType.DetectionModel.Name} 和 {modelType.RecognitionModel.Name}，结果: {preHeatResult.Text}");
        }
        catch
        {
            _localDetModel.Dispose();
            _localRecModel.Dispose();
            throw;
        }
    }

    /// <summary>注入任意后端组合（ONNX Runtime 或 ncnn）。后端所有权转移给本实例，Dispose 时一并释放。</summary>
    internal PaddleOcrService((IOcrDetector Det, IOcrRecognizer Rec) backends, PaddleOcrModelType modelType,
        string backendName = "ONNX Runtime")
    {
        BackendName = backendName;
        _localDetModel = backends.Det;
        _localRecModel = backends.Rec;
        Warmup(modelType);
    }

    /// <summary>
    ///     推荐传入三通道BGR mat，虽然四通道和单通道也做了兼容，但是三通道最快
    /// </summary>
    public string Ocr(Mat mat)
    {
        return OcrResult(mat).Text;
    }

    /// <summary>
    ///     推荐传入三通道BGR mat，虽然四通道和单通道也做了兼容，但是三通道最快
    /// </summary>
    public OcrResult OcrResult(Mat mat)
    {
        lock (_inferenceLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (mat.Channels() == 4)
            {
                using var mat3 = mat.CvtColor(ColorConversionCodes.BGRA2BGR);
                return _OcrResult(mat3);
        }

        return _OcrResult(mat);
        }
    }

    /// <summary>
    ///     推荐传入三通道BGR mat，虽然四通道和单通道也做了兼容，但是三通道最快
    /// </summary>
    public string OcrWithoutDetector(Mat mat)
    {
        var startTime = Stopwatch.GetTimestamp();
        var str = OcrResultWithoutDetector(mat).Text;
        var time = Stopwatch.GetElapsedTime(startTime);
        // Debug.WriteLine($"PaddleOcrWithoutDetector 耗时 {time.TotalMilliseconds}ms 结果: {str}");
        return str;
    }

    /// <summary>
    ///     不经过检测模型，识别一张已裁剪好的单行文字图像，返回文字和置信度。
    ///     推荐传入三通道BGR mat，虽然四通道和单通道也做了兼容，但是三通道最快
    /// </summary>
    public OcrRecognizerResult OcrResultWithoutDetector(Mat mat)
    {
        lock (_inferenceLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _localRecModel.Run(mat);
        }
    }

    /// <summary>
    ///     不经过检测模型，按输入顺序批量识别已裁剪好的单行文字图像，保留每行的置信度。
    ///     整批在同一次加锁内完成，与其他识别共用同一把推理锁。
    /// </summary>
    public OcrRecognizerResult[] OcrResultLinesWithoutDetector(Mat[] lines)
    {
        lock (_inferenceLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _localRecModel.Run(lines);
        }
    }

    private OcrResult _OcrResult(Mat mat)
    {
        var startTime = Stopwatch.GetTimestamp();
        var result = RunAll(mat);
        var time = Stopwatch.GetElapsedTime(startTime);
        // Debug.WriteLine($"PaddleOcr 耗时 {time.TotalMilliseconds}ms 结果: {result.Text}");
        return result;
    }

    /// <summary>
    ///     推荐传入三通道BGR mat，虽然四通道和单通道也做了兼容，但是三通道最快
    /// </summary>
    private OcrResult RunAll(Mat src, int recognizeBatchSize = 0)
    {
        var boxes = SortBoxes(_localDetModel.RunBoxes(src));
        List<Mat> mats = new(boxes.Length);
        try
        {
            foreach (var box in boxes)
            {
                mats.Add(GetRotateCropImage(src, box.Points));
            }

            return new OcrResult(_localRecModel.Run(mats.ToArray(), recognizeBatchSize)
                .Select((result, i) => new OcrResultRegion(boxes[i].Rect, result.Text, result.Score))
                .Where(region => region.Score >= DropScore)
                .ToArray());
        }
        finally
        {
            foreach (var mat in mats) mat.Dispose();
        }
    }

    /// <summary>
    ///     按 PaddleOCR 官方 get_rotate_crop_image 的方式对检测框执行透视裁剪。
    /// </summary>
    /// <param name="src">原始图像。</param>
    /// <param name="points">按左上、右上、右下、左下排列的四个顶点。</param>
    /// <returns>透视变换后的文字图像。</returns>
    private static Mat GetRotateCropImage(Mat src, Point2f[] points)
    {
        if (points.Length != 4)
        {
            throw new ArgumentException("OCR detection box must contain exactly four points.", nameof(points));
        }

        var cropWidth = (int)Math.Max(GetDistance(points[0], points[1]), GetDistance(points[2], points[3]));
        var cropHeight = (int)Math.Max(GetDistance(points[0], points[3]), GetDistance(points[1], points[2]));
        if (cropWidth <= 0 || cropHeight <= 0)
        {
            throw new InvalidOperationException($"Invalid OCR crop size: {cropWidth}x{cropHeight}");
        }

        Point2f[] destinationPoints =
        [
            new(0, 0),
            new(cropWidth, 0),
            new(cropWidth, cropHeight),
            new(0, cropHeight)
        ];
        using var transform = Cv2.GetPerspectiveTransform(points, destinationPoints);
        var cropped = new Mat();
        try
        {
            Cv2.WarpPerspective(src, cropped, transform, new Size(cropWidth, cropHeight),
                InterpolationFlags.Cubic, BorderTypes.Replicate);

            if (cropped.Height * 1.0 / cropped.Width >= 1.5)
            {
                Cv2.Rotate(cropped, cropped, RotateFlags.Rotate90Counterclockwise);
            }

            return cropped;
        }
        catch
        {
            cropped.Dispose();
            throw;
        }
    }

    private static double GetDistance(Point2f first, Point2f second)
    {
        var deltaX = first.X - second.X;
        var deltaY = first.Y - second.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static PaddleOcrDetectionBox[] SortBoxes(PaddleOcrDetectionBox[] source)
    {
        var boxes = source
            .OrderBy(box => box.Points[0].Y)
            .ThenBy(box => box.Points[0].X)
            .ToArray();

        for (var i = 0; i < boxes.Length - 1; i++)
        {
            for (var j = i; j >= 0; j--)
            {
                if (Math.Abs(boxes[j + 1].Points[0].Y - boxes[j].Points[0].Y) < 10
                    && boxes[j + 1].Points[0].X < boxes[j].Points[0].X)
                {
                    (boxes[j], boxes[j + 1]) = (boxes[j + 1], boxes[j]);
                }
                else
                {
                    break;
                }
            }
        }

        return boxes;
    }

    public void Dispose()
    {
        lock (_inferenceLock)
        {
            if (_disposed) return;
            _disposed = true;
            try { _localDetModel.Dispose(); }
            finally { _localRecModel.Dispose(); }
        }
    }

    /// <summary>
    /// 返回(DetConfigName, RecConfigName)
    /// </summary>
    public (string, string) GetConfigName
    {
        get
        {
            return (this._localDetModel.GetConfigName, this._localRecModel.GetConfigName);
        }
    }
}
