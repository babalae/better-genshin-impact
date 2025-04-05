using BetterGenshinImpact.Core.Recognition.OCR;
using BetterGenshinImpact.GameTask.AutoArtifactSalvage;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Model.Area;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace BetterGenshinImpact.View.Windows;

public partial class OcrDialog
{
    private readonly double xRatio;
    private readonly double yRatio;
    private readonly double widthRatio;
    private readonly double heightRatio;
    private readonly string? regularExpression;
    public OcrDialog(double xRatio, double yRatio, double widthRatio, double heightRatio, string title, string? regularExpression = null)
    {
        this.xRatio = xRatio;
        this.yRatio = yRatio;
        this.widthRatio = widthRatio;
        this.heightRatio = heightRatio;
        this.regularExpression = regularExpression;

        InitializeComponent();

        MyTitleBar.Title = title;
        Capture();
    }

    public void Capture()
    {
        using var ra = TaskControl.CaptureToRectArea();
        using ImageRegion card = ra.DeriveCrop(new OpenCvSharp.Rect((int)(ra.Width * xRatio), (int)(ra.Height * yRatio), (int)(ra.Width * widthRatio), (int)(ra.Height * heightRatio)));
        using Bitmap bitmap = card.SrcBitmap;
        using Stream stream = new MemoryStream();
        bitmap.Save(stream, format: System.Drawing.Imaging.ImageFormat.Png);
        BitmapImage bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = stream;
        bitmapImage.EndInit();

        this.Screenshot.Source = bitmapImage;

        this.TxtRecognized.Text = OcrFactory.Paddle.OcrResult(card.SrcMat).Text;
        if (this.regularExpression != null)
        {
            AutoArtifactSalvageTask.IsMatchRegularExpression(this.TxtRecognized.Text, this.regularExpression, out string msg);
            this.RegexResult.Text = msg;
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
