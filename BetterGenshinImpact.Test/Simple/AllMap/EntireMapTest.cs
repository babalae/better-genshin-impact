using System.Diagnostics;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;

namespace BetterGenshinImpact.Test.Simple.AllMap;

public class EntireMapTest
{
    public static void Test()
    {
        SpeedTimer speedTimer = new();
        var mainMap1024BlockMat = new Mat(@"E:\HuiTask\更好的原神\地图匹配\有用的素材\mainMap1024Block.png", ImreadModes.Grayscale);
        var surfMatcher = new SurfMatcher(mainMap1024BlockMat);
        var queryMat = new Mat(@"E:\HuiTask\更好的原神\地图匹配\比较\小地图\Clip_20240323_183119.png", ImreadModes.Grayscale);

        speedTimer.Record("初始化特征");

        var pArray = surfMatcher.Match(queryMat);
        speedTimer.Record("匹配1");
        if (pArray != null)
        {
            var rect = Cv2.BoundingRect(pArray);
            Debug.WriteLine($"Matched rect 1: {rect}");
            Cv2.Rectangle(mainMap1024BlockMat, rect, Scalar.Red, 2);
            // Cv2.ImWrite(@"E:\HuiTask\更好的原神\地图匹配\b1.png", mainMap1024BlockMat);

            var pArray2 = surfMatcher.Match(queryMat, rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            speedTimer.Record("匹配2");
            if (pArray2 != null)
            {
                var rect2 = Cv2.BoundingRect(pArray2);
                Debug.WriteLine($"Matched rect 2: {rect2}");
                Cv2.Rectangle(mainMap1024BlockMat, rect2, Scalar.Yellow, 1);
                // Cv2.ImWrite(@"E:\HuiTask\更好的原神\地图匹配\b2.png", mainMap1024BlockMat);
            }
            else
            {
                Debug.WriteLine("No match 2");
            }
        }
        else
        {
            Debug.WriteLine("No match 1");
        }
        speedTimer.DebugPrint();
    }
}
