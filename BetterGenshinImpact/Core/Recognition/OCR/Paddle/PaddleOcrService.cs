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
using OpenCvSharp;
using OpenCvSharp.Extensions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using Size = OpenCvSharp.Size;

namespace BetterGenshinImpact.Core.Recognition.OCR.Paddle;

public class PaddleOcrService : IOcrService, IOcrMatchService, IDisposable
{
    /// <summary>
    ///     Usage:
    ///     https://github.com/sdcb/PaddleSharp/blob/master/docs/ocr.md
    ///     模型列表:
    ///     https://github.com/PaddlePaddle/PaddleOCR/blob/release/2.5/doc/doc_ch/models_list.md
    /// </summary>
    private readonly Det _localDetModel;

    private readonly Rec _localRecModel;

    public record PaddleOcrModelType(
        BgiOnnxModel DetectionModel,
        OcrVersionConfig DetectionVersion,
        BgiOnnxModel RecognitionModel,
        OcrVersionConfig RecognitionVersion,
        Func<IReadOnlyList<string>> RecLabel,
        String PreHeatImagePath
    )
    {
        public static string TestImagePath = Global.Absolute(@"Assets\Model\PaddleOCR\test_pp_ocr.png");

        public static string TestNumberImagePath =
            Global.Absolute(@"Assets\Model\PaddleOCR\test_pp_ocr_number.png");

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


        private static PaddleOcrModelType Create(
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
                TestImagePath);
        }

        public (Det, Rec) Build(BgiOnnxFactory onnxFactory, bool allowDuplicateChar = false, float threshold = 0.5f)
        {
            return (
                new Det(DetectionModel, DetectionVersion, onnxFactory),
                new Rec(RecognitionModel, RecLabel(), RecognitionVersion, onnxFactory, allowDuplicateChar, threshold: threshold));
        }

        public static readonly PaddleOcrModelType V4 = Create(
            BgiOnnxModel.PaddleOcrDetV4,
            OcrVersionConfig.PpOcrV4,
            BgiOnnxModel.PaddleOcrRecV4,
            OcrVersionConfig.PpOcrV4);

        public static readonly PaddleOcrModelType V4En = Create(
            BgiOnnxModel.PaddleOcrDetV4,
            OcrVersionConfig.PpOcrV4,
            BgiOnnxModel.PaddleOcrRecV4En,
            OcrVersionConfig.PpOcrV4,
            TestNumberImagePath);

        public static readonly PaddleOcrModelType V5 = Create(
            BgiOnnxModel.PaddleOcrDetV5,
            OcrVersionConfig.PpOcrV5,
            BgiOnnxModel.PaddleOcrRecV5,
            OcrVersionConfig.PpOcrV5);

        public static readonly PaddleOcrModelType V5Latin = Create(
            BgiOnnxModel.PaddleOcrDetV5,
            OcrVersionConfig.PpOcrV5,
            BgiOnnxModel.PaddleOcrRecV5Latin,
            OcrVersionConfig.PpOcrV5);

        public static readonly PaddleOcrModelType V5Eslav = Create(
            BgiOnnxModel.PaddleOcrDetV5,
            OcrVersionConfig.PpOcrV5,
            BgiOnnxModel.PaddleOcrRecV5Eslav,
            OcrVersionConfig.PpOcrV5);

        public static readonly PaddleOcrModelType V5Korean = Create(
            BgiOnnxModel.PaddleOcrDetV5,
            OcrVersionConfig.PpOcrV5,
            BgiOnnxModel.PaddleOcrRecV5Korean,
            OcrVersionConfig.PpOcrV5);

