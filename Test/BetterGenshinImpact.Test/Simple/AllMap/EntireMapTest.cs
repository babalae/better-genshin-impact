using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.Helpers;
using BetterGenshinImpact.Helpers.Extensions;
using OpenCvSharp;
using OpenCvSharp.Detail;
using System.Diagnostics;
using System.Windows.Forms;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Common.Map;

namespace BetterGenshinImpact.Test.Simple.AllMap;

public class EntireMapTest
{
    public static void Test()
    {
        SpeedTimer speedTimer = new();
        var mainMap1024BlockMat = new Mat(@"E:\HuiTask\更好的原神\地图匹配\有用的素材\mainMap1024Block.png", ImreadModes.Grayscale);
        var surfMatcher = new FeatureMatcher(mainMap1024BlockMat);
        var queryMat = new Mat(@"E:\HuiTask\更好的原神\地图匹配\比较\小地图\Clip_20240323_183119.png", ImreadModes.Grayscale);

        speedTimer.Record("初始化特征");

        var p = surfMatcher.Match(queryMat);
        speedTimer.Record("匹配1");
        if (!p.IsEmpty())
        {
            Debug.WriteLine($"Matched rect 1: {p}");
            Cv2.Circle(mainMap1024BlockMat, p.ToPoint(), 10, Scalar.Red);
            // Cv2.ImWrite(@"E:\HuiTask\更好的原神\地图匹配\b1.png", mainMap1024BlockMat);

            var p2 = surfMatcher.Match(queryMat, p.X, p.Y);
            speedTimer.Record("匹配2");
            if (!p2.IsEmpty())
            {
                // var rect2 = Cv2.BoundingRect(pArray2);
                Debug.WriteLine($"Matched rect 2: {p2}");
                // Cv2.Rectangle(mainMap1024BlockMat, rect2, Scalar.Yellow, 1);
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

    public static void Storage()
    {
        var featureMatcher = new FeatureMatcher(new Mat(@"E:\HuiTask\更好的原神\地图匹配\有用的素材\5.2\map_52_2048.png", ImreadModes.Grayscale), new FeatureStorage("mainMap2048Block"));
        MessageBox.Show("特征点生成完成");
    }
    
    public static void Storage256()
    {
        FeatureMatcher _featureMatcher = new( new Mat(Global.Absolute(@"Assets\Map\mainMap256Block.png"), ImreadModes.Grayscale), new FeatureStorage("mainMap256Block"));
        MessageBox.Show("256特征点生成完成");
    }
}
