using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Helpers.Extensions;
using System.Globalization;
using System.Windows;

namespace BetterGenshinImpact.View.Windows;

public partial class OcrDialog
{
    private readonly double xRatio;
    private readonly double yRatio;
    private readonly double widthRatio;
    private readonly double heightRatio;
    private readonly string? javaScript;
    public OcrDialog(double xRatio, double yRatio, double widthRatio, double heightRatio, string title, string? javaScript = null)
    {
        this.xRatio = xRatio;
        this.yRatio = yRatio;
        this.widthRatio = widthRatio;
        this.heightRatio = heightRatio;
        this.javaScript = javaScript;

        InitializeComponent();

        MyTitleBar.Title = title;
        Capture();
    }

    public void Capture()
    {
        using var ra = TaskControl.CaptureToRectArea();
        using var card = ra.DeriveCrop(new OpenCvSharp.Rect((int)(ra.Width * xRatio), (int)(ra.Height * yRatio), (int)(ra.Width * widthRatio), (int)(ra.Height * heightRatio)));
        var bitmapImage = card.SrcMat.ToWriteableBitmap();

        this.Screenshot.Source = bitmapImage;

        ArtifactStat artifact = AutoArtifactSalvageTask.GetArtifactStat(card.SrcMat, OcrFactory.Paddle, new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName), out string allText);
        this.TxtRecognized.Text = allText;
        if (this.javaScript != null)
        {
            bool isMatch = AutoArtifactSalvageTask.IsMatchJavaScript(artifact, this.javaScript);
            this.RegexResult.Text = isMatch ? "匹配" : "不匹配";
        }
        this.UpdateLayout();
    }

    private void BtnOkClick(object sender, RoutedEventArgs e)
    {
        Capture();
    }

    private void BtnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
