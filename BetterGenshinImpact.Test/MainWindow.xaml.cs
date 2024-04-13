using System.Windows;
using BetterGenshinImpact.GameTask.Common.Map;
using BetterGenshinImpact.Test.Dataset;
using BetterGenshinImpact.Test.Simple;
using BetterGenshinImpact.Test.Simple.AllMap;
using BetterGenshinImpact.Test.View;

namespace BetterGenshinImpact.Test;

/// <summary>
/// MainWindow.xaml 的交互逻辑
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
        MapPuzzle.Put();
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
        EntireMap entireMap = new();
    }

    private void MapDrawTeleportPoint(object sender, RoutedEventArgs e)
    {
        MapTeleportPointDraw.Draw();
    }

    private void GenAvatarData(object sender, RoutedEventArgs e)
    {
        AvatarClassifyGen.GenAll();
    }
}
