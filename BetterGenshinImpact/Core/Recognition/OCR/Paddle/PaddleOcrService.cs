using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR.Engine;
using BetterGenshinImpact.Core.Recognition.ONNX;
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

        public (Det, Rec) Build(BgiOnnxFactory onnxFactory)
        {
            return (
                new Det(DetectionModel, DetectionVersion, onnxFactory),
                new Rec(RecognitionModel, RecLabel(), RecognitionVersion, onnxFactory));
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

    public PaddleOcrService(BgiOnnxFactory bgiOnnxFactory, PaddleOcrModelType modelType)
    {
        var (modelsDet, modelsRec) = modelType.Build(bgiOnnxFactory);
        _localDetModel = modelsDet;
        _localRecModel = modelsRec;

        // 预热模型
        using var preHeatImageMat = Cv2.ImRead(modelType.PreHeatImagePath) ??
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
        if (mat.Channels() == 4)
        {
            using var mat3 = mat.CvtColor(ColorConversionCodes.BGRA2BGR);
            return _OcrResult(mat3);
        }

        return _OcrResult(mat);
    }

    /// <summary>
    ///     推荐传入三通道BGR mat，虽然四通道和单通道也做了兼容，但是三通道最快
    /// </summary>
    public string OcrWithoutDetector(Mat mat)
    {
        var str = _localRecModel.Run(mat).Text;
        Debug.WriteLine($"PaddleOcrWithoutDetector 结果: {str}");
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

    public void Dispose()
    {
        _localDetModel.Dispose();
        _localRecModel.Dispose();
    }
}