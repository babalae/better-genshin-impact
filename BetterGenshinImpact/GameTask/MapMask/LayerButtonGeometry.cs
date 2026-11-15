using System;
using BetterGenshinImpact.Core.Recognition;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>
/// 读取右侧层级列当前选中的蓝色按钮，不能仅凭按钮列存在就判断地下。
/// 4K 样本的首层中心差约 149px、槽高约 102px；几何只提供相对深度先验。
/// </summary>
public static class LayerButtonGeometry
{
    /// <summary>1080P 下第一层与地表按钮中心的基准距离。</summary>
    private const double BaseDyAt1080P = 74.5;

    /// <summary>1080P 下相邻层的近似槽高。</summary>
    private const double SlotHeightAt1080P = 51;

    /// <summary>生产入口固定读取基准模板，避免 RecognitionAssets 缩放后再次缩放。</summary>
    public static LayerButtonSignal? Resolve(Mat colorMat)
    {
        if (colorMat.Empty() || colorMat.Channels() != 3)
        {
            return null;
        }

        var ground = RecognitionAssets.Get("QuickTeleport", "MapUndergroundToGroundButton", 1920, 1080);
        var selected = RecognitionAssets.Get("QuickTeleport", "MapUndergroundSwitchButton", 1920, 1080);
        return ground.TemplateImageMat == null || selected.TemplateImageMat == null
            ? null
            : Resolve(colorMat, ground.TemplateImageMat, selected.TemplateImageMat);
    }

    /// <summary>使用 1080P 基准模板解析；地表模板允许灰度，选中层模板必须包含颜色。</summary>
    public static LayerButtonSignal? Resolve(Mat colorMat, Mat toGroundTemplate, Mat switchTemplate)
    {
        if (colorMat.Empty() || colorMat.Channels() != 3 || toGroundTemplate.Empty() ||
            switchTemplate.Empty() || switchTemplate.Channels() != 3)
        {
            return null;
        }

        var scale = Math.Min(colorMat.Width / 1920d, colorMat.Height / 1080d);
        if (scale <= 0)
        {
            return null;
        }

        using var ground = new Mat();
        using var selected = new Mat();
        Cv2.Resize(toGroundTemplate, ground, new Size(Math.Max(1, (int)Math.Round(toGroundTemplate.Width * scale)),
            Math.Max(1, (int)Math.Round(toGroundTemplate.Height * scale))), 0, 0, InterpolationFlags.Area);
        Cv2.Resize(switchTemplate, selected, new Size(Math.Max(1, (int)Math.Round(switchTemplate.Width * scale)),
            Math.Max(1, (int)Math.Round(switchTemplate.Height * scale))), 0, 0, InterpolationFlags.Area);

        // 弹出详情或标记面板时，整列楼层按钮移到面板左侧。
        var mapArea = BigMapUiLayout.GetMapArea(colorMat);
        var roi = new Rect(mapArea.Right - (int)Math.Round(160 * scale), (int)Math.Round(240 * scale),
            (int)Math.Round(160 * scale), colorMat.Height - (int)Math.Round(400 * scale))
            .Intersect(new Rect(0, 0, colorMat.Width, colorMat.Height));
        if (roi.Width < Math.Max(ground.Width, selected.Width) || roi.Height < Math.Max(ground.Height, selected.Height))
        {
            return null;
        }

        using var strip = new Mat(colorMat, roi);
        using var stripGray = new Mat();
        using var groundGray = new Mat();
        Cv2.CvtColor(strip, stripGray, ColorConversionCodes.BGR2GRAY);
        if (ground.Channels() == 1)
        {
            ground.CopyTo(groundGray);
        }
        else
        {
            Cv2.CvtColor(ground, groundGray, ColorConversionCodes.BGR2GRAY);
        }

        var (groundScore, groundLocation) = MatchBest(stripGray, groundGray);
        var groundHit = groundScore >= 0.8;
        var selectedLocation = MatchSelected(strip, selected,
            groundHit ? new Point2d(groundLocation.X + ground.Width / 2d, groundLocation.Y + ground.Height / 2d) : null,
            scale);
        if (selectedLocation == null)
        {
            // 地表选中时没有蓝色地下菱形；不能把灰色的可切换按钮视为当前地下层。
            return groundHit ? new LayerButtonSignal(false, 0) : null;
        }

        if (!groundHit)
        {
            return new LayerButtonSignal(true, 0);
        }

        var dy = (selectedLocation.Value.Y + selected.Height / 2d - groundLocation.Y - ground.Height / 2d) / scale;
        var depth = 1 + (dy - BaseDyAt1080P) / SlotHeightAt1080P;
        var rounded = (int)Math.Round(depth);
        return new LayerButtonSignal(true,
            rounded is >= 1 and <= 8 && Math.Abs(depth - rounded) <= 0.35 ? rounded : 0);
    }

