using OpenCvSharp;

namespace BetterGenshinImpact.Test.Simple;

internal class AutoCookTest
{
    public static void Test()
    {
        var img = new Mat(@"E:\HuiTask\更好的原神\自动烹饪\2.png");
        Cv2.CvtColor(img, img, ColorConversionCodes.BGR2RGB);
        var img2 = new Mat();
        // Cv2.InRange(img, new Scalar(255, 192, 64), new Scalar(255, 192, 64), img2);
        Cv2.InRange(img, new Scalar(255, 255, 192), new Scalar(255, 255, 192), img2);

        Cv2.ImShow("img", img2);
    }
}
