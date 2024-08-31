using OpenCvSharp;

namespace BetterGenshinImpact.Test.Simple;

public class ScaleTest
{
    public static void ZoomOutTest()
    {
        var mainMap2048BlockMat = new Mat(@"E:\HuiTask\更好的原神\地图匹配\有用的素材\5.0\mainMap2048Block.png", ImreadModes.Grayscale);
        // 缩小 2048/256 = 8 倍
        var targetFilePath = @"E:\HuiTask\更好的原神\地图匹配\有用的素材\5.0\mainMap256Block.png";
        // opencv 缩小
        var mainMap256BlockMat = mainMap2048BlockMat.Resize(new Size(mainMap2048BlockMat.Width / 8, mainMap2048BlockMat.Height / 8));
        mainMap256BlockMat.SaveImage(targetFilePath);
    }
}
