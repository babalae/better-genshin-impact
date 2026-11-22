using System;
using System.Linq;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace BetterGenshinImpact.GameTask.Music.Service;

internal sealed class InstrumentRingTracker
{
    private const int KeyCount = 21;
    private const int History = 6;
    private const int MinRadius = 60;
    private const int MaxRadius = 103;
    private static readonly int[] Columns = [458, 626, 792, 960, 1128, 1295, 1462];
    private static readonly int[] Rows = [674, 809, 944];
    private static readonly (Point Mid, Point Inner, Point Outer)[][] Samples = MakeSamples();
    private readonly (int Radius, double Strength)[,] _history = new (int, double)[History, KeyCount];
    private int _next;
    private int _count;

    public void Reset() => (_next, _count) = (0, 0);

    // 同一琴键两侧都有亮边且连续六帧向内收缩，排除固定琴键边缘和背景。
    public unsafe uint Observe(Mat frame)
    {
        if (frame.Width != 1920 || frame.Height != 1080 || (frame.Type() != MatType.CV_8UC3 && frame.Type() != MatType.CV_8UC4))
        {
            Reset();
            return 0;
        }

        var channels = frame.Channels();
        long step = frame.Step();
        uint keys = 0;
        for (int key = 0; key < KeyCount; key++)
        {
            int x = Columns[key % 7], y = Rows[key / 7];
            byte* center = (byte*)frame.Data + y * step + x * channels;
            var peak = (Radius: 0, Strength: double.NegativeInfinity);
            for (int radius = MinRadius; radius <= MaxRadius; radius++)
            {
                double left = 0, right = 0;
                int nl = 0, nr = 0;
                foreach (var s in Samples[radius - MinRadius])
                {
                    if (y + s.Mid.Y > 1014) continue;
                    double edge = Brightness(center + s.Mid.Y * step + s.Mid.X * channels);
                    double inside = Brightness(center + s.Inner.Y * step + s.Inner.X * channels);
                    double outside = Brightness(center + s.Outer.Y * step + s.Outer.X * channels);
                    double strength = Math.Clamp(edge - (inside + outside) / 2, -4, 22);
                    if (s.Mid.X < 0) { left += strength; nl++; }
                    else { right += strength; nr++; }
                }
                float bothSides = (float)Math.Min(left / nl, right / nr);
                if (bothSides > peak.Strength) peak = (radius, bothSides);
            }
            _history[_next, key] = peak;
            if (_count >= History - 1 && IsContracting(key)) keys |= 1u << key;
        }
        _count = Math.Min(History, _count + 1);
        _next = (_next + 1) % History;
        GC.KeepAlive(frame);
        return keys;
    }

    private bool IsContracting(int key)
    {
        int oldest = (_next + 1) % History;
        var first = _history[oldest, key];
        var last = _history[_next, key];
        if (first.Radius < 82 || last.Radius > 86 || first.Radius - last.Radius < 10) return false;
        int previous = first.Radius;
        for (int i = 0; i < History; i++)
        {
            var current = _history[(oldest + i) % History, key];
            if (current.Strength < 5 || previous - current.Radius is < -1 or > 9) return false;
            previous = current.Radius;
        }
        return true;
    }

    private static unsafe byte Brightness(byte* pixel) => Math.Max(pixel[0], Math.Max(pixel[1], pixel[2]));

    private static (Point, Point, Point)[][] MakeSamples()
    {
        var angles = Enumerable.Range(0, 64).Select(i => 2 * Math.PI * i / 64)
            .Where(a => Math.Abs(Math.Cos(a)) >= .59).ToArray();
        return Enumerable.Range(MinRadius, MaxRadius - MinRadius + 1)
            .Select(r => angles.Select(a => (Offset(r, a), Offset(r-4, a), Offset(r+4, a))).ToArray()).ToArray();
    }
    private static Point Offset(int r, double a) => new((int)Math.Round(r * Math.Cos(a)), (int)Math.Round(r * Math.Sin(a)));
}
