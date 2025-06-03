using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OCR.engine;
using BetterGenshinImpact.Core.Recognition.OCR.paddle;
using BetterGenshinImpact.Core.Recognition.ONNX;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.OCR;

public class PaddleOcrService : IOcrService
{
    /// <summary>
    ///     Usage:
    ///     https://github.com/sdcb/PaddleSharp/blob/master/docs/ocr.md
    ///     模型列表:
    ///     https://github.com/PaddlePaddle/PaddleOCR/blob/release/2.5/doc/doc_ch/models_list.md
    /// </summary>
    private readonly Det _localDetModel;

    private readonly Rec _localRecModel;

    public PaddleOcrService(string cultureInfoName, BgiOnnxFactory bgiOnnxFactory)
    {
        var path = Global.Absolute(@"Assets\Model\PaddleOcr");

        switch (cultureInfoName)
        {
            case "zh-Hant":
                _localDetModel = new Det(BgiOnnxModel.PaddleOcrChDet, OcrVersionConfig.PpOcrV4, bgiOnnxFactory);
                _localRecModel = new Rec(BgiOnnxModel.PaddleOcrChtRec, Path.Combine(path, "chinese_cht_dict.txt"),
                    OcrVersionConfig.PpOcrV3, bgiOnnxFactory);
                break;
            case "fr":
                _localDetModel = new Det(BgiOnnxModel.PaddleOcrEnDet, OcrVersionConfig.PpOcrV3, bgiOnnxFactory);
                _localRecModel = new Rec(BgiOnnxModel.PaddleOcrLatinRec, Path.Combine(path, "latin_dict.txt"),
                    OcrVersionConfig.PpOcrV3, bgiOnnxFactory);
                break;
            default:
                _localDetModel = new Det(BgiOnnxModel.PaddleOcrChDet, OcrVersionConfig.PpOcrV4, bgiOnnxFactory);
                _localRecModel = new Rec(BgiOnnxModel.PaddleOcrChRec, Path.Combine(path, "ppocr_keys_v1.txt"),
                    OcrVersionConfig.PpOcrV4, bgiOnnxFactory);

                break;
        }
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
}