using System;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.MapMask;

/// <summary>比较可见地图主体，累计检测平移、缩放及面板变化，忽略小范围图标动画。</summary>
internal sealed class BigMapFrameTracker : IDisposable
{
    /// <summary>捕获与停用可以来自不同线程，缩略图由此锁保护。</summary>
    private readonly object _sync = new();

    /// <summary>上次发生有效变化时的地图缩略图，缓慢移动也会累计到阈值。</summary>
    private Mat? _reference;

    /// <summary>首帧或地图发生实质变化时返回 true，不改动调用方截图。</summary>
    internal bool HasChanged(Mat gray)
    {
        if (gray.Empty())
        {
            return false;
        }
        lock (_sync)
        {
            // 避开任务栏、顶部资源及底部日志；右侧保留，以检测详情面板的开关。
            var area = new Rect(gray.Width / 5, gray.Height / 10, gray.Width * 4 / 5, gray.Height * 4 / 5);
            using var map = new Mat(gray, area);
            using var small = new Mat();
            Cv2.Resize(map, small, new Size(320, 180), 0, 0, InterpolationFlags.Area);
            Cv2.GaussianBlur(small, small, new Size(3, 3), 0);
            if (_reference == null)
            {
                _reference = small.Clone();
                return true;
            }
            using var difference = new Mat();
            using var changed = new Mat();
            Cv2.Absdiff(small, _reference, difference);
            Cv2.Threshold(difference, changed, 12, 255, ThresholdTypes.Binary);
            if (Cv2.Mean(difference).Val0 < 1.2 || Cv2.CountNonZero(changed) < small.Total() * 0.02)
            {
                return false;
            }
            small.CopyTo(_reference);
            return true;
        }
    }

    /// <summary>关闭地图或停用后释放上一会话缩略图。</summary>
    internal void Reset()
    {
        lock (_sync)
        {
            _reference?.Dispose();
            _reference = null;
        }
    }

    /// <summary>释放本实例拥有的缩略图。</summary>
    public void Dispose() => Reset();
}