        /// <summary>
        /// 这边多语言部分写的比较丑陋，但是能跑。可以根据PP-OCR的语言列表来优化。
        /// </summary>
        /// <param name="cultureInfo"></param>
        /// <returns></returns>
        public static PaddleOcrModelType? FromCultureInfo(CultureInfo cultureInfo)
        {
            HashSet<string> eslavLangs = new(StringComparer.OrdinalIgnoreCase)
                { "ru", "be", "uk" };
            HashSet<string> latinLangs = new(StringComparer.OrdinalIgnoreCase)
            {
                "af", "az", "bs", "cs", "cy", "da", "de", "es", "et", "fr", "ga", "hr", "hu", "id", "is", "it", "ku",
                "la",
                "lt", "lv", "mi", "ms", "mt", "nl", "no", "oc", "pi", "pl", "pt", "ro", "rs_latin", "sk", "sl", "sq",
                "sv",
                "sw", "tl", "tr", "uz", "vi", "french", "german"
            };
            HashSet<string> ocrV5Langs = new(StringComparer.OrdinalIgnoreCase)
                { "zh", "chi", "zho", "en", "japan", "jp" };
            // HashSet<string> SPECIAL_LANGS = new(StringComparer.OrdinalIgnoreCase)
            //     { "ch", "chinese_cht", "en", "japan", "korean" };

            List<string> names =
            [
                cultureInfo.EnglishName.ToLowerInvariant(), cultureInfo.Name.ToLowerInvariant(),
                cultureInfo.ThreeLetterISOLanguageName.ToLowerInvariant(),
                cultureInfo.TwoLetterISOLanguageName.ToLowerInvariant()
            ];
            foreach (var name in names)
            {
                if (name.Equals("korean") || name.Equals("ko"))
                {
                    return V5Korean;
                }

                if (eslavLangs.Contains(name))
                {
                    return V5Eslav;
                }

                if (latinLangs.Contains(name))
                {
                    return V5Latin;
                }

                if (ocrV5Langs.Contains(name))
                {
                    return V5;
                }
            }

            return null;
        }

