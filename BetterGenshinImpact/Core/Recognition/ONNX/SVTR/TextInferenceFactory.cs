using System;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using OpenCvSharp;

namespace BetterGenshinImpact.Core.Recognition.ONNX.SVTR;

public class TextInferenceFactory
{
    public static ITextInference Pick { get; } = Create(OcrEngineTypes.YapModel);

    public static ITextInference Create(OcrEngineTypes type)
    {
        return type switch
        {
            OcrEngineTypes.YapModel => new PickTextInference(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
    // public static Mat PreProcessForInference(Mat mat)
    // {
    //     if (mat.Channels() == 3)
    //     {
    //         mat = mat.CvtColor(ColorConversionCodes.BGR2GRAY);
    //     }
    //     else if (mat.Channels() == 4)
    //     {
    //         mat = mat.CvtColor(ColorConversionCodes.BGRA2GRAY);
    //     }
    //     else if (mat.Channels() != 1)
    //     {
    //         throw new ArgumentException("mat must be 1, 3 or 4 channels");
    //     }
    //
    //     // Yap 已经改用灰度图了 https://github.com/Alex-Beng/Yap/commit/c2ad1e7b1442aaf2d80782a032e00876cd1c6c84
    //     // 二值化
    //     // Cv2.Threshold(mat, mat, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);
    //     //Cv2.AdaptiveThreshold(mat, mat, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 31, 3); // 效果不错 但是和模型不搭
    //     //mat = OpenCvCommonHelper.Threshold(mat, Scalar.FromRgb(235, 235, 235), Scalar.FromRgb(255, 255, 255)); // 识别物品不太行
    //     // 不知道为什么要强制拉伸到 221x32
    //     mat = ResizeHelper.ResizeTo(mat, 221, 32);
    //     // 填充到 384x32
    //     var padded = new Mat(new Size(384, 32), MatType.CV_8UC1, Scalar.Black);
    //     padded[new Rect(0, 0, mat.Width, mat.Height)] = mat;
    //     //Cv2.ImWrite(Global.Absolute("padded.png"), padded);
    //     return padded;
    // }
}
