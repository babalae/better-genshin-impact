using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Helpers.Extensions;
using BetterGenshinImpact.Helpers.Ui;
using BetterGenshinImpact.ViewModel.Pages;
using OpenCvSharp;
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Windows;

public partial class ArtifactOcrDialog
{
    private readonly double xRatio;
    private readonly double yRatio;
    private readonly double widthRatio;
    private readonly double heightRatio;
    private readonly string? javaScript;
    private readonly AutoArtifactSalvageTask autoArtifactSalvageTask;
    public static ILogger Logger { get; } = App.GetLogger<ArtifactOcrDialog>();

    public ArtifactOcrDialog(double xRatio, double yRatio, double widthRatio, double heightRatio, string title, string? javaScript = null)
    {
        this.xRatio = xRatio;
        this.yRatio = yRatio;
        this.widthRatio = widthRatio;
        this.heightRatio = heightRatio;
        this.javaScript = javaScript;
        this.autoArtifactSalvageTask = new AutoArtifactSalvageTask(new AutoArtifactSalvageTaskParam(5, null, null, null, null, new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName)));

        InitializeComponent();
        SourceInitialized += (s, e) => WindowHelper.TryApplySystemBackdrop(this);

        MyTitleBar.Title = title;

        _ = CaptureAsync();
    }

    public async Task CaptureAsync()
    {
        // 没启动时候，启动截图器
        var homePageViewModel = App.GetService<HomePageViewModel>();
        if (!homePageViewModel!.TaskDispatcherEnabled) { await homePageViewModel.OnStartTriggerAsync(); }

        using var ra = TaskControl.CaptureToRectArea();
        using var card = ra.DeriveCrop(new OpenCvSharp.Rect((int)(ra.Width * xRatio), (int)(ra.Height * yRatio), (int)(ra.Width * widthRatio), (int)(ra.Height * heightRatio)));
        var bitmapImage = card.SrcMat.ToWriteableBitmap();

        this.Screenshot.Source = bitmapImage;
        this.InvalidateVisual();
        this.UpdateLayout();

        try
        {
            ArtifactStat artifact = this.autoArtifactSalvageTask.GetArtifactStat(card.SrcMat, OcrFactory.Paddle, out string allText);

            this.TxtRecognized.Text = allText;
            this.ModelStructure.Text = artifact.ToStructuredString();
            if (this.javaScript != null)
            {
                bool isMatch = Task.Run(() => AutoArtifactSalvageTask.IsMatchJavaScript(artifact, this.javaScript)).Result;
                this.RegexResult.Text = isMatch ? "匹配" : "不匹配";
            }
        }
        catch (Exception e)
        {
            await HandleOcrExceptionAsync(e, card.SrcMat);
        }
    }

    private static async Task HandleOcrExceptionAsync(Exception e, Mat srcMat)
    {
        Logger.LogError(e, "自动分解圣遗物-OCR识别异常");
        var result = ThemedMessageBox.Error(
            $"{e.Message}\n\n是否保存该圣遗物截图？（至log/autoArtifactSalvageException/）",
            "异常处理",
            MessageBoxButton.YesNo,
            MessageBoxResult.No
        );

        if (result == MessageBoxResult.Yes)
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "log/autoArtifactSalvageException");
            Directory.CreateDirectory(directory);
            string filePath = Path.Combine(directory, $"{DateTime.Now:yyyyMMddHHmmss}_GetArtifactStat.png");
            Cv2.ImWrite(filePath, srcMat);
        }
        await Task.CompletedTask;
    }

    private async void BtnOkClick(object sender, RoutedEventArgs e)
    {
        _ = CaptureAsync();
    }

    private void BtnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
