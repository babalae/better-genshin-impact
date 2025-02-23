using System.Diagnostics;
using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.Core.Recognition.ONNX.SVTR;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using OpenCvSharp;

namespace BetterGenshinImpact.Test.Simple;

public class OcrTest
{
    public static void TestYap()
    {
        Mat mat = Cv2.ImRead(@"E:\HuiTask\更好的原神\临时文件\fuben_jueyuan.png", ImreadModes.Grayscale);
        var text = TextInferenceFactory.Pick.Inference(PreProcessForInference(mat));
        Debug.WriteLine(text);

        Mat mat2 = Cv2.ImRead(@"E:\HuiTask\更好的原神\临时文件\fuben_jueyuan.png", ImreadModes.Grayscale);
        var text2 = OcrFactory.Paddle.Ocr(mat2);
        Debug.WriteLine(text2);
    }

    private static Mat PreProcessForInference(Mat mat)
    {
        // Yap 已经改用灰度图了 https://github.com/Alex-Beng/Yap/commit/c2ad1e7b1442aaf2d80782a032e00876cd1c6c84
        // 二值化
        // Cv2.Threshold(mat, mat, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);
        //Cv2.AdaptiveThreshold(mat, mat, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 31, 3); // 效果不错 但是和模型不搭
        //mat = OpenCvCommonHelper.Threshold(mat, Scalar.FromRgb(235, 235, 235), Scalar.FromRgb(255, 255, 255)); // 识别物品不太行
        // 不知道为什么要强制拉伸到 221x32
        mat = ResizeHelper.ResizeTo(mat, 221, 32);
        // 填充到 384x32
        var padded = new Mat(new Size(384, 32), MatType.CV_8UC1, Scalar.Black);
        padded[new Rect(0, 0, mat.Width, mat.Height)] = mat;
        //Cv2.ImWrite(Global.Absolute("padded.png"), padded);
        return padded;
    }
}
