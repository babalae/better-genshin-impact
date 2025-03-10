using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Helpers;
using OpenCvSharp;
using OpenCvSharp.Detail;
using System.Diagnostics;
using System.Windows.Forms;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Helpers.Extensions;

namespace BetterGenshinImpact.Test.Simple.AllMap;

public class BigMapMatchTest
{
    public static void Test()
    {
        SpeedTimer speedTimer = new();
        // var mainMap100BlockMat = new Mat(@"D:\HuiPrograming\Projects\CSharp\MiHoYo\BetterGenshinImpact\BetterGenshinImpact\Assets\Map\mainMap100Block.png", ImreadModes.Grayscale);

        var map2048 =  new Mat(@"E:\HuiTask\更好的原神\地图匹配\有用的素材\5.2\map_52_2048.png", ImreadModes.Grayscale);
        var mainMap100BlockMat = ResizeHelper.Resize(map2048, 1d / (4 * 2));
        Cv2.ImWrite(@"E:\HuiTask\更好的原神\地图匹配\有用的素材\mainMap128Block.png", mainMap100BlockMat);

        var surfMatcher = new FeatureMatcher(mainMap100BlockMat);
        var queryMat = new Mat(@"E:\HuiTask\更好的原神\地图匹配\比较\Clip_20240321_000329.png", ImreadModes.Grayscale);

        speedTimer.Record("初始化特征");

        queryMat = ResizeHelper.Resize(queryMat, 1d / 4);
        Cv2.ImShow("queryMat", queryMat);

        var p = surfMatcher.Match(queryMat);
        speedTimer.Record("匹配1");
        if (p.IsEmpty())
        {
            // var rect = Cv2.BoundingRect(pArray);
            Debug.WriteLine($"Matched rect 1: {p}");
            // Cv2.Rectangle(mainMap100BlockMat, rect, Scalar.Red, 2);
            // Cv2.ImShow(@"b1", mainMap100BlockMat);
        }
        else
        {
            Debug.WriteLine("No match 1");
        }
        speedTimer.DebugPrint();
    }
}
