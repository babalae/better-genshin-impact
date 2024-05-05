using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Recognition.OpenCv;
using BetterGenshinImpact.Core.Recognition.OpenCv.FeatureMatch;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using OpenCvSharp;
using System;
using System.Diagnostics;
using BetterGenshinImpact.Model;
using Point = OpenCvSharp.Point;
using Size = OpenCvSharp.Size;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.AutoTrackPath;

namespace BetterGenshinImpact.GameTask.Common.Map;

public class EntireMap : Singleton<EntireMap>
{
    // 这个模板缩放大小的计算方式 https://github.com/babalae/better-genshin-impact/issues/318
    public static readonly Size TemplateSize = new(240, 135);

    // 对无用部分进行裁剪（左160，上80，下96）
    public static readonly Rect TemplateSizeRoi = new Rect(20, 10, TemplateSize.Width - 20, TemplateSize.Height - 22);

    /// <summary>
    /// 主要地图缩小1024的模板
    /// </summary>
    private readonly Mat _mainMap100BlockMat;

    // /// <summary>
    // /// 1024区块拼接的主要地图
    // /// </summary>
    // private readonly Mat _mainMap1024BlockMat;
    //
    // /// <summary>
    // /// 2048城市区块拼接的主要地图
    // /// </summary>
    // private readonly Mat _cityMap2048BlockMat;

    private readonly FeatureMatcher _featureMatcher;

    private int _prevX = -1;
    private int _prevY = -1;

    public EntireMap()
    {
        // 大地图模板匹配使用的模板
        _mainMap100BlockMat = MapAssets.Instance.MainMap100BlockMat.Value;
        // _mainMap1024BlockMat = MapAssets.Instance.MainMap1024BlockMat.Value;
        // _cityMap2048BlockMat = new Mat(@"E:\HuiTask\更好的原神\地图匹配\有用的素材\cityMap2048Block.png", ImreadModes.Grayscale);
        // Mat grey = new();
        // Cv2.CvtColor(_mainMap100BlockMat, grey, ColorConversionCodes.BGR2GRAY);
        _featureMatcher = new FeatureMatcher(MapAssets.Instance.MainMap1024BlockMat.Value, new FeatureStorage("mainMap1024BlockMat"));
    }

    /// <summary>
    /// 基于模板匹配获取地图位置(100区块，缩小了10.24倍)
    /// 当前只支持大地图
    /// </summary>
    /// <param name="captureMat">彩色图像</param>
    /// <returns></returns>
    public Point GetMapPositionByMatchTemplate(Mat captureMat)
    {
        Cv2.CvtColor(captureMat, captureMat, ColorConversionCodes.BGRA2BGR);
        using var tar = new Mat(captureMat.Resize(TemplateSize, 0, 0, InterpolationFlags.Cubic), TemplateSizeRoi);
        var p = MatchTemplateHelper.MatchTemplate(_mainMap100BlockMat, tar, TemplateMatchModes.CCoeffNormed, null, 0.2);
        Debug.WriteLine($"BigMap Match Template: {p}");
        return p;
    }

    public void GetMapPositionAndDrawByMatchTemplate(Mat captureMat)
    {
        var p = GetMapPositionByMatchTemplate(captureMat);
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "UpdateBigMapRect", new object(),
            new System.Windows.Rect(p.X, p.Y, TemplateSizeRoi.Width, TemplateSizeRoi.Height)));
    }

    /// <summary>
    /// 基于特征匹配获取地图位置(1024区块)
    /// 支持大地图和小地图
    /// </summary>
    /// <param name="greyMat">灰度图</param>
    /// <returns></returns>
    public Rect GetMapPositionByFeatureMatch(Mat greyMat)
    {
        try
        {
            Point2f[]? pArray;
            if (_prevX != -1 && _prevY != -1)
            {
                pArray = _featureMatcher.Match(greyMat, _prevX, _prevY);
            }
            else
            {
                pArray = _featureMatcher.Match(greyMat);
            }

            if (pArray == null || pArray.Length < 4)
            {
                throw new InvalidOperationException();
            }
            var rect = Cv2.BoundingRect(pArray);
            _prevX = rect.X + rect.Width / 2;
            _prevY = rect.Y + rect.Height / 2;
            return rect;
        }
        catch
        {
            (_prevX, _prevY) = (-1, -1);
            Debug.WriteLine("Feature Match Failed");
            return Rect.Empty;
        }
    }

    // public static Point GetIntersection(Point2f[] points)
    // {
    //     double a1 = (points[0].Y - points[2].Y) / (double)(points[0].X - points[2].X);
    //     double b1 = points[0].Y - a1 * points[0].X;
    //
    //     double a2 = (points[1].Y - points[3].Y) / (double)(points[1].X - points[3].X);
    //     double b2 = points[1].Y - a2 * points[1].X;
    //
    //     if (Math.Abs(a1 - a2) < double.Epsilon)
    //     {
    //         // 不相交
    //         throw new InvalidOperationException();
    //     }
    //
    //     double x = (b2 - b1) / (a1 - a2);
    //     double y = a1 * x + b1;
    //     return new Point((int)x, (int)y);
    // }

    public void GetMapPositionAndDrawByFeatureMatch(Mat captureGreyMat)
    {
        var rect = GetMapPositionByFeatureMatch(captureGreyMat);
        if (rect != Rect.Empty)
        {
            WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<object>(this, "UpdateBigMapRect", new object(),
                new System.Windows.Rect(rect.X / 10.24, rect.Y / 10.24, rect.Width / 10.24, rect.Height / 10.24)));
        }
    }
}
