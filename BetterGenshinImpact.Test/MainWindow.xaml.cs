using System.Diagnostics;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Test.Dataset;
using BetterGenshinImpact.Test.Simple;
using BetterGenshinImpact.Test.Simple.AllMap;
using BetterGenshinImpact.Test.Simple.Track;
using BetterGenshinImpact.Test.View;
using System.Windows;
using BetterGenshinImpact.GameTask.Common.Map;
using OpenCvSharp;
using Window = System.Windows.Window;

namespace BetterGenshinImpact.Test;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Global.StartUpPath = @"D:\HuiPrograming\Projects\CSharp\MiHoYo\BetterGenshinImpact\BetterGenshinImpact\bin\x64\Debug\net8.0-windows10.0.22621.0";
    }

    private void ShowCameraRecWindow(object sender, System.Windows.RoutedEventArgs e)
    {
        new CameraRecWindow().Show();
    }

    private void ShowHsvTestWindow(object sender, System.Windows.RoutedEventArgs e)
    {
        new HsvTestWindow().Run();
    }

    private void DoMapPuzzle(object sender, System.Windows.RoutedEventArgs e)
    {
        MapPuzzle.PutAll();
    }

    private void DoOcrTest(object sender, System.Windows.RoutedEventArgs e)
    {
        OcrTest.TestYap();
    }

    private void DoMatchTemplateTest(object sender, System.Windows.RoutedEventArgs e)
    {
        MatchTemplateTest.Test();
    }

    private void DoMatchTest(object sender, System.Windows.RoutedEventArgs e)
    {
        // KeyPointMatchTest.Test();
        // EntireMapTest.Test();
        // EntireMapTest.Storage();
        // BigMapMatchTest.Test();

        // FeatureTransfer.Transfer();

        // var extractor = new LargeSiftExtractor();
        // extractor.ExtractAndSaveSift(@"E:\HuiTask\更好的原神\地图匹配\有用的素材\5.2\map_52_2048.png", @"E:\HuiTask\更好的原神\地图匹配\有用的素材\5.2\");

        EntireMapTest.Storage256();
    }

    private void MapDrawTeleportPoint(object sender, RoutedEventArgs e)
    {
        MapTeleportPointDraw.Draw();
    }

    private void GenAvatarData(object sender, RoutedEventArgs e)
    {
        AvatarClassifyGen.GenAll();
    }

    private void AutoCookTestCase(object sender, RoutedEventArgs e)
    {
        AutoCookTest.Test();
    }

    private void MapPathView(object sender, RoutedEventArgs e)
    {
        MapPathTest.Test();
    }

    private void ZoomOut(object sender, RoutedEventArgs e)
    {
        ScaleTest.ZoomOutTest();
    }

    private void GenAvatarDataT(object sender, RoutedEventArgs e)
    {
        AvatarClassifyTransparentGen.GenAll();
    }

    private void CameraTest(object sender, RoutedEventArgs e)
    {
        var path = @"E:\HuiTask\更好的原神\地图匹配\比较\小地图\Clip_20240323_185854.png";
        
        var pic1 = new Mat(path);
        CameraOrientationFromLimint cameraOrientation = new();
        var f = cameraOrientation.PredictRotation(pic1);
        Debug.WriteLine("C#版本 方向1:" + f);
        
                
        // var pic2 = new Mat(path);
        // CameraOrientationV2 cameraOrientation3 = new();
        // var f3 = cameraOrientation3.PredictRotation(pic2);
        // Debug.WriteLine("py直接翻译C#版本 方向1:" + f3);

        
        var grey = new Mat(path, ImreadModes.Grayscale);
        var f2 = CameraOrientation.ComputeMiniMap(grey);
        Debug.WriteLine("老版本方向2:" + f2);
    }
}