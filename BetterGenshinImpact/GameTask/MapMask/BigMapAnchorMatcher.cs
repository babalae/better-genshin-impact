using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BetterGenshinImpact.GameTask.Common.Element.Assets;
using BetterGenshinImpact.GameTask.Common.Map.Maps.Base;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>L3：识别传送锚点图标并对照离线传送点坐标定位，不依赖 SIFT 成功结果。</summary>
internal sealed class BigMapAnchorMatcher(SceneBaseMap map, string? cachePath = null) : IDisposable
{
    /// <summary>坐标转换和离线锚点所属的场景地图。</summary>
    private readonly SceneBaseMap _map = map;

    /// <summary>由已确认地形视口采集的本地锚点，与外部传送点资源保持分离。</summary>
    private readonly BigMapAnchorAtlas _atlas = new(map.MapSize, cachePath);

    /// <summary>每帧的实际检测数量和拒绝原因，不把缺少坐标误报成缺少图标。</summary>
    internal string LastDiagnostics { get; private set; } = string.Empty;

    /// <summary>离线传送点参考总数，诊断资源覆盖情况。</summary>
    private int _referenceCount;

    /// <summary>按需建立、跨帧复用的锚点几何索引。</summary>
    private BigMapAnchorGeometry? _geometry;

    /// <summary>本匹配器拥有的灰度图标模板。</summary>
    private Mat? _template;

    /// <summary>模板对应的 UI 缩放比例。</summary>
    private double _templateScale;

    /// <summary>匹配完整计算尺度截图；缺少四个锚点时交给后续 SIFT。</summary>
    internal BigMapFeatureMatch? Match(Mat gray, BigMapScalePrior? scalePrior, Rect? mapArea = null, CancellationToken cancellationToken = default)
    {
        var points = Detect(gray, mapArea, cancellationToken);
        LastDiagnostics = $"检测到 {points.Length} 个锚点";
        if (points.Length < 4)
        {
            LastDiagnostics += "，不足 4 个独立锚点";
            return null;
        }

        var learned = _atlas.Match(points, gray.Size(), scalePrior, cancellationToken);
        if (learned != null)
        {
            LastDiagnostics += $"，本地参考定位通过（{learned.Confidence:P0} 一致）";
            return learned;
        }
        if (_geometry == null)
        {
            var references = MapLazyAssets.Get().ScenesDic[_map.Type.ToString()].Points
                .Where(p => p.Type == "TeleportWaypoint")
                .Select(p => _map.ConvertGenshinMapCoordinatesToImageCoordinates(new Point2f((float)p.X, (float)p.Y))).ToArray();
            _referenceCount = references.Length;
            _geometry = new BigMapAnchorGeometry(references, cancellationToken);
        }
        var result = _geometry.Match(points, gray.Size(), scalePrior?.Scale, scalePrior?.Tolerance ?? 0.15, cancellationToken);
        LastDiagnostics += result == null ? $"，未找到无歧义的坐标对应（离线参考 {_referenceCount} 点）"
            : $"，离线参考定位通过（{result.Confidence:P0} 一致）";
        return result;
    }

    /// <summary>只从高置信度的地形定位采集参考，L3 自身或未确认的地下背景结果不能反向教学。</summary>
    internal void Observe(Mat gray, Rect mapArea, MapMatchResult result)
    {
        if (result.Via is not ("L4-SIFT" or "L5-LayerSIFT") || result.Floor == null || result.Confidence < 0.6)
        {
            return;
        }
        var points = Detect(gray, mapArea);
        var viewport = result.Viewport2048;
        _atlas.Observe(points.Select(p => new Point2f(
            (float)(viewport.X + p.X * viewport.Width / gray.Width),
            (float)(viewport.Y + p.Y * viewport.Height / gray.Height))));
    }

    /// <summary>读取独立传送图标中心；限定地图区域并抑制同一图标周围重复峰值。</summary>
    internal Point2f[] Detect(Mat gray, Rect? mapArea = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (gray.Empty() || gray.Type() != MatType.CV_8UC1)
        {
            return [];
        }

        var scale = Math.Min(gray.Width / 1920d, gray.Height / 1080d);
        if (_template == null || Math.Abs(_templateScale - scale) > 0.001)
        {
            using var original = GameTaskManager.LoadAssetImage("QuickTeleport", "TeleportWaypoint.png", 1920, 1080, ImreadModes.Grayscale);
            var resized = new Mat();
            Cv2.Resize(original, resized, new Size(Math.Max(1, (int)Math.Round(original.Width * scale)),
                Math.Max(1, (int)Math.Round(original.Height * scale))), 0, 0, InterpolationFlags.Area);
            _template?.Dispose();
            _template = resized;
            _templateScale = scale;
        }

        if (gray.Width < _template.Width || gray.Height < _template.Height)
        {
            return [];
        }

        var visible = mapArea ?? BigMapUiLayout.GetMapArea(gray);
        using var scores = new Mat();
        Cv2.MatchTemplate(gray, _template, scores, TemplateMatchModes.CCoeffNormed);
        var points = new List<Point2f>();
        for (var i = 0; i < 64 && points.Count < 32; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Cv2.MinMaxLoc(scores, out _, out var score, out _, out var location);
            if (!double.IsFinite(score) || score < 0.74)
            {
                break;
            }

            var center = new Point2f(location.X + _template.Width / 2f, location.Y + _template.Height / 2f);
            // UI 资源栏、任务栏、楼层列及 UID 不作为世界锚点。
            if (center.X > 140 * scale && center.X < visible.Right - 100 * scale &&
                center.Y > 90 * scale && center.Y < gray.Height - 80 * scale &&
                !(center.X < 360 * scale && center.Y < 440 * scale))
            {
                points.Add(center);
            }

            var suppression = new Rect(location.X - _template.Width / 2, location.Y - _template.Height / 2,
                _template.Width, _template.Height).Intersect(new Rect(0, 0, scores.Width, scores.Height));
            using var rejected = new Mat(scores, suppression);
            rejected.SetTo(Scalar.All(-1));
        }

        return points.ToArray();
    }

    /// <summary>只释放本实例拥有的模板，不修改外部资源包。</summary>
    public void Dispose() => _template?.Dispose();
}
