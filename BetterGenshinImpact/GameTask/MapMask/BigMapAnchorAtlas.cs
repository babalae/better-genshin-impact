using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>保存经过地形定位确认的锚点组，为离线传送点表尚未覆盖的地区补充本地参考。</summary>
internal sealed class BigMapAnchorAtlas
{
    /// <summary>限制参考组数，避免全局点对索引随长期使用无限增长。</summary>
    private const int MaxFrames = 16;

    /// <summary>地图坐标边界，用于拒绝无效缓存数据。</summary>
    private readonly Size _mapSize;

    /// <summary>可选持久化位置；不指定时只在本实例中学习，便于离线测试隔离。</summary>
    private readonly string? _path;

    /// <summary>按最近采集顺序保存的已确认锚点组，各组单独验证几何，避免把重复观察算成独立锚点。</summary>
    private readonly LinkedList<ReferenceFrame> _frames = new();

    /// <summary>缓存读写失败仅记录诊断，不中断地图识别。</summary>
    private readonly ILogger<BigMapAnchorAtlas> _logger = App.GetLogger<BigMapAnchorAtlas>();

    /// <summary>读取本地已确认参考；缓存缺失或损坏时从空表开始。</summary>
    internal BigMapAnchorAtlas(Size mapSize, string? path = null)
    {
        _mapSize = mapSize;
        _path = path;
        if (path == null || !File.Exists(path))
        {
            return;
        }
        try
        {
            var frames = JsonConvert.DeserializeObject<List<float[][]>>(File.ReadAllText(path));
            foreach (var frame in frames?.Take(MaxFrames) ?? [])
            {
                Add(frame.Where(p => p is { Length: 2 }).Select(p => new Point2f(p[0], p[1])), false);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogDebug(e, "本地地图锚点缓存不可用，重新采集");
        }
    }

    /// <summary>接收已通过地形几何及楼层外观核验的锚点坐标；相同视口不反复写盘。</summary>
    internal void Observe(IEnumerable<Point2f> points) => Add(points, true);

    /// <summary>独立匹配参考组，并拒绝支持数相近但坐标不同的竞争解。</summary>
    internal BigMapFeatureMatch? Match(Point2f[] points, Size size, BigMapScalePrior? prior, CancellationToken cancellationToken = default)
    {
        var matches = _frames.Select(f => f.Geometry.Match(points, size, prior?.Scale, prior?.Tolerance ?? 0.15, cancellationToken))
            .Where(m => m != null).Select(m => m!).OrderByDescending(m => m.Confidence).ToArray();
        if (matches.Length == 0)
        {
            return null;
        }
        var best = matches[0];
        var scale = best.Viewport.Width / size.Width;
        var ambiguous = matches.Skip(1).Any(m => m.Confidence >= best.Confidence - 1d / points.Length &&
            (Math.Abs(m.Viewport.Width / best.Viewport.Width - 1) > 0.02 ||
             Math.Abs(m.Viewport.X + m.Viewport.Width / 2 - best.Viewport.X - best.Viewport.Width / 2) > 5 * scale ||
             Math.Abs(m.Viewport.Y + m.Viewport.Height / 2 - best.Viewport.Y - best.Viewport.Height / 2) > 5 * scale));
        return ambiguous ? null : best;
    }

    /// <summary>校验坐标并去掉重复观察，每组最多 32 个独立图标，与检测上限一致。</summary>
    private void Add(IEnumerable<Point2f> points, bool save)
    {
        var valid = points.Where(p => float.IsFinite(p.X) && float.IsFinite(p.Y) &&
            p.X >= 0 && p.X < _mapSize.Width && p.Y >= 0 && p.Y < _mapSize.Height).Distinct().Take(32).ToArray();
        if (valid.Length < 4 || _frames.Any(f => valid.Count(p => f.Points.Any(q => p.DistanceTo(q) < 12)) >= valid.Length * 0.8))
        {
            return;
        }
        _frames.AddFirst(new ReferenceFrame(valid, new BigMapAnchorGeometry(valid)));
        if (_frames.Count > MaxFrames)
        {
            _frames.RemoveLast();
        }
        if (!save || _path == null)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(
                _frames.Select(f => f.Points.Select(p => new[] { p.X, p.Y })), Formatting.None));
            File.Move(temporaryPath, _path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(e, "本地地图锚点缓存写入失败，继续使用内存参考");
        }
    }

    /// <summary>一次已确认地形视口中的独立锚点及其几何索引。</summary>
    /// <param name="Points">2048 级地图像素坐标。</param>
    /// <param name="Geometry">只针对这一组独立锚点的几何验证器。</param>
    private sealed record ReferenceFrame(Point2f[] Points, BigMapAnchorGeometry Geometry);
}
