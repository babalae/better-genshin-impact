using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.Helpers.Extensions;
using OpenCvSharp;
using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace BetterGenshinImpact.View.Windows;

public partial class OcrDialog
{
    private readonly double xRatio;
    private readonly double yRatio;
    private readonly double widthRatio;
    private readonly double heightRatio;
    private readonly string? javaScript;
    private readonly AutoArtifactSalvageTask autoArtifactSalvageTask;

    public OcrDialog(double xRatio, double yRatio, double widthRatio, double heightRatio, string title, string? javaScript = null)
    {
        this.xRatio = xRatio;
        this.yRatio = yRatio;
        this.widthRatio = widthRatio;
        this.heightRatio = heightRatio;
        this.javaScript = javaScript;
        this.autoArtifactSalvageTask = new AutoArtifactSalvageTask(new AutoArtifactSalvageTaskParam(5, null, null, null, null, new CultureInfo(TaskContext.Instance().Config.OtherConfig.GameCultureInfoName)));

        InitializeComponent();

        MyTitleBar.Title = title;
        Capture();
    }

    public void Capture()
    {
        using var ra = TaskControl.CaptureToRectArea();
        using var card = ra.DeriveCrop(new OpenCvSharp.Rect((int)(ra.Width * xRatio), (int)(ra.Height * yRatio), (int)(ra.Width * widthRatio), (int)(ra.Height * heightRatio)));
        //Cv2.ImWrite($"{DateTime.Now.ToString("yyyyMMddHHmm")}_GetArtifactStat.png", card.SrcMat);
        var bitmapImage = card.SrcMat.ToWriteableBitmap();

        this.Screenshot.Source = bitmapImage;

        try
        {
            ArtifactStat artifact = this.autoArtifactSalvageTask.GetArtifactStat(card.SrcMat, OcrFactory.Paddle, out string allText);

            this.TxtRecognized.Text = allText;
            this.ModelStructure.Text = artifact.ToStructuredString();
            if (this.javaScript != null)
            {
                bool isMatch = AutoArtifactSalvageTask.IsMatchJavaScript(artifact, this.javaScript);
                this.RegexResult.Text = isMatch ? "匹配" : "不匹配";
            }
        }
        catch (Exception e)
        {
            var multilineTextBox = new TextBox
            {
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = e.ToString(),
                IsReadOnly = true
            };
            var p = new PromptDialog($"出错了：{e.Message}\r\n\r\n是否保存该圣遗物截图？（至log/autoArtifactSalvageException/）", $"异常处理", multilineTextBox, null);
            p.Height = 600;
            p.MaxWidth = 800;
            p.ShowDialog();

            if (p.DialogResult == true)
            {
                string directory = Path.Combine(AppContext.BaseDirectory, "log/autoArtifactSalvageException");
                Directory.CreateDirectory(directory);
                string filePath = Path.Combine(directory, $"{DateTime.Now.ToString("yyyyMMddHHmmss")}_GetArtifactStat.png");
                Cv2.ImWrite(filePath, card.SrcMat);
            }

            throw;
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