    /// <summary>逐个验证候选峰值的选中颜色，避免相关度略高的灰色按钮遮蔽真正的蓝色按钮。</summary>
    private static Point? MatchSelected(Mat strip, Mat template, Point2d? groundCenter, double scale)
    {
        // 只匹配上方发光菱形；下方叠层阴影及右下徽记随区域变化，会拉低整块模板的相关度。
        using var diamond = new Mat(template, new Rect(0, 0, template.Width, Math.Max(1, template.Height * 5 / 8)));
        using var result = new Mat();
        Cv2.MatchTemplate(strip, diamond, result, TemplateMatchModes.CCoeffNormed);
        for (var i = 0; i < 8; i++)
        {
            Cv2.MinMaxLoc(result, out _, out var score, out _, out var location);
            if (score < 0.72)
            {
                return null;
            }

            // 菱形还可能出现在地图地形中，候选必须与地表按钮处于同一列及合法的层槽。
            var aligned = true;
            if (groundCenter is { } center)
            {
                var dy = (location.Y + template.Height / 2d - center.Y) / scale;
                var depth = 1 + (dy - BaseDyAt1080P) / SlotHeightAt1080P;
                aligned = Math.Abs(location.X + template.Width / 2d - center.X) <= 10 * scale &&
                          depth >= 0.65 && depth <= 8.35 && Math.Abs(depth - Math.Round(depth)) <= 0.35;
            }
            if (aligned && HasSelectedColor(strip, diamond, location))
            {
                return location;
            }

            // 同一灰色按钮周围的相近峰只算一个候选，继续查找其它层按钮。
            var suppression = new Rect(location.X - template.Width / 2, location.Y - template.Height / 2,
                template.Width, template.Height).Intersect(new Rect(0, 0, result.Width, result.Height));
            using var rejected = new Mat(result, suppression);
            rejected.SetTo(Scalar.All(-1));
        }

        return null;
    }

    /// <summary>颜色相关度还会命中灰色同形图标，额外验证模板蓝色区域的实际颜色。</summary>
    private static bool HasSelectedColor(Mat strip, Mat template, Point location)
    {
        var expected = 0;
        var selected = 0;
        for (var y = 0; y < template.Height; y++)
        {
            for (var x = 0; x < template.Width; x++)
            {
                var pixel = template.At<Vec3b>(y, x);
                if (pixel.Item0 <= pixel.Item2 + 30 || pixel.Item1 <= pixel.Item2 + 20)
                {
                    continue;
                }

                expected++;
                var actual = strip.At<Vec3b>(location.Y + y, location.X + x);
                if (actual.Item0 > actual.Item2 + 30 && actual.Item1 > actual.Item2 + 20)
                {
                    selected++;
                }
            }
        }

        return expected > 0 && selected >= expected * 0.6;
    }

    /// <summary>归一化零均值模板匹配，返回最大值及左上角位置。</summary>
    private static (double Score, Point Location) MatchBest(Mat image, Mat template)
    {
        using var result = new Mat();
        Cv2.MatchTemplate(image, template, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out var score, out _, out var location);
        return (score, location);
    }
}

/// <summary>当前选中的层按钮；深度是 UI 中的相对序号，不能直接等同于所有素材的 Floor。</summary>
/// <param name="IsUnderground">是否选中了非地表按钮。</param>
/// <param name="DepthFromSurface">地表按钮下方的选中序号，0 表示地表或未知深度。</param>
public sealed record LayerButtonSignal(bool IsUnderground, int DepthFromSurface);
