using System;
using System.Linq;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>用已定位地图的相邻帧光流更新视口，使拖动和缩放无需等待全局搜索。</summary>
internal sealed class BigMapMotionTracker : IDisposable
{
    /// <summary>保护捕获线程跟踪与后台匹配结果的校准。</summary>
    private readonly object _sync = new();

    /// <summary>最近一次有效跟踪的缩略图，仅保留 640 像素长边。</summary>
    private Mat? _reference;

    /// <summary>参考图对应的精确地图视口，逐帧计算不取整以免缓慢拖动积累误差。</summary>
    private Rect2d? _viewport;

    /// <summary>捕获线程已观察到的修订，旧的全局结果不能反向覆盖新画面。</summary>
    private long _revision = -1;

    /// <summary>最近截图尺寸，窗口大小变化后须重新进行绝对定位。</summary>
    private Size _inputSize;

    /// <summary>复制低分辨率灰度图，调用方仍拥有完整截图。</summary>
    private static Mat Thumbnail(Mat gray)
    {
        var small = new Mat();
        var scale = Math.Min(1, 640d / gray.Width);
        Cv2.Resize(gray, small, new Size((int)Math.Round(gray.Width * scale),
            (int)Math.Round(gray.Height * scale)), 0, 0, InterpolationFlags.Area);
        return small;
    }

    /// <summary>绝对定位成功后校准；仅接受捕获线程当前修订对应的图像。</summary>
    internal bool SetReference(Mat gray, Rect2d viewport, long revision)
    {
        using var small = Thumbnail(gray);
        lock (_sync)
        {
            if (_revision != revision || _inputSize != gray.Size())
            {
                return false;
            }
            _reference?.Dispose();
            _reference = small.Clone();
            _viewport = viewport;
            return true;
        }
    }

    /// <summary>验证双向光流及多点一致性；失败时放弃相对跟踪，交回绝对定位。</summary>
    internal bool TryTrack(Mat gray, long revision, out Rect2d viewport)
    {
        viewport = default;
        using var current = Thumbnail(gray);
        lock (_sync)
        {
            _revision = revision;
            if (_inputSize != gray.Size())
            {
                _inputSize = gray.Size();
                _viewport = null;
            }
            if (_reference == null || _viewport is not { } previousView)
            {
                return false;
            }

            // 排除固定 UI、底部日志和详情面板，地图点位以外的真实地形也必须共同支持变换。
            var mapArea = BigMapUiLayout.GetMapArea(current);
            using var mask = new Mat(current.Size(), MatType.CV_8UC1, Scalar.All(0));
            var left = current.Width / 5;
            var right = Math.Min(current.Width * 4 / 5, mapArea.Right - 20);
            if (right <= left)
            {
                _viewport = null;
                return false;
            }
            Cv2.Rectangle(mask, new Rect(left, current.Height / 6, right - left, current.Height * 2 / 3), Scalar.White, -1);
            var points = Cv2.GoodFeaturesToTrack(_reference, 160, 0.02, 10, mask, 3, false, 0.04);
            if (points.Length < 16)
            {
                _viewport = null;
                return false;
            }
            Point2f[] next = [];
            Point2f[] back = [];
            Cv2.CalcOpticalFlowPyrLK(_reference, current, points, ref next, out var forwardStatus,
                out var errors, new Size(21, 21), 3, new TermCriteria(CriteriaTypes.Count | CriteriaTypes.Eps, 20, 0.03),
                OpticalFlowFlags.None, 0.0001);
            Cv2.CalcOpticalFlowPyrLK(current, _reference, next, ref back, out var backwardStatus,
                out _, new Size(21, 21), 3, new TermCriteria(CriteriaTypes.Count | CriteriaTypes.Eps, 20, 0.03),
                OpticalFlowFlags.None, 0.0001);
            var accepted = Enumerable.Range(0, points.Length).Where(i =>
                forwardStatus[i] != 0 && backwardStatus[i] != 0 && errors[i] < 15 &&
                next[i].X >= left && next[i].X < right && next[i].Y >= 0 && next[i].Y < current.Height &&
                Math.Abs(points[i].X - back[i].X) + Math.Abs(points[i].Y - back[i].Y) < 1.5).ToArray();
            if (accepted.Length < 16)
            {
                _viewport = null;
                return false;
            }

            // 将当前屏幕映射回上一屏幕，再复合上一帧的世界视口；只允许缩放和平移。
            var fit = BigMapFeatureGeometry.Fit(accepted.Select(i => next[i]).ToArray(),
                accepted.Select(i => points[i]).ToArray(), current.Size(), 0.75);
            if (fit == null || fit.Viewport.Width / current.Width is < 0.7 or > 1.4)
            {
                _viewport = null;
                return false;
            }
            var pixelScale = previousView.Width / current.Width;
            viewport = new Rect2d(previousView.X + fit.Viewport.X * pixelScale,
                previousView.Y + fit.Viewport.Y * pixelScale,
                fit.Viewport.Width * pixelScale, fit.Viewport.Height * pixelScale);
            _viewport = viewport;
            current.CopyTo(_reference);
            return true;
        }
    }

    /// <summary>关图或停用时释放参考图及视口，禁止跨会话跟踪。</summary>
    internal void Reset()
    {
        lock (_sync)
        {
            _reference?.Dispose();
            _reference = null;
            _viewport = null;
            _revision = -1;
            _inputSize = default;
        }
    }

    /// <summary>释放本实例拥有的缩略图。</summary>
    public void Dispose() => Reset();
}