        /// <summary>
        /// 中英文优先使用V4模型，其他语言使用V5模型
        /// </summary>
        /// <param name="cultureInfo"></param>
        /// <returns></returns>
        public static PaddleOcrModelType? FromCultureInfoV4(CultureInfo cultureInfo)
        {
            var v5 = FromCultureInfo(cultureInfo);
            // 如果用的是v5, 那么优先用V4的细分模型
            if (v5 == V5)
            {
                List<string> names =
                [
                    cultureInfo.EnglishName.ToLowerInvariant(), cultureInfo.Name.ToLowerInvariant(),
                    cultureInfo.ThreeLetterISOLanguageName.ToLowerInvariant(),
                    cultureInfo.TwoLetterISOLanguageName.ToLowerInvariant()
                ];
                foreach (var name in names)
                {
                    if (name.Equals("en"))
                    {
                        return V4En;
                    }
                    else if (name.Equals("zh-hant") || name.Equals("zh-tw") || name.Equals("zh-hk"))
                    {
                        return V5;
                    }
                }

                return V4;
            }
            else
            {
                return v5;
            }
        }
    }

    public PaddleOcrService(BgiOnnxFactory bgiOnnxFactory, PaddleOcrModelType modelType,
        bool allowDuplicateChar = false, float threshold = 0.5f)
    {
        var (modelsDet, modelsRec) = modelType.Build(bgiOnnxFactory, allowDuplicateChar, threshold);
        _localDetModel = modelsDet;
        _localRecModel = modelsRec;

        // 预热模型
        using var preHeatImageMat = Bv.ImRead(modelType.PreHeatImagePath) ??
                                    throw new FileNotFoundException($"预热图片未找到: {modelType.PreHeatImagePath}");
        // Debug输出结果
        var preHeatResult = RunAll(preHeatImageMat, 1);
        Debug.WriteLine(
            $"PaddleOcrService 预热完成，使用模型: {modelType.DetectionModel.Name} 和 {modelType.RecognitionModel.Name}，结果: {preHeatResult.Text}");
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
        using var converted = ConvertBgrIfNeeded(mat);
        return _OcrResult(converted ?? mat);
    }

    /// <summary>
    ///     推荐传入三通道BGR mat，虽然四通道和单通道也做了兼容，但是三通道最快
    /// </summary>
    public string OcrWithoutDetector(Mat mat)
    {
        var startTime = Stopwatch.GetTimestamp();
        var str = _localRecModel.Run(mat).Text;
        var time = Stopwatch.GetElapsedTime(startTime);
        Debug.WriteLine($"PaddleOcrWithoutDetector 耗时 {time.TotalMilliseconds}ms 结果: {str}");
        return str;
    }

    private OcrResult _OcrResult(Mat mat)
    {
        var startTime = Stopwatch.GetTimestamp();
        var result = RunAll(mat);
        var time = Stopwatch.GetElapsedTime(startTime);
        Debug.WriteLine($"PaddleOcr 耗时 {time.TotalMilliseconds}ms 结果: {result.Text}");
        return result;
    }

    /// <summary>
    ///     推荐传入三通道BGR mat，虽然四通道和单通道也做了兼容，但是三通道最快
    /// </summary>
    private OcrResult RunAll(Mat src, int recognizeBatchSize = 0)
    {
        var rects = _localDetModel.Run(src);
        Mat[] mats =
            rects.Select(rect =>
                {
                    var roi = src[GetCropedRect(rect.BoundingRect(), src.Size())];
                    return roi;
                })
                .ToArray();
        try
        {
            return new OcrResult(_localRecModel.Run(mats, recognizeBatchSize)
                .Select((result, i) => new OcrResultRegion(rects[i], result.Text, result.Score))
                .ToArray());
        }
        finally
        {
            foreach (var mat in mats) mat.Dispose();
        }
    }

    /// <summary>
    ///     Gets the cropped region of the source image specified by the given rectangle, clamping the rectangle coordinates to
    ///     the image bounds.
    /// </summary>
    /// <param name="rect">The rectangle to crop.</param>
    /// <param name="size">The size of the source image.</param>
    /// <returns>The cropped rectangle.</returns>
    private static Rect GetCropedRect(Rect rect, Size size)
    {
        return Rect.FromLTRB(
            Math.Clamp(rect.Left, 0, size.Width),
            Math.Clamp(rect.Top, 0, size.Height),
            Math.Clamp(rect.Right, 0, size.Width),
            Math.Clamp(rect.Bottom, 0, size.Height));
    }

    /// <summary>
    /// 若输入为 BGRA 则转换为 BGR，否则返回 null。
    /// 调用方需在使用后 Dispose 返回的 Mat（若非 null）。
    /// </summary>
    private static Mat? ConvertBgrIfNeeded(Mat mat)
    {
        return mat.Channels() == 4 ? mat.CvtColor(ColorConversionCodes.BGRA2BGR) : null;
    }

    /// <summary>
    /// 使用检测器定位文字区域后，对每个区域进行 DP 模糊匹配，返回最高置信度 (0~1)。
    /// </summary>
    public double OcrMatch(Mat mat, string target)
    {
        var startTime = Stopwatch.GetTimestamp();

        using var src = ConvertBgrIfNeeded(mat);
        var bgr = src ?? mat;

        var rects = _localDetModel.Run(bgr);
        Mat[] mats = rects.Select(rect =>
        {
            var roi = bgr[GetCropedRect(rect.BoundingRect(), bgr.Size())];
            return roi;
        }).ToArray();

        try
        {
            var score = _localRecModel.RunMatch(mats, target);
            var time = Stopwatch.GetElapsedTime(startTime);
            Debug.WriteLine($"PaddleOcrMatch 耗时 {time.TotalMilliseconds}ms 目标: {target} 分数: {score:F4}");
            return score;
        }
        finally
        {
            foreach (var m in mats) m.Dispose();
        }
    }

    /// <summary>
    /// 不使用检测器，直接对整张图像进行 DP 模糊匹配，返回置信度 (0~1)。
    /// </summary>
    public double OcrMatchDirect(Mat mat, string target)
    {
        var startTime = Stopwatch.GetTimestamp();

        using var src = ConvertBgrIfNeeded(mat);
        var bgr = src ?? mat;

        var score = _localRecModel.RunMatch([bgr], target);
        var time = Stopwatch.GetElapsedTime(startTime);
        Debug.WriteLine($"PaddleOcrMatchDirect 耗时 {time.TotalMilliseconds}ms 目标: {target} 分数: {score:F4}");
        return score;
    }

    public void Dispose()
    {
        _localDetModel.Dispose();
        _localRecModel.Dispose();
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