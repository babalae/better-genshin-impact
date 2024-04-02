using BetterGenshinImpact.Core.Recognition.OpenCv;
using OpenCvSharp;

namespace BetterGenshinImpact.Test.Simple.AllMap;

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

    public static void TestMenu()
    {
        var tar = new Mat(@"D:\HuiPrograming\Projects\CSharp\MiHoYo\BetterGenshinImpact\BetterGenshinImpact\GameTask\Common\Element\Assets\1920x1080\paimon_menu.png", ImreadModes.Color);
        var src = new Mat(@"E:\HuiTask\更好的原神\地图匹配\比较\菜单\Clip_20240331_155210.png", ImreadModes.Color);
        var src2 = src.Clone();
        var p = MatchTemplateHelper.MatchTemplate(src, tar, TemplateMatchModes.CCoeffNormed, null, 0.1);
        Cv2.Rectangle(src2, new Rect(p.X, p.Y, tar.Width, tar.Height), new Scalar(0, 0, 255), 1);
        // 画出小地图的位置
        // 此图38x40 小地图210x210 小地图左上角位置 24,-15
        Cv2.Rectangle(src2, new Rect(p.X + 24, p.Y - 15, 210, 210), Scalar.Blue, 1);
        Cv2.ImWrite(@"E:\HuiTask\更好的原神\地图匹配\比较\菜单\rec2.png", src2);
    }
}
