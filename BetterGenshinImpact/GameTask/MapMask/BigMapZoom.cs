using System;
using BetterGenshinImpact.Core.Recognition;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>
/// L1 缩放组件：只读取缩放 UI，不依赖运行环境或发出游戏输入；
/// 用已验证视口校准比例，为 L3 提供当前帧的几何约束。
/// </summary>
internal sealed class BigMapZoom(bool useTeyvatPrior = true)
{
    /// <summary>从任意分辨率灰度截图读取缩放滑块，未知状态不提供虚构的比例。</summary>
    internal static BigMapZoomSignal? Read(Mat gray)
    {
        if (gray.Empty() || gray.Type() != MatType.CV_8UC1)
        {
            return null;
        }

        var scale = Math.Min(gray.Width / 1920d, gray.Height / 1080d);
        var template = RecognitionAssets.Get("QuickTeleport", "MapScaleButton", 1920, 1080).TemplateImageGreyMat;
        if (template == null || template.Empty())
        {
            return null;
        }

        using var resized = new Mat();
        Cv2.Resize(template, resized, new Size(Math.Max(1, (int)Math.Round(template.Width * scale)),
            Math.Max(1, (int)Math.Round(template.Height * scale))), 0, 0, InterpolationFlags.Area);
        var roi = new Rect((int)(25 * scale), (int)(430 * scale), (int)(45 * scale), (int)(220 * scale))
            .Intersect(new Rect(0, 0, gray.Width, gray.Height));
        if (roi.Width < resized.Width || roi.Height < resized.Height)
        {
            return null;
        }

        using var strip = new Mat(gray, roi);
        using var scores = new Mat();
        Cv2.MatchTemplate(strip, resized, scores, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(scores, out _, out var score, out _, out var location);
        var centerY = (roi.Y + location.Y + resized.Height / 2d) / scale;
        if (!double.IsFinite(score) || score < 0.68 || centerY < 463 || centerY > 617)
        {
            return null;
        }

        // 1080P 滑块中心从 468 到 612，对应现有传送模块的 1～6 等级；上下加减按钮不在行程内。
        return new BigMapZoomSignal(Math.Clamp((centerY - 468) / 144, 0, 1), score);
    }

    /// <summary>一级缩放时每 1080P UI 像素的地图像素数，只由通过校验的视口更新。</summary>
    private double? _unitsPerLevel;

    /// <summary>独立地图最近一个实测缩放位置，不能假定与提瓦特共享滑块范围。</summary>
    private double? _observedPosition;

    /// <summary>实测位置对应的原生地图像素与 1080P UI 像素比例。</summary>
    private double _observedUnits;

    /// <summary>由两个充分分离的实测位置得到的比例变化率。</summary>
    private double? _observedSlope;

    /// <summary>将 L1 的 1～6 线性等级转成地图比例，冷启动宽容差，成功定位后实测校准。</summary>
    internal BigMapScalePrior? Predict(BigMapZoomSignal? zoom, double assetScale)
    {
        if (zoom == null || !double.IsFinite(assetScale) || assetScale <= 0)
        {
            return null;
        }

        // 独立地图滑块范围各不相同：未校准时让多锚点几何自行定位，不能错误套用大陆比例。
        if (!useTeyvatPrior)
        {
            if (_observedPosition is not { } observed) return null;
            var delta = zoom.Position - observed;
            if (_observedSlope == null && Math.Abs(delta) > 0.015) return null;
            var observedUnits = _observedUnits + (_observedSlope ?? 0) * delta;
            return observedUnits > 0 ? new BigMapScalePrior(observedUnits / assetScale, 0.15) : null;
        }

        // 一级缩放的默认系数由真实最大放大视口确认，近、中、远样本均验证其范围。
        // 此估计只筛除不可能的锚点假设，坐标仍必须通过多点几何验证，不能直接输出。
        var units = _unitsPerLevel ?? 64d / 75;
        return new BigMapScalePrior(units * (1 + 5 * zoom.Position) / assetScale, _unitsPerLevel == null ? 0.2 : 0.15);
    }

    /// <summary>只由通过定位校验的结果更新，失败帧不能污染比例。</summary>
    internal void Observe(BigMapZoomSignal? zoom, double unitsPerPixel)
    {
        if (zoom == null || !double.IsFinite(unitsPerPixel) || unitsPerPixel <= 0)
        {
            return;
        }

        // 提瓦特只需校准线性等级；独立地图单独学习滑块位置与比例的关系。
        if (useTeyvatPrior)
        {
            _unitsPerLevel = unitsPerPixel / (1 + 5 * zoom.Position);
            return;
        }

        if (_observedPosition is { } position && Math.Abs(zoom.Position - position) >= 0.03)
        {
            var slope = (unitsPerPixel - _observedUnits) / (zoom.Position - position);
            if (slope > 0 && double.IsFinite(slope)) _observedSlope = slope;
        }
        // 未取得斜率时保留首个读数，慢速连续缩放也能累计到足够的校准间距。
        if (_observedPosition == null || _observedSlope != null)
        {
            _observedPosition = zoom.Position;
            _observedUnits = unitsPerPixel;
        }
    }

    /// <summary>重开地图时清除会话校准，避免分辨率或其它地图场景沿用旧比例。</summary>
    internal void Reset()
    {
        _unitsPerLevel = null;
        _observedPosition = null;
        _observedUnits = 0;
        _observedSlope = null;
    }
}

/// <summary>缩放条的实测位置；Position 从地图最大时的 0 增加到地图最小时的 1。</summary>
/// <param name="Position">去除分辨率影响后的缩放条位置。</param>
/// <param name="Confidence">滑块模板的相关度。</param>
public sealed record BigMapZoomSignal(double Position, double Confidence);

/// <summary>缩放 UI 提供的比例先验；冷启动保留宽容差，实测校准后收紧。</summary>
/// <param name="Scale">每个当前截图像素对应的 2048 级地图像素数。</param>
/// <param name="Tolerance">比例允许的相对偏差。</param>
internal readonly record struct BigMapScalePrior(double Scale, double Tolerance);
