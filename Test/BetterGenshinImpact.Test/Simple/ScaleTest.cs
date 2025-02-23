using OpenCvSharp;

namespace BetterGenshinImpact.Test.Simple;

public class ScaleTest
{
    public static void ZoomOutTest()
    {
        var mainMap2048BlockMat = new Mat(@"E:\HuiTask\更好的原神\地图匹配\有用的素材\5.0\mainMap2048Block.png", ImreadModes.Color);
        // 缩小 2048/512 = 4
        var targetFilePath = @"E:\HuiTask\更好的原神\地图匹配\有用的素材\5.0\mainMap1024BlockColor.png";
        // opencv 缩小
        var mainMap256BlockMat = mainMap2048BlockMat.Resize(new Size(mainMap2048BlockMat.Width / 2, mainMap2048BlockMat.Height / 2));
        mainMap256BlockMat.SaveImage(targetFilePath);
    }
}
