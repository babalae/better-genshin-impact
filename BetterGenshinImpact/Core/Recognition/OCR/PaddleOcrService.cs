using System;
using System.Drawing;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;

namespace BetterGenshinImpact.Core.Recognition.OCR;

public class PaddleOcrService : IOcrService
{
    /// <summary>
    /// Usage:
    /// https://github.com/sdcb/PaddleSharp/blob/master/docs/ocr.md
    /// 模型列表:
    /// https://github.com/PaddlePaddle/PaddleOCR/blob/release/2.5/doc/doc_ch/models_list.md
    /// </summary>
    private readonly PaddleOcrAll _paddleOcrAll = new(LocalFullModels.ChineseV4, PaddleDevice.Mkldnn())
    {
        AllowRotateDetection = true, /* 允许识别有角度的文字 */
        Enable180Classification = false, /* 允许识别旋转角度大于90度的文字 */
    };

    public string Ocr(Bitmap bitmap)
    {
        throw new NotImplementedException();
    }

    public string Ocr(Mat mat)
    {
        var result = _paddleOcrAll.Run(mat);
        Console.WriteLine("PaddleOcr结果: " + result.Text);
        return result.Text;
    }
}