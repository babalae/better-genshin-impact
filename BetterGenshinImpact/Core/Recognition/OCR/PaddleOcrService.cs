using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Drawing;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Online;
using Sdcb.PaddleOCR.Models.Details;
using System.Threading;
using BetterGenshinImpact.Core.Config;
using Sdcb.PaddleInference;
using Vanara.PInvoke;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Collections.Generic;

namespace BetterGenshinImpact.Core.Recognition.OCR;

public class PaddleOcrService : IOcrService
{
    /// <summary>
    /// Usage:
    /// https://github.com/sdcb/PaddleSharp/blob/master/docs/ocr.md
    /// 模型列表:
    /// https://github.com/PaddlePaddle/PaddleOCR/blob/release/2.5/doc/doc_ch/models_list.md
    /// </summary>
    private PaddleOcrAll? _paddleOcrAll;

    //public PaddleOcrService()
    //{
    //    //     public static OnlineDetectionModel ChineseV4 => new OnlineDetectionModel("ch_PP-OCRv4_det", new Uri("https://paddleocr.bj.bcebos.com/PP-OCRv4/chinese/ch_PP-OCRv4_det_infer.tar"), ModelVersion.V4);
    //    var path = Global.Absolute("Model\\PaddleOcr");
    //    FileDetectionModel localDetModel = new FileDetectionModel(path, ModelVersion.V4);
    //    FileClassificationModel localClsModel = new FileClassificationModel(path, ModelVersion.V2);
    //    RecognizationModel localRecModel = (RecognizationModel)new StreamDictFileRecognizationModel(this.RootDirectory, (IReadOnlyList<string>)SharedUtils.LoadDicts(this.DictName), this.Version);
    //    FullOcrModel model = new FullOcrModel(localDetModel, localClsModel, localRecModel);
    //}

    public string Ocr(Bitmap bitmap)
    {
        throw new NotImplementedException();
    }

    public string Ocr(Mat mat)
    {
        if (_paddleOcrAll == null)
        {
            var model = OnlineFullModels.ChineseV4.DownloadAsync().GetAwaiter().GetResult();
            _paddleOcrAll = new PaddleOcrAll(model, PaddleDevice.Onnx())
            {
                AllowRotateDetection = false, /* 允许识别有角度的文字 */
                Enable180Classification = false, /* 允许识别旋转角度大于90度的文字 */
            };
        }

        var stopwatch = new Stopwatch();
        stopwatch.Start();
        var result = _paddleOcrAll.Run(mat);
        stopwatch.Stop();
        Debug.WriteLine($"PaddleOcr 耗时 {stopwatch.ElapsedMilliseconds}ms 结果: {result.Text}");
        return result.Text;
    }
}