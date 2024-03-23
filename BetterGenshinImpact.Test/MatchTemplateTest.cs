using BetterGenshinImpact.Core.Recognition.OpenCv;
using OpenCvSharp;

namespace BetterGenshinImpact.Test;

public class MatchTemplateTest
{
    public static readonly Size TemplateSize = new(240, 135);

    // 对无用部分进行裁剪（左160，上80，下96）
    public static readonly Rect TemplateSizeRoi = new Rect(20, 10, TemplateSize.Width - 20, TemplateSize.Height - 22);

    public static void Test()
    {
        var tar = new Mat(@"E:\HuiTask\更好的原神\地图匹配\比较\叠图\涂黑匹配2.png", ImreadModes.Color);
        var sTar = tar.Resize(new Size(240, 135), 0, 0, InterpolationFlags.Cubic);
        // sTar = new Mat(sTar, TemplateSizeRoi);
        Cv2.ImShow("sTar", sTar);
        var src = new Mat(@"E:\HuiTask\更好的原神\地图匹配\combined_image_small.png", ImreadModes.Color);
        var src2 = src.Clone();
        var p = MatchTemplateHelper.MatchTemplate(src, sTar, TemplateMatchModes.CCoeffNormed, null, 0.1);

        Cv2.Rectangle(src2, new Rect(p.X, p.Y, sTar.Width, sTar.Height), new Scalar(0, 0, 255));

        Cv2.ImWrite(@"E:\HuiTask\更好的原神\地图匹配\x1.png", src2);
    }
}
