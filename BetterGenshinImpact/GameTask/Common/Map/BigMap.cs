using System.Diagnostics;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Helpers;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using OpenCvSharp;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;

namespace BetterGenshinImpact.GameTask.Common.Map;

public class BigMap
{
    public static readonly Size TemplateSize = new(240, 135);

    // 对无用部分进行裁剪（左160，上80，下96）
    public static readonly Rect TemplateSizeRoi = new Rect(20, 10, TemplateSize.Width - 20, TemplateSize.Height - 22);

    private readonly Mat _mapSrcMat;

    public BigMap()
    {
        var stream = ResourceHelper.GetStream(@"pack://application:,,,/Assets/Map/map_sd1024.png");
        _mapSrcMat = Mat.FromStream(stream, ImreadModes.Color);
    }

    public Point GetMapPosition(Mat captureMat)
    {
        Cv2.CvtColor(captureMat, captureMat, ColorConversionCodes.BGRA2BGR);
        using var tar = new Mat(captureMat.Resize(TemplateSize, 0, 0, InterpolationFlags.Cubic), TemplateSizeRoi);
        var p = MatchTemplateHelper.MatchTemplate(_mapSrcMat, tar, TemplateMatchModes.CCoeffNormed, null, 0.2);
        Debug.WriteLine($"BigMap Match Template: {p}");
        return p;
    }

    public void GetMapPositionAndDraw(Mat captureMat)
    {
        var p = GetMapPosition(captureMat);
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "UpdateBigMapRect", new object(),
            new System.Windows.Rect(p.X, p.Y, TemplateSizeRoi.Width, TemplateSizeRoi.Height)));
    }
}
