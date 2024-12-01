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
        CameraOrientationV3 cameraOrientation = new();
        cameraOrientation.PredictRotation(new Mat(@"E:\HuiTask\更好的原神\地图匹配\比较\小地图\Clip_20240323_183119.png"));
    }
}
