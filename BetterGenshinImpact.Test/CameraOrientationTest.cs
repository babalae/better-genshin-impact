using OpenCvSharp;

namespace BetterGenshinImpact.Test;

public class CameraOrientationTest
{
    public static void Test1()
    {
        var mat = Cv2.ImRead(@"E:\HuiTask\更好的原神\自动秘境\视角朝向识别\2.png", ImreadModes.Color);
        // 极坐标展开
        var centerPoint = new Point2f(mat.Width / 2f, mat.Height / 2f);
        var polarMat = new Mat();
        Cv2.WarpPolar(mat, polarMat, new Size(360, 360), centerPoint, 360d, InterpolationFlags.Linear, WarpPolarMode.Linear);
        Cv2.ImShow("polarMat", polarMat);
        var polarRoiMat = new Mat(polarMat, new Rect(20, 0, 70, polarMat.Height));
        Cv2.Rotate(polarRoiMat, polarRoiMat, RotateFlags.Rotate90Counterclockwise);
        Cv2.ImShow("极坐标转换后", polarRoiMat);
        // Cv2.ImWrite(@"E:\HuiTask\更好的原神\自动秘境\视角朝向识别\1_p.png", polarRoiMat);

        Cv2.Scharr(polarRoiMat, polarRoiMat, MatType.CV_8UC1, 1, 0);
        Cv2.ImShow("Scharr", polarRoiMat);
    }

    public static void Test2()
    {
        var mat = Cv2.ImRead(@"E:\HuiTask\更好的原神\自动秘境\视角朝向识别\1.png", ImreadModes.Color);

        // 极坐标展开
        var centerPoint = new Point2f(mat.Width / 2f, mat.Height / 2f);
        var polarMat = new Mat();
        Cv2.WarpPolar(mat, polarMat, new Size(360, 360), centerPoint, 360d, InterpolationFlags.Linear, WarpPolarMode.Linear);
        var polarRoiMat = new Mat(polarMat, new Rect(20, 0, 70, polarMat.Height));
        Cv2.Rotate(polarRoiMat, polarRoiMat, RotateFlags.Rotate90Counterclockwise);
        Cv2.CvtColor(polarRoiMat, polarRoiMat, ColorConversionCodes.BGR2Lab);
        Cv2.ImShow("极坐标转换后", polarRoiMat);

        var splitMat = polarRoiMat.Split();

        for (int i = 0; i < splitMat.Length; i++)
        {
            Cv2.ImShow($"splitMat{i}", splitMat[i]);
        }

        var andMat = new Mat();
        Cv2.Scharr(splitMat[0], andMat, MatType.CV_8UC1, 1, 0);
        Cv2.ImShow("Scharr", andMat);
    }
}