using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.ONNX.OCR.Paddle;
using OpenCvSharp;
using System;
using System.Diagnostics;

namespace BetterGenshinImpact.Core.Recognition.OCR;

public class PaddleOcrService : IOcrService
{
    private static readonly object locker = new();
    private PaddleOcrEngine _ocrEngine;

    public PaddleOcrService()
    {
        var detPath = Global.Absolute("Assets\\Model\\PaddleOCR\\V3\\ch_PP-OCRv3_det_infer.onnx");
        var clsPath = Global.Absolute("Assets\\Model\\PaddleOCR\\V3\\ch_ppocr_mobile_v2.0_cls_infer.onnx");
        var recPath = Global.Absolute("Assets\\Model\\PaddleOCR\\V3\\ch_PP-OCRv3_rec_infer.onnx");
        var keysPath = Global.Absolute("Assets\\Model\\PaddleOCR\\V3\\ppocr_keys_v1.txt");
        _ocrEngine = new PaddleOcrEngine();
        _ocrEngine.InitModels(detPath, clsPath, recPath, keysPath, Math.Min(4, Environment.ProcessorCount));
    }

    public string Ocr(Mat mat)
    {
        return OcrResult(mat).Text;
    }

    public string OcrWithoutDetector(Mat mat)
    {
        lock (locker)
        {
            var str = _ocrEngine.OnlyRecognizerRun(mat);
            Debug.WriteLine($"PaddleOcrWithoutDetector 结果: {str}");
            return str;
        }
    }

    public OcrResult OcrResult(Mat mat)
    {
        lock (locker)
        {
            long startTime = Stopwatch.GetTimestamp();
            var result = _ocrEngine.Run(mat).ToBgiOcrResult();
            TimeSpan time = Stopwatch.GetElapsedTime(startTime);
            Debug.WriteLine($"PaddleOcr 耗时 {time.TotalMilliseconds}ms 结果: {result}");
            return result;
        }
        throw new System.NotImplementedException();
    }
}
