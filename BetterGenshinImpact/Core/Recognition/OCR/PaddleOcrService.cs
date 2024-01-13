using BetterGenshinImpact.Core.Config;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using SharpDX;
using System;
using System.Diagnostics;
using System.Drawing;
using Path = System.IO.Path;

namespace BetterGenshinImpact.Core.Recognition.OCR;

public class PaddleOcrService : IOcrService
{
    /// <summary>
    /// Usage:
    /// https://github.com/sdcb/PaddleSharp/blob/master/docs/ocr.md
    /// 模型列表:
    /// https://github.com/PaddlePaddle/PaddleOCR/blob/release/2.5/doc/doc_ch/models_list.md
    /// </summary>
    private readonly PaddleOcrAll _paddleOcrAll;

    private static readonly object locker = new();

    public PaddleOcrService()
    {
        var path = Global.Absolute("Assets\\Model\\PaddleOcr");
        var localDetModel = DetectionModel.FromDirectory(Path.Combine(path, "ch_PP-OCRv4_det"), ModelVersion.V4);
        var localClsModel = ClassificationModel.FromDirectory(Path.Combine(path, "ch_ppocr_mobile_v2.0_cls"));
        var localRecModel = RecognizationModel.FromDirectory(Path.Combine(path, "ch_PP-OCRv4_rec"), Path.Combine(path, "ppocr_keys_v1.txt"), ModelVersion.V4);
        var model = new FullOcrModel(localDetModel, localClsModel, localRecModel);
        _paddleOcrAll = new PaddleOcrAll(model, PaddleDevice.Onnx())
        {
            AllowRotateDetection = false, /* 允许识别有角度的文字 */
            Enable180Classification = false, /* 允许识别旋转角度大于90度的文字 */
        };

        // System.AccessViolationException
        // https://github.com/babalae/better-genshin-impact/releases/latest
        // 下载并解压到相同目录下
    }

    public string Ocr(Mat mat)
    {
        return OcrResult(mat).Text;
    }

    public PaddleOcrResult OcrResult(Mat mat)
    {
        lock (locker)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var result = _paddleOcrAll.Run(mat);
            stopwatch.Stop();
            Debug.WriteLine($"PaddleOcr 耗时 {stopwatch.ElapsedMilliseconds}ms 结果: {result.Text}");
            return result;
        }
    }

    public string OcrWithoutDetector(Mat mat)
    {
        lock (locker)
        {
            var str = _paddleOcrAll.Recognizer.Run(mat).Text;
            Debug.WriteLine($"PaddleOcrWithoutDetector 结果: {str}");
            return str;
        }
    }
}